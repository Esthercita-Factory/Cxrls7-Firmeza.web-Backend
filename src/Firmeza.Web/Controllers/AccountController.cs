using System.ComponentModel.DataAnnotations;
using Firmeza.Web.Data;
using Firmeza.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Firmeza.Web.Controllers;

public sealed class AccountController(
    UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signInManager,
    ApplicationDbContext db) : Controller
{
    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        return View(new LoginViewModel());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        if (!ModelState.IsValid) return View(model);
        var user = await users.FindByEmailAsync(model.Email);
        if (user is null || !await users.IsInRoleAsync(user, "Administrador"))
        {
            ModelState.AddModelError(string.Empty, "Esta cuenta no tiene acceso al panel administrativo.");
            return View(model);
        }
        var result = await signInManager.PasswordSignInAsync(user, model.Password, model.RememberMe, lockoutOnFailure: true);
        if (result.Succeeded)
            return !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)
                ? LocalRedirect(returnUrl)
                : RedirectToAction("Index", "Home");
        ModelState.AddModelError(string.Empty, result.IsLockedOut
            ? "La cuenta está temporalmente bloqueada. Intenta más tarde."
            : "Correo o contraseña incorrectos.");
        return View(model);
    }

    [HttpGet]
    public IActionResult Register() => View(new RegisterViewModel());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (!ModelState.IsValid) return View(model);
        if (await db.Customers.AnyAsync(c => c.DocumentNumber == model.DocumentNumber))
        {
            ModelState.AddModelError(nameof(model.DocumentNumber), "Este documento ya está registrado.");
            return View(model);
        }
        var customer = new Customer
        {
            FirstName = model.FirstName.Trim(), LastName = model.LastName.Trim(),
            DocumentType = model.DocumentType, DocumentNumber = model.DocumentNumber.Trim(),
            Email = model.Email.Trim(), Phone = model.Phone?.Trim()
        };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        var user = new ApplicationUser
        {
            UserName = model.Email.Trim(), Email = model.Email.Trim(), FullName = $"{model.FirstName.Trim()} {model.LastName.Trim()}", CustomerId = customer.Id
        };
        var creation = await users.CreateAsync(user, model.Password);
        if (!creation.Succeeded)
        {
            db.Customers.Remove(customer);
            await db.SaveChangesAsync();
            foreach (var error in creation.Errors) ModelState.AddModelError(string.Empty, error.Description);
            return View(model);
        }
        var roleResult = await users.AddToRoleAsync(user, "Cliente");
        if (!roleResult.Succeeded)
        {
            await users.DeleteAsync(user);
            db.Customers.Remove(customer);
            await db.SaveChangesAsync();
            foreach (var error in roleResult.Errors) ModelState.AddModelError(string.Empty, error.Description);
            return View(model);
        }
        TempData["Success"] = "Cuenta creada. El acceso a la administración está reservado para administradores.";
        return RedirectToAction(nameof(Login));
    }

    [HttpPost, Authorize, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await signInManager.SignOutAsync();
        return RedirectToAction(nameof(Login));
    }

    [HttpGet]
    public IActionResult AccessDenied() => View();
}

public sealed class LoginViewModel
{
    [Required, EmailAddress, Display(Name = "Correo electrónico")]
    public string Email { get; set; } = string.Empty;
    [Required, DataType(DataType.Password), Display(Name = "Contraseña")]
    public string Password { get; set; } = string.Empty;
    [Display(Name = "Mantener sesión")]
    public bool RememberMe { get; set; }
}

public sealed class RegisterViewModel
{
    [Required, StringLength(80), Display(Name = "Nombre")]
    public string FirstName { get; set; } = string.Empty;
    [Required, StringLength(80), Display(Name = "Apellido")]
    public string LastName { get; set; } = string.Empty;
    [Required, StringLength(32), Display(Name = "Tipo de documento")]
    public string DocumentType { get; set; } = "Cédula";
    [Required, StringLength(24), Display(Name = "Número de documento")]
    public string DocumentNumber { get; set; } = string.Empty;
    [Required, EmailAddress, StringLength(254), Display(Name = "Correo electrónico")]
    public string Email { get; set; } = string.Empty;
    [Phone, StringLength(24), Display(Name = "Teléfono")]
    public string? Phone { get; set; }
    [Required, StringLength(100, MinimumLength = 10), DataType(DataType.Password), Display(Name = "Contraseña")]
    public string Password { get; set; } = string.Empty;
    [Required, Compare(nameof(Password)), DataType(DataType.Password), Display(Name = "Confirmar contraseña")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
