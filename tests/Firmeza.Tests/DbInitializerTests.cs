using Firmeza.Web.Data;
using Firmeza.Web.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Firmeza.Tests;

public sealed class DbInitializerTests
{
    [Fact]
    public async Task EnsureAdminAsync_UpdatesExistingAdminPassword()
    {
        var databaseName = Guid.NewGuid().ToString();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddDbContext<ApplicationDbContext>(builder => builder.UseInMemoryDatabase(databaseName));
        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.Password.RequiredLength = 6;
                options.Password.RequireDigit = true;
                options.Password.RequireUppercase = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireNonAlphanumeric = false;
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();

        await using var provider = services.BuildServiceProvider();
        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = provider.GetRequiredService<RoleManager<IdentityRole>>();

        var roleCreateResult = await roleManager.CreateAsync(new IdentityRole("Administrador"));
        Assert.True(roleCreateResult.Succeeded);

        var admin = new ApplicationUser
        {
            UserName = "admin@firmeza.local",
            Email = "admin@firmeza.local",
            EmailConfirmed = true,
            FullName = "Administrador Firmeza"
        };

        var createResult = await userManager.CreateAsync(admin, "OldPass1!");
        Assert.True(createResult.Succeeded);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AdminSeed:Email"] = "admin@firmeza.local",
                ["AdminSeed:Password"] = "123456"
            })
            .Build();

        await DbInitializer.EnsureAdminAsync(userManager, configuration);

        Assert.True(await userManager.CheckPasswordAsync(admin, "123456"));
    }
}
