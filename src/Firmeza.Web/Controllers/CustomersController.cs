using Firmeza.Web.Data;
using Firmeza.Web.Models;
using Firmeza.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Firmeza.Web.Controllers;

[Authorize(Roles = "Administrador")]
public sealed class CustomersController(ApplicationDbContext db) : Controller
{
    public async Task<IActionResult> Index(string? search)
    {
        var query = db.Customers.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(c => c.FirstName.Contains(search) || c.LastName.Contains(search) || c.DocumentNumber.Contains(search));
        ViewBag.Search = search;
        return View(await query.OrderBy(c => c.LastName).ThenBy(c => c.FirstName).ToListAsync());
    }

    public IActionResult Create() => View(new CustomerFormViewModel());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CustomerFormViewModel model)
    {
        await ValidateUniqueFields(model);
        if (!ModelState.IsValid) return View(model);
        db.Customers.Add(ToEntity(model));
        await db.SaveChangesAsync();
        TempData["Success"] = "Cliente creado correctamente.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int id)
    {
        var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
        return customer is null ? NotFound() : View(ToViewModel(customer));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, CustomerFormViewModel model)
    {
        if (id != model.Id) return BadRequest();
        await ValidateUniqueFields(model, id);
        if (!ModelState.IsValid) return View(model);
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == id);
        if (customer is null) return NotFound();
        Apply(model, customer);
        await db.SaveChangesAsync();
        TempData["Success"] = "Cliente actualizado correctamente.";
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Delete(int id)
    {
        var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
        return customer is null ? NotFound() : View(customer);
    }

    [HttpPost, ActionName("Delete"), ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == id);
        if (customer is null) return NotFound();
        if (await db.Sales.AnyAsync(s => s.CustomerId == id))
        {
            TempData["Error"] = "No se puede eliminar porque el cliente tiene ventas asociadas.";
            return RedirectToAction(nameof(Index));
        }
        db.Customers.Remove(customer);
        await db.SaveChangesAsync();
        TempData["Success"] = "Cliente eliminado correctamente.";
        return RedirectToAction(nameof(Index));
    }

    private async Task ValidateUniqueFields(CustomerFormViewModel model, int? id = null)
    {
        if (await db.Customers.AnyAsync(c => c.DocumentNumber == model.DocumentNumber && (!id.HasValue || c.Id != id.Value)))
            ModelState.AddModelError(nameof(model.DocumentNumber), "Este documento ya está registrado.");
        if (!string.IsNullOrWhiteSpace(model.Email) && await db.Customers.AnyAsync(c => c.Email == model.Email && (!id.HasValue || c.Id != id.Value)))
            ModelState.AddModelError(nameof(model.Email), "Este correo ya está registrado.");
    }

    private static Customer ToEntity(CustomerFormViewModel model)
    {
        var entity = new Customer(); Apply(model, entity); return entity;
    }

    private static void Apply(CustomerFormViewModel model, Customer entity)
    {
        entity.FirstName = model.FirstName.Trim(); entity.LastName = model.LastName.Trim();
        entity.DocumentNumber = model.DocumentNumber.Trim(); entity.DocumentType = model.DocumentType.Trim();
        entity.Email = model.Email?.Trim(); entity.Phone = model.Phone?.Trim(); entity.Address = model.Address?.Trim(); entity.Age = model.Age;
    }

    private static CustomerFormViewModel ToViewModel(Customer c) => new()
    {
        Id = c.Id, FirstName = c.FirstName, LastName = c.LastName, DocumentNumber = c.DocumentNumber,
        DocumentType = c.DocumentType, Email = c.Email, Phone = c.Phone, Address = c.Address, Age = c.Age
    };
}
