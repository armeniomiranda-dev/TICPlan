using AppTIC.Web.Models.Domain;
using AppTIC.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AppTIC.Web.Data;

public static class DbSeeder
{
    /// <summary>Aplica migrações (se configurado), cria os perfis e o primeiro Admin.</summary>
    public static async Task SeedAsync(IServiceProvider services, IConfiguration config)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("DbSeeder");

        if (config.GetValue("Database:MigrateOnStartup", true))
            await sp.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();

        var roleManager = sp.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in Roles.Todos)
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));

        var email = config["Seed:AdminEmail"];
        var password = config["Seed:AdminPassword"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogInformation("Seed:AdminEmail/Seed:AdminPassword não definidos; nenhum Admin criado.");
            return;
        }

        var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
        if (await userManager.FindByEmailAsync(email) is not null) return;

        var admin = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true, NomeCompleto = "Administrador" };
        var result = await userManager.CreateAsync(admin, password);
        if (!result.Succeeded)
        {
            logger.LogError("Falha ao criar Admin: {Erros}", string.Join("; ", result.Errors.Select(e => e.Description)));
            return;
        }
        await userManager.AddToRoleAsync(admin, Roles.Admin);
        logger.LogInformation("Admin {Email} criado.", email);
    }
}
