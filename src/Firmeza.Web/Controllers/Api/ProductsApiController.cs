using Firmeza.Web.Contracts;
using Firmeza.Web.Data;
using Firmeza.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Firmeza.Web.Controllers.Api;

[ApiController]
[Authorize(Roles = "Administrador")]
[Route("api/products")]
public sealed class ProductsApiController(ApplicationDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ProductResponse>>> List(
        [FromQuery] string? search,
        [FromQuery] string? category,
        [FromQuery] bool includeInactive = false,
        CancellationToken cancellationToken = default)
    {
        var query = db.Products.AsNoTracking().AsQueryable();
        if (!includeInactive) query = query.Where(product => product.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(product => product.Name.Contains(search) || product.Sku.Contains(search));
        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(product => product.Category == category);

        var products = await query.OrderBy(product => product.Name).ToListAsync(cancellationToken);
        return Ok(products.Select(ToResponse).ToList());
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ProductResponse>> Get(int id, CancellationToken cancellationToken)
    {
        var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(product => product.Id == id, cancellationToken);
        return product is null ? NotFound() : Ok(ToResponse(product));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<ActionResult<ProductResponse>> Create(ProductRequest request, CancellationToken cancellationToken)
    {
        var sku = request.Sku.Trim();
        if (await db.Products.IgnoreQueryFilters().AnyAsync(product => product.Sku == sku, cancellationToken))
            return Conflict(new { message = "Ya existe un producto con este código." });

        var product = new Product();
        Apply(request, product);
        db.Products.Add(product);
        await db.SaveChangesAsync(cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = product.Id }, ToResponse(product));
    }

    [HttpPut("{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<ActionResult<ProductResponse>> Update(int id, ProductRequest request, CancellationToken cancellationToken)
    {
        var product = await db.Products.IgnoreQueryFilters().FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (product is null) return NotFound();
        var sku = request.Sku.Trim();
        if (await db.Products.IgnoreQueryFilters().AnyAsync(item => item.Sku == sku && item.Id != id, cancellationToken))
            return Conflict(new { message = "Ya existe un producto con este código." });

        Apply(request, product);
        await db.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(product));
    }

    [HttpDelete("{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var product = await db.Products.IgnoreQueryFilters().FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (product is null) return NotFound();
        product.IsActive = false;
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private static void Apply(ProductRequest request, Product product)
    {
        product.Sku = request.Sku.Trim();
        product.Name = request.Name.Trim();
        product.Description = request.Description?.Trim();
        product.Category = request.Category.Trim();
        product.UnitOfMeasure = request.UnitOfMeasure.Trim();
        product.UnitPrice = request.UnitPrice;
        product.Stock = request.Stock;
        product.MinimumStock = request.MinimumStock;
        product.IsActive = request.IsActive;
    }

    private static ProductResponse ToResponse(Product product) => new(
        product.Id, product.Sku, product.Name, product.Description, product.Category,
        product.UnitOfMeasure, product.UnitPrice, product.Stock, product.MinimumStock, product.IsActive);
}
