using System.ComponentModel.DataAnnotations;

namespace Firmeza.Web.Models;

public sealed class Sale
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    [Required, StringLength(24)]
    public string Status { get; set; } = "Pendiente";

    [StringLength(40)]
    public string? ExternalReference { get; set; }

    public decimal Subtotal { get; set; }
    public decimal TaxRate { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal Total { get; set; }
    public ICollection<SaleDetail> Details { get; set; } = new List<SaleDetail>();
}
