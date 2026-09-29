using Firmeza.Web.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Firmeza.Web.Services;

public sealed class PdfDocumentService(IWebHostEnvironment environment)
{
    public async Task<string> SaveReceiptAsync(Sale sale, CancellationToken cancellationToken = default)
    {
        var directory = Path.Combine(environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot"), "recibos");
        Directory.CreateDirectory(directory);
        var fileName = $"recibo-{sale.Id:D6}.pdf";
        var fullPath = Path.Combine(directory, fileName);
        var pdf = CreateReceipt(sale);
        await File.WriteAllBytesAsync(fullPath, pdf, cancellationToken);
        return fullPath;
    }

    public byte[] CreateReceipt(Sale sale) => Document.Create(container =>
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(42);
            page.DefaultTextStyle(style => style.FontFamily("Arial").FontSize(10).FontColor("#26352f"));
            page.Header().Column(column =>
            {
                column.Item().Row(row =>
                {
                    row.RelativeItem().Column(brand =>
                    {
                        brand.Item().Text("FIRMEZA").FontSize(22).Bold().FontColor("#245b49");
                        brand.Item().Text("MATERIALES DE CONSTRUCCIÓN").FontSize(8).LetterSpacing(1).FontColor("#788580");
                    });
                    row.ConstantItem(190).AlignRight().Column(meta =>
                    {
                        meta.Item().Text("RECIBO DE VENTA").Bold().FontSize(11);
                        meta.Item().Text($"N.º {sale.ExternalReference ?? $"F-{sale.Id:D6}"}");
                        meta.Item().Text(sale.CreatedAtUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm"));
                    });
                });
                column.Item().PaddingTop(15).LineHorizontal(1).LineColor("#dce5de");
            });
            page.Content().PaddingVertical(22).Column(column =>
            {
                column.Spacing(17);
                column.Item().Background("#f4f7f3").Padding(13).Column(customer =>
                {
                    customer.Item().Text("CLIENTE").FontSize(8).Bold().FontColor("#788580");
                    customer.Item().PaddingTop(4).Text(sale.Customer.FullName).Bold().FontSize(12);
                    customer.Item().Text($"{sale.Customer.DocumentType}: {sale.Customer.DocumentNumber}");
                    if (!string.IsNullOrWhiteSpace(sale.Customer.Email)) customer.Item().Text(sale.Customer.Email);
                    if (!string.IsNullOrWhiteSpace(sale.Customer.Phone)) customer.Item().Text(sale.Customer.Phone);
                });
                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(4);
                        columns.RelativeColumn(1);
                        columns.RelativeColumn(1.5f);
                        columns.RelativeColumn(1.5f);
                    });
                    table.Header(header =>
                    {
                        header.Cell().Element(HeaderCell).Text("PRODUCTO");
                        header.Cell().Element(HeaderCell).AlignRight().Text("CANT.");
                        header.Cell().Element(HeaderCell).AlignRight().Text("PRECIO");
                        header.Cell().Element(HeaderCell).AlignRight().Text("SUBTOTAL");
                    });
                    foreach (var line in sale.Details)
                    {
                        table.Cell().Element(BodyCell).Text(line.Product.Name);
                        table.Cell().Element(BodyCell).AlignRight().Text(line.Quantity.ToString());
                        table.Cell().Element(BodyCell).AlignRight().Text(line.UnitPrice.ToString("C2"));
                        table.Cell().Element(BodyCell).AlignRight().Text(line.LineTotal.ToString("C2"));
                    }
                });
                column.Item().AlignRight().Width(230).Column(totals =>
                {
                    totals.Item().Row(row => { row.RelativeItem().Text("Subtotal"); row.ConstantItem(100).AlignRight().Text(sale.Subtotal.ToString("C2")); });
                    totals.Item().PaddingTop(5).Row(row => { row.RelativeItem().Text($"IVA ({sale.TaxRate:P0})"); row.ConstantItem(100).AlignRight().Text(sale.TaxAmount.ToString("C2")); });
                    totals.Item().PaddingTop(8).LineHorizontal(1).LineColor("#dce5de");
                    totals.Item().PaddingTop(7).Row(row => { row.RelativeItem().Text("TOTAL").Bold().FontSize(13).FontColor("#245b49"); row.ConstantItem(100).AlignRight().Text(sale.Total.ToString("C2")).Bold().FontSize(13).FontColor("#245b49"); });
                });
            });
            page.Footer().AlignCenter().Text("Gracias por confiar en Firmeza · Conserva este recibo para tus registros").FontSize(8).FontColor("#788580");
        });
    }).GeneratePdf();

    public byte[] CreateTableReport(string title, IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows) => Document.Create(container =>
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4.Landscape());
            page.Margin(32);
            page.DefaultTextStyle(style => style.FontFamily("Arial").FontSize(8).FontColor("#26352f"));
            page.Header().Column(column =>
            {
                column.Item().Text("FIRMEZA").FontSize(18).Bold().FontColor("#245b49");
                column.Item().Text(title).FontSize(12).Bold();
                column.Item().PaddingTop(8).Text($"Generado: {DateTime.Now:dd/MM/yyyy HH:mm}").FontSize(8).FontColor("#788580");
            });
            page.Content().PaddingVertical(18).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    for (var i = 0; i < headers.Count; i++) columns.RelativeColumn();
                });
                table.Header(header =>
                {
                    foreach (var item in headers) header.Cell().Element(HeaderCell).Text(item);
                });
                foreach (var row in rows)
                    foreach (var item in row) table.Cell().Element(BodyCell).Text(item);
            });
            page.Footer().AlignRight().Text(text => { text.Span("Página "); text.CurrentPageNumber(); text.Span(" / "); text.TotalPages(); });
        });
    }).GeneratePdf();

    private static IContainer HeaderCell(IContainer container) => container.Background("#245b49").Padding(7).DefaultTextStyle(style => style.FontColor("#ffffff").Bold().FontSize(8));
    private static IContainer BodyCell(IContainer container) => container.BorderBottom(0.5f).BorderColor("#e6ebe7").Padding(7);
}
