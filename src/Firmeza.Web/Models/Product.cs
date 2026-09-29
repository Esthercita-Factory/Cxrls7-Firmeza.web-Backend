using System.ComponentModel.DataAnnotations;

namespace Firmeza.Web.Models;

public sealed class Product
{
    public int Id { get; set; }

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
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public ICollection<SaleDetail> SaleDetails { get; set; } = new List<SaleDetail>();
}
