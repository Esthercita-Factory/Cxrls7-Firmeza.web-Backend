using Firmeza.Web.Data;
using Firmeza.Web.Models;
using Firmeza.Web.Services;
using Firmeza.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Firmeza.Web.Controllers;

[Authorize(Roles = "Administrador")]
public sealed class SalesController(ApplicationDbContext db, PdfDocumentService pdfDocuments, IConfiguration configuration, ILogger<SalesController> logger, IWebHostEnvironment environment) : Controller
{
    public async Task<IActionResult> Index(string? status)
    {
        var query = db.Sales.AsNoTracking().Include(s => s.Customer).AsQueryable();
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(s => s.Status == status);
        ViewBag.Status = status;
        return View(await query.OrderByDescending(s => s.CreatedAtUtc).ToListAsync());
    }

    public async Task<IActionResult> Details(int id)
    {
        var sale = await db.Sales.AsNoTracking().Include(s => s.Customer)
            .Include(s => s.Details).ThenInclude(d => d.Product).FirstOrDefaultAsync(s => s.Id == id);
        return sale is null ? NotFound() : View(sale);
    }

    public async Task<IActionResult> Create()
    {
        await PopulateOptionsAsync();
        ViewBag.TaxRate = configuration.GetValue<decimal?>("TaxRate") ?? 0.15m;
        return View(new SaleCreateViewModel { Items = [new SaleLineInputViewModel()] });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(SaleCreateViewModel model, CancellationToken cancellationToken)
    {
        var taxRate = configuration.GetValue<decimal?>("TaxRate") ?? 0.15m;
        var requested = model.Items.Where(item => item.ProductId.HasValue).GroupBy(item => item.ProductId!.Value)
            .Select(group => new { ProductId = group.Key, Quantity = group.Sum(item => item.Quantity) }).ToList();
        var requestedProductIds = requested.Select(item => item.ProductId).ToArray();
        if (requested.Count == 0) ModelState.AddModelError(nameof(model.Items), "Agrega al menos un producto a la venta.");
        if (requested.Any(item => item.Quantity <= 0)) ModelState.AddModelError(nameof(model.Items), "Las cantidades deben ser enteros mayores que cero.");
        var customer = model.CustomerId.HasValue ? await db.Customers.FirstOrDefaultAsync(c => c.Id == model.CustomerId.Value, cancellationToken) : null;
        if (customer is null) ModelState.AddModelError(nameof(model.CustomerId), "Selecciona un cliente válido.");
        var products = await db.Products.Where(p => p.IsActive && requestedProductIds.Contains(p.Id)).ToListAsync(cancellationToken);
        if (products.Count != requested.Count) ModelState.AddModelError(nameof(model.Items), "Uno o más productos seleccionados no están disponibles.");
        foreach (var line in requested)
        {
            var product = products.FirstOrDefault(p => p.Id == line.ProductId);
            if (product is not null && product.Stock < line.Quantity)
                ModelState.AddModelError(nameof(model.Items), $"Stock insuficiente para {product.Name}. Disponible: {product.Stock}.");
        }
        if (!ModelState.IsValid)
        {
            await PopulateOptionsAsync(cancellationToken);
            ViewBag.TaxRate = taxRate;
            return View(model);
        }

        if (taxRate is < 0 or > 1)
        {
            logger.LogError("La configuración TaxRate debe estar entre 0 y 1.");
            ModelState.AddModelError(string.Empty, "No se pudo calcular el impuesto. Contacta al administrador técnico.");
            await PopulateOptionsAsync(cancellationToken);
            ViewBag.TaxRate = taxRate;
            return View(model);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        string? receiptPath = null;
        try
        {
            var now = DateTime.UtcNow;
            var sale = new Sale { CustomerId = customer!.Id, CreatedAtUtc = now, Status = "Completada", TaxRate = taxRate };
            foreach (var line in requested)
            {
                var product = products.Single(p => p.Id == line.ProductId);
                var stockUpdated = await db.Products.Where(p => p.Id == product.Id && p.IsActive && p.Stock >= line.Quantity)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(p => p.Stock, p => p.Stock - line.Quantity), cancellationToken);
                if (stockUpdated == 0) throw new InsufficientStockException(product.Name);
                var lineTotal = Math.Round(product.UnitPrice * line.Quantity, 2);
                sale.Details.Add(new SaleDetail { ProductId = product.Id, Quantity = line.Quantity, UnitPrice = product.UnitPrice, LineTotal = lineTotal });
            }
            sale.Subtotal = sale.Details.Sum(detail => detail.LineTotal);
            sale.TaxAmount = Math.Round(sale.Subtotal * sale.TaxRate, 2);
            sale.Total = sale.Subtotal + sale.TaxAmount;
            db.Sales.Add(sale);
            await db.SaveChangesAsync(cancellationToken);
            sale.ExternalReference = $"F-{sale.Id:D6}";
            await db.SaveChangesAsync(cancellationToken);
            await db.Entry(sale).Reference(s => s.Customer).LoadAsync(cancellationToken);
            foreach (var detail in sale.Details)
                await db.Entry(detail).Reference(d => d.Product).LoadAsync(cancellationToken);
            receiptPath = await pdfDocuments.SaveReceiptAsync(sale, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            TempData["Success"] = $"Venta {sale.ExternalReference} registrada y recibo PDF generado.";
            return RedirectToAction(nameof(Details), new { id = sale.Id });
        }
        catch (InsufficientStockException exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            ModelState.AddModelError(nameof(model.Items), $"El inventario de {exception.ProductName} cambió y ya no alcanza para esta venta. Revisa el stock e inténtalo de nuevo.");
            await PopulateOptionsAsync(cancellationToken);
            ViewBag.TaxRate = taxRate;
            return View(model);
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            if (receiptPath is not null && System.IO.File.Exists(receiptPath)) System.IO.File.Delete(receiptPath);
            logger.LogError(exception, "Error al registrar la venta y generar su recibo.");
            ModelState.AddModelError(string.Empty, "No se pudo registrar la venta. Verifica los datos e inténtalo nuevamente.");
            await PopulateOptionsAsync(cancellationToken);
            ViewBag.TaxRate = taxRate;
            return View(model);
        }
    }

    [HttpGet]
    public async Task<IActionResult> Receipt(int id, CancellationToken cancellationToken)
    {
        var sale = await db.Sales.AsNoTracking().Include(s => s.Customer)
            .Include(s => s.Details).ThenInclude(detail => detail.Product)
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
        if (sale is null) return NotFound();
        var receiptPath = Path.Combine(environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot"), "recibos", $"recibo-{sale.Id:D6}.pdf");
        if (!System.IO.File.Exists(receiptPath)) await pdfDocuments.SaveReceiptAsync(sale, cancellationToken);
        return PhysicalFile(receiptPath, "application/pdf", $"recibo-{sale.ExternalReference ?? sale.Id.ToString("D6")}.pdf");
    }

    private async Task PopulateOptionsAsync(CancellationToken cancellationToken = default)
    {
        ViewBag.Customers = await db.Customers.AsNoTracking().OrderBy(c => c.LastName).ThenBy(c => c.FirstName).ToListAsync(cancellationToken);
        ViewBag.Products = await db.Products.AsNoTracking().Where(p => p.IsActive).OrderBy(p => p.Name).ToListAsync(cancellationToken);
    }

    private sealed class InsufficientStockException(string productName) : Exception
    {
        public string ProductName { get; } = productName;
    }
}
