using System.ComponentModel.DataAnnotations;

namespace Firmeza.Web.Models;

public sealed class Customer
{
    public int Id { get; set; }

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

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public ICollection<Sale> Sales { get; set; } = new List<Sale>();
    public ICollection<ApplicationUser> Users { get; set; } = new List<ApplicationUser>();

    public string FullName => $"{FirstName} {LastName}".Trim();
}
