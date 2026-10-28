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

        var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();

        var email = config["Seed:AdminEmail"];
        var password = config["Seed:AdminPassword"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            logger.LogInformation("Seed:AdminEmail/Seed:AdminPassword não definidos; nenhum Admin criado.");
        else
            await EnsureUserAsync(userManager, logger, new SeedUser(email, password, "Administrador", null, Roles.Admin));

        // Logins de teste (apenas configurados em appsettings.Development.json).
        foreach (var user in config.GetSection("Seed:TestUsers").Get<List<SeedUser>>() ?? [])
            await EnsureUserAsync(userManager, logger, user);
    }

    public record SeedUser(string Email, string Password, string? NomeCompleto, string? Entidade, string Perfil = Roles.Utilizador);

    private static async Task EnsureUserAsync(UserManager<ApplicationUser> userManager, ILogger logger, SeedUser seed)
    {
        if (string.IsNullOrWhiteSpace(seed.Email) || await userManager.FindByEmailAsync(seed.Email) is not null) return;

        var user = new ApplicationUser
        {
            UserName = seed.Email, Email = seed.Email, EmailConfirmed = true,
            NomeCompleto = seed.NomeCompleto, Entidade = seed.Entidade
        };
        var result = await userManager.CreateAsync(user, seed.Password);
        if (!result.Succeeded)
        {
            logger.LogError("Falha ao criar {Email}: {Erros}", seed.Email, string.Join("; ", result.Errors.Select(e => e.Description)));
            return;
        }
        await userManager.AddToRoleAsync(user, Roles.Todos.Contains(seed.Perfil) ? seed.Perfil : Roles.Utilizador);
        logger.LogInformation("Utilizador {Email} ({Perfil}) criado.", seed.Email, seed.Perfil);
    }
}
