using System.ComponentModel.DataAnnotations;
using Firmeza.Web.Models;
using Xunit;

namespace Firmeza.Tests;

public sealed class DomainValidationTests
{
    [Fact]
    public void Product_RequiresPositivePriceAndNonNegativeStock()
    {
        var product = new Product
        {
            Sku = "CEM-50", Name = "Cemento", Category = "Cementos", UnitOfMeasure = "saco",
            UnitPrice = 0, Stock = -1
        };
        var results = Validate(product);
        Assert.Contains(results, result => result.MemberNames.Contains(nameof(Product.UnitPrice)));
        Assert.Contains(results, result => result.MemberNames.Contains(nameof(Product.Stock)));
    }

    [Fact]
    public void Customer_RejectsAgeOutsideSupportedRange()
    {
        var customer = new Customer
        {
            FirstName = "Ana", LastName = "Pérez", DocumentNumber = "1234567890",
            DocumentType = "Cédula", Age = 17
        };
        var results = Validate(customer);
        Assert.Contains(results, result => result.MemberNames.Contains(nameof(Customer.Age)));
    }

    private static List<ValidationResult> Validate(object instance)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(instance, new ValidationContext(instance), results, validateAllProperties: true);
        return results;
    }
}
