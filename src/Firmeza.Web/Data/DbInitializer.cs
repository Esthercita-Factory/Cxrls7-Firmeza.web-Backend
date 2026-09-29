using Firmeza.Web.Models;
using Microsoft.AspNetCore.Identity;

namespace Firmeza.Web.Data;

public static class DbInitializer
{
    public static async Task SeedAsync(IServiceProvider services, IConfiguration configuration)
    {
        using var scope = services.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        foreach (var role in new[] { "Administrador", "Cliente" })
        {
            if (!await roles.RoleExistsAsync(role))
            {
                var result = await roles.CreateAsync(new IdentityRole(role));
                if (!result.Succeeded)
                    throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));
            }
        }

        await EnsureAdminAsync(users, configuration);
    }

    public static async Task EnsureAdminAsync(UserManager<ApplicationUser> users, IConfiguration configuration)
    {
        var email = configuration["AdminSeed:Email"];
        var password = configuration["AdminSeed:Password"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            return;

        var admin = await users.FindByEmailAsync(email);
        if (admin is null)
        {
            admin = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                FullName = "Administrador Firmeza"
            };

            var createResult = await users.CreateAsync(admin, password);
            if (!createResult.Succeeded)
                throw new InvalidOperationException(string.Join("; ", createResult.Errors.Select(e => e.Description)));
        }
        else
        {
            var resetToken = await users.GeneratePasswordResetTokenAsync(admin);
            var resetResult = await users.ResetPasswordAsync(admin, resetToken, password);
            if (!resetResult.Succeeded)
                throw new InvalidOperationException(string.Join("; ", resetResult.Errors.Select(e => e.Description)));
        }

        if (!await users.IsInRoleAsync(admin, "Administrador"))
        {
            var result = await users.AddToRoleAsync(admin, "Administrador");
            if (!result.Succeeded)
                throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));
        }
    }
}
