using Microsoft.AspNetCore.Identity;

namespace Firmeza.Web.Models;

public sealed class ApplicationUser : IdentityUser
{
    public string? FullName { get; set; }
    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }
}
