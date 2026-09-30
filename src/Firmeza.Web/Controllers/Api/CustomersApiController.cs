using Firmeza.Web.Contracts;
using Firmeza.Web.Data;
using Firmeza.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Firmeza.Web.Controllers.Api;

[ApiController]
[Authorize(Roles = "Administrador")]
[Route("api/customers")]
public sealed class CustomersApiController(ApplicationDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CustomerResponse>>> List(
        [FromQuery] string? search,
        CancellationToken cancellationToken)
    {
        var query = db.Customers.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(customer =>
                customer.FirstName.Contains(search) ||
                customer.LastName.Contains(search) ||
                customer.DocumentNumber.Contains(search));
        var customers = await query.OrderBy(customer => customer.LastName)
            .ThenBy(customer => customer.FirstName).ToListAsync(cancellationToken);
        return Ok(customers.Select(ToResponse).ToList());
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<CustomerResponse>> Get(int id, CancellationToken cancellationToken)
    {
        var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        return customer is null ? NotFound() : Ok(ToResponse(customer));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<ActionResult<CustomerResponse>> Create(CustomerRequest request, CancellationToken cancellationToken)
    {
        var duplicate = await FindDuplicateAsync(request, null, cancellationToken);
        if (duplicate is not null) return Conflict(new { message = duplicate });

        var customer = new Customer();
        Apply(request, customer);
        db.Customers.Add(customer);
        await db.SaveChangesAsync(cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = customer.Id }, ToResponse(customer));
    }

    [HttpPut("{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<ActionResult<CustomerResponse>> Update(int id, CustomerRequest request, CancellationToken cancellationToken)
    {
        var customer = await db.Customers.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (customer is null) return NotFound();
        var duplicate = await FindDuplicateAsync(request, id, cancellationToken);
        if (duplicate is not null) return Conflict(new { message = duplicate });

        Apply(request, customer);
        await db.SaveChangesAsync(cancellationToken);
        return Ok(ToResponse(customer));
    }

    [HttpDelete("{id:int}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var customer = await db.Customers.FirstOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (customer is null) return NotFound();
        if (await db.Sales.AnyAsync(sale => sale.CustomerId == id, cancellationToken))
            return Conflict(new { message = "No se puede eliminar porque el cliente tiene ventas asociadas." });

        db.Customers.Remove(customer);
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private async Task<string?> FindDuplicateAsync(CustomerRequest request, int? excludedId, CancellationToken cancellationToken)
    {
        var documentNumber = request.DocumentNumber.Trim();
        if (await db.Customers.AnyAsync(customer =>
                customer.DocumentNumber == documentNumber && (!excludedId.HasValue || customer.Id != excludedId.Value),
                cancellationToken))
            return "Este documento ya está registrado.";

        var email = request.Email?.Trim();
        if (!string.IsNullOrWhiteSpace(email) &&
            await db.Customers.AnyAsync(customer =>
                customer.Email == email && (!excludedId.HasValue || customer.Id != excludedId.Value),
                cancellationToken))
            return "Este correo ya está registrado.";
        return null;
    }

    private static void Apply(CustomerRequest request, Customer customer)
    {
        customer.FirstName = request.FirstName.Trim();
        customer.LastName = request.LastName.Trim();
        customer.DocumentNumber = request.DocumentNumber.Trim();
        customer.DocumentType = request.DocumentType.Trim();
        customer.Email = request.Email?.Trim();
        customer.Phone = request.Phone?.Trim();
        customer.Address = request.Address?.Trim();
        customer.Age = request.Age;
    }

    private static CustomerResponse ToResponse(Customer customer) => new(
        customer.Id, customer.FirstName, customer.LastName, customer.FullName, customer.DocumentNumber,
        customer.DocumentType, customer.Email, customer.Phone, customer.Address, customer.Age, customer.CreatedAtUtc);
}
