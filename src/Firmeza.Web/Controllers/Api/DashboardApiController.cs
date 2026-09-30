using Firmeza.Web.Contracts;
using Firmeza.Web.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Firmeza.Web.Controllers.Api;

[ApiController]
[Authorize(Roles = "Administrador")]
[Route("api/dashboard")]
public sealed class DashboardApiController(ApplicationDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<DashboardResponse>> Get(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var monthSales = db.Sales.AsNoTracking().Where(sale => sale.CreatedAtUtc >= monthStart);
        var monthlyRevenue = await monthSales.Select(sale => (decimal?)sale.Total)
            .SumAsync(cancellationToken) ?? 0m;
        var monthlyCount = await monthSales.CountAsync(cancellationToken);
        var productCount = await db.Products.CountAsync(product => product.IsActive, cancellationToken);
        var customerCount = await db.Customers.CountAsync(cancellationToken);
        var recentSales = await db.Sales.AsNoTracking()
            .Include(sale => sale.Customer)
            .OrderByDescending(sale => sale.CreatedAtUtc)
            .Take(6)
            .Select(sale => new SaleResponse(
                sale.Id,
                sale.ExternalReference ?? sale.Id.ToString(),
                sale.Customer.FirstName + " " + sale.Customer.LastName,
                sale.Status,
                sale.CreatedAtUtc,
                sale.Subtotal,
                sale.TaxAmount,
                sale.Total,
                Array.Empty<SaleLineResponse>()))
            .ToListAsync(cancellationToken);

        return Ok(new DashboardResponse(productCount, customerCount, monthlyCount, monthlyRevenue, recentSales));
    }
}
