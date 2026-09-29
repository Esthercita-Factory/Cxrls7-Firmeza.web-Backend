using Firmeza.Web.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Firmeza.Web.Controllers;

[Authorize(Roles = "Administrador")]
public sealed class HomeController(ApplicationDbContext db) : Controller
{
    public async Task<IActionResult> Index()
    {
        var today = DateTime.UtcNow.Date;
        var model = new DashboardViewModel(
            await db.Products.CountAsync(p => p.IsActive),
            await db.Customers.CountAsync(),
            await db.Sales.CountAsync(),
            await db.Sales.Where(s => s.CreatedAtUtc >= today).SumAsync(s => (decimal?)s.Total) ?? 0,
            await db.Sales.Include(s => s.Customer).OrderByDescending(s => s.CreatedAtUtc).Take(6).ToListAsync());
        return View(model);
    }

    [AllowAnonymous]
    public IActionResult Error() => View();
}

public sealed record DashboardViewModel(int ProductCount, int CustomerCount, int SaleCount, decimal TodayRevenue, IReadOnlyList<Firmeza.Web.Models.Sale> RecentSales);
