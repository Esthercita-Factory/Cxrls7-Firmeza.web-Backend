using System.ComponentModel.DataAnnotations;

namespace Firmeza.Web.Contracts;

public sealed record UserResponse(string Email, string Role, string FullName);

public sealed record ProductResponse(
    int Id,
    string Sku,
    string Name,
    string? Description,
    string Category,
    string UnitOfMeasure,
    decimal UnitPrice,
    int Stock,
    int MinimumStock,
    bool IsActive);

public sealed class ProductRequest
{
    [Required, StringLength(32)]
    public string Sku { get; set; } = string.Empty;

    [Required, StringLength(120)]
    public string Name { get; set; } = string.Empty;

    [StringLength(800)]
    public string? Description { get; set; }

    [Required, StringLength(80)]
    public string Category { get; set; } = string.Empty;

    [Required, StringLength(24)]
    public string UnitOfMeasure { get; set; } = "unidad";

    [Range(0.01, 999999999)]
    public decimal UnitPrice { get; set; }

    [Range(0, int.MaxValue)]
    public int Stock { get; set; }

    [Range(0, int.MaxValue)]
    public int MinimumStock { get; set; }

    public bool IsActive { get; set; } = true;
}

public sealed record CustomerResponse(
    int Id,
    string FirstName,
    string LastName,
    string FullName,
    string DocumentNumber,
    string DocumentType,
    string? Email,
    string? Phone,
    string? Address,
    int? Age,
    DateTime CreatedAtUtc);

public sealed class CustomerRequest
{
    [Required, StringLength(80)]
    public string FirstName { get; set; } = string.Empty;

    [Required, StringLength(80)]
    public string LastName { get; set; } = string.Empty;

    [Required, StringLength(24)]
    public string DocumentNumber { get; set; } = string.Empty;

    [Required, StringLength(32)]
    public string DocumentType { get; set; } = "Cédula";

    [EmailAddress, StringLength(254)]
    public string? Email { get; set; }

    [Phone, StringLength(24)]
    public string? Phone { get; set; }

    [StringLength(240)]
    public string? Address { get; set; }

    [Range(18, 120)]
    public int? Age { get; set; }
}

public sealed record DashboardResponse(
    int ProductCount,
    int CustomerCount,
    int MonthlySaleCount,
    decimal MonthlyRevenue,
    IReadOnlyList<SaleResponse> RecentSales);

public sealed record SaleResponse(
    int Id,
    string Reference,
    string Customer,
    string Status,
    DateTime CreatedAtUtc,
    decimal Subtotal,
    decimal TaxAmount,
    decimal Total,
    IReadOnlyList<SaleLineResponse> Details);

public sealed record SaleLineResponse(
    int ProductId,
    string Product,
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal);

public sealed class SaleRequest
{
    [Range(1, int.MaxValue)]
    public int CustomerId { get; set; }

    [Required, MinLength(1)]
    public List<SaleLineRequest> Items { get; set; } = [];
}

public sealed class SaleLineRequest
{
    [Range(1, int.MaxValue)]
    public int ProductId { get; set; }

    [Range(1, 100000)]
    public int Quantity { get; set; }
}

public sealed record ImportIssueResponse(string Worksheet, int Row, string Message);

public sealed record ImportResponse(int SuccessfulRows, IReadOnlyList<ImportIssueResponse> Issues);
