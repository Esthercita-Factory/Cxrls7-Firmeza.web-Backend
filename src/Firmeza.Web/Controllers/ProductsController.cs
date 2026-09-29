using Firmeza.Web.Data;
using Firmeza.Web.Models;
using Firmeza.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Firmeza.Web.Controllers;

[Authorize(Roles = "Administrador")]
public sealed class ProductsController(ApplicationDbContext db) : Controller
{
    public async Task<IActionResult> Index(string? search, string? category)
    {
        var query = db.Products.AsNoTracking().Where(p => p.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(p => p.Name.Contains(search) || p.Sku.Contains(search));
        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(p => p.Category == category);
        ViewBag.Search = search;
        ViewBag.Category = category;
        ViewBag.Categories = await db.Products.Select(p => p.Category).Distinct().OrderBy(c => c).ToListAsync();
        return View(await query.OrderBy(p => p.Name).ToListAsync());
    }

    public IActionResult Create() => View(new ProductFormViewModel());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ProductFormViewModel model)
    {
        if (await db.Products.IgnoreQueryFilters().AnyAsync(p => p.Sku == model.Sku))
            ModelState.AddModelError(nameof(model.Sku), "Ya existe un producto con este código.");
        if (!ModelState.IsValid) return View(model);
        db.Products.Add(ToEntity(model));
        await db.SaveChangesAsync();
        TempData["Success"] = "Producto creado correctamente.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id)
    {
        var product = await db.Products.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
        return product is null ? NotFound() : View(ToViewModel(product));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, ProductFormViewModel model)
    {
        if (id != model.Id) return BadRequest();
        if (await db.Products.IgnoreQueryFilters().AnyAsync(p => p.Sku == model.Sku && p.Id != id))
            ModelState.AddModelError(nameof(model.Sku), "Ya existe un producto con este código.");
        if (!ModelState.IsValid) return View(model);
        var product = await db.Products.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == id);
        if (product is null) return NotFound();
        Apply(model, product);
        await db.SaveChangesAsync();
        TempData["Success"] = "Producto actualizado correctamente.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Delete(int id)
    {
        var product = await db.Products.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
        return product is null ? NotFound() : View(product);
    }

    [HttpPost, ActionName("Delete"), ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var product = await db.Products.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == id);
        if (product is null) return NotFound();
        product.IsActive = false;
        await db.SaveChangesAsync();
        TempData["Success"] = "Producto eliminado del catálogo.";
        return RedirectToAction(nameof(Index));
    }

    private static Product ToEntity(ProductFormViewModel model)
    {
        var entity = new Product();
        Apply(model, entity);
        return entity;
    }

    private static void Apply(ProductFormViewModel model, Product entity)
    {
        entity.Sku = model.Sku.Trim(); entity.Name = model.Name.Trim(); entity.Description = model.Description?.Trim();
        entity.Category = model.Category.Trim(); entity.UnitOfMeasure = model.UnitOfMeasure.Trim();
        entity.UnitPrice = model.UnitPrice; entity.Stock = model.Stock; entity.MinimumStock = model.MinimumStock;
        entity.IsActive = model.IsActive;
    }

    private static ProductFormViewModel ToViewModel(Product p) => new()
    {
        Id = p.Id, Sku = p.Sku, Name = p.Name, Description = p.Description, Category = p.Category,
        UnitOfMeasure = p.UnitOfMeasure, UnitPrice = p.UnitPrice, Stock = p.Stock, MinimumStock = p.MinimumStock, IsActive = p.IsActive
    };
}
