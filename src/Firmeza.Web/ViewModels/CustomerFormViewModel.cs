using System.ComponentModel.DataAnnotations;

namespace Firmeza.Web.ViewModels;

public sealed class CustomerFormViewModel
{
    public int Id { get; set; }

    [Required, StringLength(80)]
    [Display(Name = "Nombre")]
    public string FirstName { get; set; } = string.Empty;

    [Required, StringLength(80)]
    [Display(Name = "Apellido")]
    public string LastName { get; set; } = string.Empty;

    [Required, StringLength(24)]
    [Display(Name = "Número de documento")]
    public string DocumentNumber { get; set; } = string.Empty;

    [Required, StringLength(32)]
    [Display(Name = "Tipo de documento")]
    public string DocumentType { get; set; } = "Cédula";

    [EmailAddress, StringLength(254)]
    [Display(Name = "Correo electrónico")]
    public string? Email { get; set; }

    [Phone, StringLength(24)]
    [Display(Name = "Teléfono")]
    public string? Phone { get; set; }

    [StringLength(240)]
    [Display(Name = "Dirección")]
    public string? Address { get; set; }

    [Range(18, 120, ErrorMessage = "La edad debe ser un número entero entre 18 y 120 años.")]
    [Display(Name = "Edad (opcional, persona natural)")]
    public int? Age { get; set; }
}
