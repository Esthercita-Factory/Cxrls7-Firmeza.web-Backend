using System.ComponentModel.DataAnnotations;

namespace Firmeza.Web.ViewModels;

public sealed class SaleCreateViewModel
{
    [Required(ErrorMessage = "Selecciona un cliente.")]
    [Display(Name = "Cliente")]
    public int? CustomerId { get; set; }

    public List<SaleLineInputViewModel> Items { get; set; } = [];
}

public sealed class SaleLineInputViewModel
{
    public int? ProductId { get; set; }
    [Range(1, 100000, ErrorMessage = "La cantidad debe ser mayor que cero.")]
    public int Quantity { get; set; } = 1;
}
