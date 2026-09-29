using System.Text;
using Firmeza.Web.Data;
using Firmeza.Web.Models;
using Firmeza.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using OfficeOpenXml;
using OfficeOpenXml.Style;

namespace Firmeza.Web.Controllers;

[Authorize(Roles = "Administrador")]
public sealed class ReportsController(
    ApplicationDbContext db,
    BulkImportService importer,
    PdfDocumentService pdfDocuments,
    IMemoryCache cache,
    ILogger<ReportsController> logger) : Controller
{
    [HttpGet]
    public IActionResult Index() => View(new ReportsPageViewModel());

    [HttpPost, ValidateAntiForgeryToken, RequestSizeLimit(10_000_000)]
    public async Task<IActionResult> Import(IFormFile? file, CancellationToken cancellationToken)
    {
        var model = new ReportsPageViewModel();
        if (file is null || file.Length == 0)
        {
            ModelState.AddModelError("", "Selecciona un archivo Excel .xlsx.");
            return View("Index", model);
        }
        if (!string.Equals(Path.GetExtension(file.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError("", "El archivo debe tener formato .xlsx.");
            return View("Index", model);
        }
        if (file.Length > 10_000_000)
        {
            ModelState.AddModelError("", "El archivo no puede superar 10 MB.");
            return View("Index", model);
        }

        try
        {
            await using var stream = file.OpenReadStream();
            model.ImportResult = await importer.ImportAsync(stream, cancellationToken);
            if (model.ImportResult.Issues.Count > 0)
            {
                model.LogToken = Guid.NewGuid().ToString("N");
                cache.Set($"import-log:{model.LogToken}", model.ImportResult.Issues, TimeSpan.FromMinutes(30));
            }
            logger.LogInformation("Excel import processed. {SuccessfulRows} changes, {IssueCount} issues.", model.ImportResult.SuccessfulRows, model.ImportResult.Issues.Count);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Excel import failed.");
            ModelState.AddModelError("", "No se pudo procesar el archivo. Verifica que sea un libro .xlsx válido y vuelve a intentarlo.");
        }
        return View("Index", model);
    }

    [HttpGet]
    public IActionResult DownloadImportLog(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || !cache.TryGetValue($"import-log:{token}", out List<ImportIssue>? issues) || issues is null)
            return NotFound();
        var csv = new StringBuilder("Hoja;Fila;Inconsistencia\r\n");
        foreach (var issue in issues)
            csv.Append(Csv(issue.Worksheet)).Append(';').Append(issue.Row).Append(';').Append(Csv(issue.Message)).Append("\r\n");
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray(), "text/csv; charset=utf-8", "log-importacion-firmeza.csv");
    }

    [HttpGet]
    public IActionResult Template()
    {
        using var package = new ExcelPackage();
        var sheet = package.Workbook.Worksheets.Add("Datos");
        var headers = new[] { "SKU", "Producto", "Categoría", "Unidad", "Precio", "Stock", "Tipo documento", "Documento", "Nombre cliente", "Apellido", "Correo", "Teléfono", "Número venta", "Fecha venta", "Cantidad", "Estado venta" };
        for (var i = 0; i < headers.Length; i++)
        {
            var cell = sheet.Cells[1, i + 1];
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Font.Color.SetColor(System.Drawing.Color.White);
            cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
            cell.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(36, 91, 73));
        }
        sheet.View.FreezePanes(2, 1);
        sheet.Cells[1, 1, 1, headers.Length].AutoFilter = true;
        sheet.Cells[1, 1, 1, headers.Length].AutoFitColumns();
        return File(package.GetAsByteArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "plantilla-importacion-firmeza.xlsx");
    }

    [HttpGet]
    public async Task<IActionResult> Products(string format, CancellationToken cancellationToken)
    {
        var products = await db.Products.AsNoTracking().OrderBy(p => p.Name).ToListAsync(cancellationToken);
        var headers = new[] { "SKU", "Producto", "Categoría", "Unidad", "Precio", "Stock", "Estado" };
        var rows = products.Select(p => (IReadOnlyList<string>)[p.Sku, p.Name, p.Category, p.UnitOfMeasure, p.UnitPrice.ToString("F2"), p.Stock.ToString(), p.IsActive ? "Activo" : "Inactivo"]).ToList();
        return Export("Productos", headers, rows, format);
    }

    [HttpGet]
    public async Task<IActionResult> Customers(string format, CancellationToken cancellationToken)
    {
        var customers = await db.Customers.AsNoTracking().OrderBy(c => c.LastName).ThenBy(c => c.FirstName).ToListAsync(cancellationToken);
        var headers = new[] { "Nombre", "Tipo documento", "Documento", "Correo", "Teléfono", "Dirección", "Registrado" };
        var rows = customers.Select(c => (IReadOnlyList<string>)[c.FullName, c.DocumentType, c.DocumentNumber, c.Email ?? "", c.Phone ?? "", c.Address ?? "", c.CreatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd")]).ToList();
        return Export("Clientes", headers, rows, format);
    }

    [HttpGet]
    public async Task<IActionResult> Sales(string format, CancellationToken cancellationToken)
    {
        var sales = await db.Sales.AsNoTracking().Include(s => s.Customer).OrderByDescending(s => s.CreatedAtUtc).ToListAsync(cancellationToken);
        var headers = new[] { "Número", "Cliente", "Documento", "Fecha", "Estado", "Subtotal", "IVA", "Total" };
        var rows = sales.Select(s => (IReadOnlyList<string>)[s.ExternalReference ?? s.Id.ToString(), s.Customer.FullName, s.Customer.DocumentNumber, s.CreatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"), s.Status, s.Subtotal.ToString("F2"), s.TaxAmount.ToString("F2"), s.Total.ToString("F2")]).ToList();
        return Export("Ventas", headers, rows, format);
    }

    private IActionResult Export(string title, IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows, string format)
    {
        if (string.Equals(format, "pdf", StringComparison.OrdinalIgnoreCase))
            return File(pdfDocuments.CreateTableReport(title, headers, rows), "application/pdf", $"firmeza-{title.ToLowerInvariant()}.pdf");
        if (!string.Equals(format, "xlsx", StringComparison.OrdinalIgnoreCase)) return BadRequest("Formato no soportado. Usa xlsx o pdf.");
        using var package = new ExcelPackage();
        var sheet = package.Workbook.Worksheets.Add(title);
        for (var col = 0; col < headers.Count; col++) sheet.Cells[1, col + 1].Value = headers[col];
        for (var row = 0; row < rows.Count; row++)
            for (var col = 0; col < headers.Count; col++) sheet.Cells[row + 2, col + 1].Value = rows[row][col];
        using (var header = sheet.Cells[1, 1, 1, headers.Count])
        {
            header.Style.Font.Bold = true;
            header.Style.Font.Color.SetColor(System.Drawing.Color.White);
            header.Style.Fill.PatternType = ExcelFillStyle.Solid;
            header.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(36, 91, 73));
        }
        sheet.Cells[sheet.Dimension!.Address].AutoFitColumns();
        return File(package.GetAsByteArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"firmeza-{title.ToLowerInvariant()}.xlsx");
    }

    private static string Csv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
}

public sealed class ReportsPageViewModel
{
    public BulkImportResult? ImportResult { get; set; }
    public string? LogToken { get; set; }
}
