using Firmeza.Web.Services;
using Xunit;

namespace Firmeza.Tests;

public sealed class BulkImportTests
{
    [Theory]
    [InlineData("Número de Venta", "numerodeventa")]
    [InlineData("Nombre del Cliente", "nombredelcliente")]
    [InlineData(" SKU ", "sku")]
    public void NormalizeHeader_RemovesAccentsSpacesAndPunctuation(string source, string expected)
    {
        Assert.Equal(expected, BulkImportService.NormalizeHeader(source));
    }
}
