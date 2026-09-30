using Firmeza.Web.Contracts;
using Firmeza.Web.Data;
using Firmeza.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using OfficeOpenXml.Style;

namespace Firmeza.Web.Controllers.Api;

[ApiController]
[Authorize(Roles = "Administrador")]
[Route("api/reports")]
public sealed class ReportsApiController(
    ApplicationDbContext db,
    BulkImportService importer,
    PdfDocumentService pdfDocuments,
    ILogger<ReportsApiController> logger) : ControllerBase
{
    [HttpGet("{report}")]
    public async Task<IActionResult> Export(
        string report,
        [FromQuery] string format = "xlsx",
        CancellationToken cancellationToken = default)
    {
        var (title, headers, rows) = report.ToLowerInvariant() switch
        {
            "products" => await GetProductsAsync(cancellationToken),
            "customers" => await GetCustomersAsync(cancellationToken),
            "sales" => await GetSalesAsync(cancellationToken),
            _ => (null, null, null)
        };
        if (title is null || headers is null || rows is null)
            return NotFound(new { message = "El reporte solicitado no existe." });

        if (string.Equals(format, "pdf", StringComparison.OrdinalIgnoreCase))
            return File(pdfDocuments.CreateTableReport(title, headers, rows), "application/pdf",
                $"firmeza-{title.ToLowerInvariant()}.pdf");
        if (!string.Equals(format, "xlsx", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Formato no soportado. Usa xlsx o pdf." });

        using var package = new ExcelPackage();
        var sheet = package.Workbook.Worksheets.Add(title);
        for (var col = 0; col < headers.Count; col++) sheet.Cells[1, col + 1].Value = headers[col];
        for (var row = 0; row < rows.Count; row++)
            for (var col = 0; col < headers.Count; col++)
                sheet.Cells[row + 2, col + 1].Value = rows[row][col];
        using (var header = sheet.Cells[1, 1, 1, headers.Count])
        {
            header.Style.Font.Bold = true;
            header.Style.Font.Color.SetColor(System.Drawing.Color.White);
            header.Style.Fill.PatternType = ExcelFillStyle.Solid;
            header.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(36, 91, 73));
        }
        sheet.Cells[sheet.Dimension!.Address].AutoFitColumns();
        return File(package.GetAsByteArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"firmeza-{title.ToLowerInvariant()}.xlsx");
    }

    [HttpGet("import/template")]
    public IActionResult Template()
    {
        using var package = new ExcelPackage();
        var sheet = package.Workbook.Worksheets.Add("Datos");
        var headers = new[]
        {
            "SKU", "Producto", "Categoría", "Unidad", "Precio", "Stock", "Tipo documento",
            "Documento", "Nombre cliente", "Apellido", "Correo", "Teléfono", "Número venta",
            "Fecha venta", "Cantidad", "Estado venta"
        };
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
        return File(package.GetAsByteArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "plantilla-importacion-firmeza.xlsx");
    }

    [HttpPost("import")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(10_000_000)]
    public async Task<ActionResult<ImportResponse>> Import(
        [FromForm] IFormFile? file,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { message = "Selecciona un archivo Excel .xlsx." });
        if (!string.Equals(Path.GetExtension(file.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "El archivo debe tener formato .xlsx." });
        if (file.Length > 10_000_000)
            return BadRequest(new { message = "El archivo no puede superar 10 MB." });

        try
        {
            await using var stream = file.OpenReadStream();
            var result = await importer.ImportAsync(stream, cancellationToken);
            logger.LogInformation("API Excel import processed. {SuccessfulRows} changes, {IssueCount} issues.",
                result.SuccessfulRows, result.Issues.Count);
            return Ok(new ImportResponse(result.SuccessfulRows,
                result.Issues.Select(issue =>
                    new ImportIssueResponse(issue.Worksheet, issue.Row, issue.Message)).ToList()));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "API Excel import failed.");
            return Problem(
                "No se pudo procesar el archivo. Verifica que sea un libro .xlsx válido y vuelve a intentarlo.");
        }
    }

    private async Task<(string, IReadOnlyList<string>, IReadOnlyList<IReadOnlyList<string>>)> GetProductsAsync(
        CancellationToken cancellationToken)
    {
        var products = await db.Products.AsNoTracking().OrderBy(item => item.Name).ToListAsync(cancellationToken);
        var rows = products.Select(product => (IReadOnlyList<string>)
        [
            product.Sku, product.Name, product.Category, product.UnitOfMeasure,
            product.UnitPrice.ToString("F2"), product.Stock.ToString(), product.IsActive ? "Activo" : "Inactivo"
        ]).ToList();
        return ("Productos", ["SKU", "Producto", "Categoría", "Unidad", "Precio", "Stock", "Estado"], rows);
    }

    private async Task<(string, IReadOnlyList<string>, IReadOnlyList<IReadOnlyList<string>>)> GetCustomersAsync(
        CancellationToken cancellationToken)
    {
        var customers = await db.Customers.AsNoTracking()
            .OrderBy(item => item.LastName).ThenBy(item => item.FirstName).ToListAsync(cancellationToken);
        var rows = customers.Select(customer => (IReadOnlyList<string>)
        [
            customer.FullName, customer.DocumentType, customer.DocumentNumber, customer.Email ?? "",
            customer.Phone ?? "", customer.Address ?? "", customer.CreatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd")
        ]).ToList();
        return ("Clientes", ["Nombre", "Tipo documento", "Documento", "Correo", "Teléfono", "Dirección", "Registrado"], rows);
    }

    private async Task<(string, IReadOnlyList<string>, IReadOnlyList<IReadOnlyList<string>>)> GetSalesAsync(
        CancellationToken cancellationToken)
    {
        var sales = await db.Sales.AsNoTracking().Include(sale => sale.Customer)
            .OrderByDescending(sale => sale.CreatedAtUtc).ToListAsync(cancellationToken);
        var rows = sales.Select(sale => (IReadOnlyList<string>)
        [
            sale.ExternalReference ?? sale.Id.ToString(), sale.Customer.FullName, sale.Customer.DocumentNumber,
            sale.CreatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"), sale.Status,
            sale.Subtotal.ToString("F2"), sale.TaxAmount.ToString("F2"), sale.Total.ToString("F2")
        ]).ToList();
        return ("Ventas", ["Número", "Cliente", "Documento", "Fecha", "Estado", "Subtotal", "IVA", "Total"], rows);
    }
}
