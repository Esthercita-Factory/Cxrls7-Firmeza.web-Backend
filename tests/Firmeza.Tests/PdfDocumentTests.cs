using System.Text;
using Firmeza.Web.Models;
using Firmeza.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using QuestPDF.Infrastructure;
using Xunit;

namespace Firmeza.Tests;

public sealed class PdfDocumentTests
{
    [Fact]
    public void CreateReceipt_ReturnsPdfDocument()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var service = new PdfDocumentService(new TestWebHostEnvironment());
        var sale = new Sale
        {
            Id = 12,
            ExternalReference = "F-000012",
            CreatedAtUtc = DateTime.UtcNow,
            Status = "Completada",
            Subtotal = 100,
            TaxRate = 0.15m,
            TaxAmount = 15,
            Total = 115,
            Customer = new Customer { FirstName = "Ana", LastName = "Pérez", DocumentType = "Cédula", DocumentNumber = "1234567890" },
            Details = [new SaleDetail
            {
                Quantity = 2,
                UnitPrice = 50,
                LineTotal = 100,
                Product = new Product { Name = "Cemento", Sku = "CEM-50" }
            }]
        };

        var pdf = service.CreateReceipt(sale);

        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Firmeza.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = Path.GetTempPath();
        public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
