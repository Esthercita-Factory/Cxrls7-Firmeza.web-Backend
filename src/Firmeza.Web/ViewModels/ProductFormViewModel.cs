using System.ComponentModel.DataAnnotations;

namespace Firmeza.Web.ViewModels;

public sealed class ProductFormViewModel
{
    public int Id { get; set; }

    [Required, StringLength(32)]
    [Display(Name = "SKU / código")]
    public string Sku { get; set; } = string.Empty;

    [Required, StringLength(120)]
    [Display(Name = "Nombre")]
    public string Name { get; set; } = string.Empty;

    [StringLength(800)]
    [Display(Name = "Descripción")]
    public string? Description { get; set; }

    [Required, StringLength(80)]
    [Display(Name = "Categoría")]
    public string Category { get; set; } = string.Empty;

    [Required, StringLength(24)]
    [Display(Name = "Unidad de medida")]
    public string UnitOfMeasure { get; set; } = "unidad";

    [Range(0.01, 999999999, ErrorMessage = "El precio debe ser mayor que cero.")]
    [Display(Name = "Precio unitario")]
    public decimal UnitPrice { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "El inventario no puede ser negativo.")]
    [Display(Name = "Existencias")]
    public int Stock { get; set; }

    [Range(0, int.MaxValue)]
    [Display(Name = "Stock mínimo")]
    public int MinimumStock { get; set; }

    [Display(Name = "Activo")]
    public bool IsActive { get; set; } = true;
}
