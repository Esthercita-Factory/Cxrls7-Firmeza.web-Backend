using Firmeza.Web.Contracts;
using Firmeza.Web.Models;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Firmeza.Web.Controllers.Api;

[ApiController]
[Route("api/auth")]
public sealed class AuthApiController(
    UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signInManager,
    IAntiforgery antiforgery) : ControllerBase
{
    [HttpGet("csrf")]
    [IgnoreAntiforgeryToken]
    public IActionResult GetCsrfToken()
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        return Ok(new { token = tokens.RequestToken });
    }

    [HttpPost("login")]
    [ValidateAntiForgeryToken]
    public async Task<ActionResult<UserResponse>> Login(LoginRequest request)
    {
        var user = await users.FindByEmailAsync(request.Email);
        if (user is null || !await users.IsInRoleAsync(user, "Administrador"))
            return Unauthorized(new { message = "Esta cuenta no tiene acceso al panel administrativo." });

        var result = await signInManager.PasswordSignInAsync(
            user, request.Password, request.RememberMe, lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            var message = result.IsLockedOut
                ? "La cuenta está temporalmente bloqueada. Intenta más tarde."
                : "Correo o contraseña incorrectos.";
            return result.IsLockedOut
                ? StatusCode(StatusCodes.Status423Locked, new { message })
                : Unauthorized(new { message });
        }

        return Ok(ToResponse(user));
    }

    [HttpGet("me")]
    public async Task<ActionResult<UserResponse>> Me()
    {
        var user = await users.GetUserAsync(User);
        if (user is null || !await users.IsInRoleAsync(user, "Administrador"))
            return Unauthorized();
        return Ok(ToResponse(user));
    }

    [HttpPost("logout")]
    [Authorize]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await signInManager.SignOutAsync();
        return NoContent();
    }

    private static UserResponse ToResponse(ApplicationUser user) =>
        new(user.Email ?? user.UserName ?? string.Empty, "Administrador", user.FullName ?? user.Email ?? string.Empty);

    public sealed class LoginRequest
    {
        [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.EmailAddress]
        public string Email { get; set; } = string.Empty;

        [System.ComponentModel.DataAnnotations.Required]
        public string Password { get; set; } = string.Empty;

        public bool RememberMe { get; set; }
    }
}
