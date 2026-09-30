using Firmeza.Web.Contracts;
using Firmeza.Web.Data;
using Firmeza.Web.Models;
using Firmeza.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Firmeza.Web.Controllers.Api;

[ApiController]
[Authorize(Roles = "Administrador")]
[Route("api/sales")]
public sealed class SalesApiController(
    ApplicationDbContext db,
    PdfDocumentService pdfDocuments,
    IConfiguration configuration,
    ILogger<SalesApiController> logger,
    IWebHostEnvironment environment) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SaleResponse>>> List(
        [FromQuery] string? status,
        CancellationToken cancellationToken)
    {
        var query = db.Sales.AsNoTracking().Include(sale => sale.Customer).AsQueryable();
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(sale => sale.Status == status);
        var sales = await query.OrderByDescending(sale => sale.CreatedAtUtc).ToListAsync(cancellationToken);
        return Ok(sales.Select(sale => ToResponse(sale, [])).ToList());
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<SaleResponse>> Get(int id, CancellationToken cancellationToken)
    {
        var sale = await LoadSaleAsync(id, cancellationToken);
        return sale is null ? NotFound() : Ok(ToResponse(sale, sale.Details.Select(ToLineResponse).ToList()));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<ActionResult<SaleResponse>> Create(SaleRequest request, CancellationToken cancellationToken)
    {
        var taxRate = configuration.GetValue<decimal?>("TaxRate") ?? 0.15m;
        if (taxRate is < 0 or > 1)
        {
            logger.LogError("La configuración TaxRate debe estar entre 0 y 1.");
            return Problem("No se pudo calcular el impuesto. Contacta al administrador técnico.");
        }

        var requested = request.Items.GroupBy(item => item.ProductId)
            .Select(group => new { ProductId = group.Key, Quantity = group.Sum(item => item.Quantity) }).ToList();
        var customer = await db.Customers.FirstOrDefaultAsync(
            item => item.Id == request.CustomerId, cancellationToken);
        if (customer is null) return BadRequest(new { message = "Selecciona un cliente válido." });

        var productIds = requested.Select(item => item.ProductId).ToArray();
        var products = await db.Products.Where(product => product.IsActive && productIds.Contains(product.Id))
            .ToListAsync(cancellationToken);
        if (products.Count != requested.Count)
            return BadRequest(new { message = "Uno o más productos seleccionados no están disponibles." });
        var insufficient = requested.FirstOrDefault(item =>
            products.Single(product => product.Id == item.ProductId).Stock < item.Quantity);
        if (insufficient is not null)
        {
            var product = products.Single(item => item.Id == insufficient.ProductId);
            return Conflict(new { message = $"Stock insuficiente para {product.Name}. Disponible: {product.Stock}." });
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        string? receiptPath = null;
        try
        {
            var sale = new Sale
            {
                CustomerId = customer.Id,
                CreatedAtUtc = DateTime.UtcNow,
                Status = "Completada",
                TaxRate = taxRate
            };
            foreach (var line in requested)
            {
                var product = products.Single(item => item.Id == line.ProductId);
                var updatedRows = await db.Products
                    .Where(item => item.Id == product.Id && item.IsActive && item.Stock >= line.Quantity)
                    .ExecuteUpdateAsync(
                        setters => setters.SetProperty(item => item.Stock, item => item.Stock - line.Quantity),
                        cancellationToken);
                if (updatedRows == 0)
                    throw new InsufficientStockException(product.Name);

                var lineTotal = Math.Round(product.UnitPrice * line.Quantity, 2);
                sale.Details.Add(new SaleDetail
                {
                    ProductId = product.Id,
                    Quantity = line.Quantity,
                    UnitPrice = product.UnitPrice,
                    LineTotal = lineTotal
                });
            }

            sale.Subtotal = sale.Details.Sum(detail => detail.LineTotal);
            sale.TaxAmount = Math.Round(sale.Subtotal * sale.TaxRate, 2);
            sale.Total = sale.Subtotal + sale.TaxAmount;
            db.Sales.Add(sale);
            await db.SaveChangesAsync(cancellationToken);
            sale.ExternalReference = $"F-{sale.Id:D6}";
            await db.SaveChangesAsync(cancellationToken);
            await db.Entry(sale).Reference(item => item.Customer).LoadAsync(cancellationToken);
            foreach (var detail in sale.Details)
                await db.Entry(detail).Reference(item => item.Product).LoadAsync(cancellationToken);

            receiptPath = await pdfDocuments.SaveReceiptAsync(sale, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return CreatedAtAction(nameof(Get), new { id = sale.Id },
                ToResponse(sale, sale.Details.Select(ToLineResponse).ToList()));
        }
        catch (InsufficientStockException exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Conflict(new
            {
                message = $"El inventario de {exception.ProductName} cambió y ya no alcanza para esta venta. Revisa el stock e inténtalo de nuevo."
            });
        }
        catch (Exception exception)
        {
            await transaction.RollbackAsync(cancellationToken);
            if (receiptPath is not null && System.IO.File.Exists(receiptPath))
                System.IO.File.Delete(receiptPath);
            logger.LogError(exception, "Error al registrar la venta y generar su recibo desde la API.");
            return Problem("No se pudo registrar la venta. Verifica los datos e inténtalo nuevamente.");
        }
    }

    [HttpGet("{id:int}/receipt")]
    public async Task<IActionResult> Receipt(int id, CancellationToken cancellationToken)
    {
        var sale = await LoadSaleAsync(id, cancellationToken);
        if (sale is null) return NotFound();
        var webRoot = environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot");
        var receiptPath = Path.Combine(webRoot, "recibos", $"recibo-{sale.Id:D6}.pdf");
        if (!System.IO.File.Exists(receiptPath))
            await pdfDocuments.SaveReceiptAsync(sale, cancellationToken);
        return PhysicalFile(receiptPath, "application/pdf",
            $"recibo-{sale.ExternalReference ?? sale.Id.ToString("D6")}.pdf");
    }

    private Task<Sale?> LoadSaleAsync(int id, CancellationToken cancellationToken) =>
        db.Sales.AsNoTracking()
            .Include(sale => sale.Customer)
            .Include(sale => sale.Details).ThenInclude(detail => detail.Product)
            .FirstOrDefaultAsync(sale => sale.Id == id, cancellationToken);

    private static SaleResponse ToResponse(Sale sale, IReadOnlyList<SaleLineResponse> details) => new(
        sale.Id, sale.ExternalReference ?? sale.Id.ToString(), sale.Customer.FullName, sale.Status,
        sale.CreatedAtUtc, sale.Subtotal, sale.TaxAmount, sale.Total, details);

    private static SaleLineResponse ToLineResponse(SaleDetail detail) => new(
        detail.ProductId, detail.Product.Name, detail.Quantity, detail.UnitPrice, detail.LineTotal);

    private sealed class InsufficientStockException(string productName) : Exception
    {
        public string ProductName { get; } = productName;
    }
}
