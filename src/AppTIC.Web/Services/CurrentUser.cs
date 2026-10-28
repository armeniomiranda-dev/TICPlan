using System.Security.Claims;
using AppTIC.Web.Data;
using AppTIC.Web.Models.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AppTIC.Web.Services;

public interface ICurrentUser
{
    string? UserId { get; }
    string? UserName { get; }
    bool IsAdmin { get; }
    /// <summary>Operador do utilizador (claim gravada no início de sessão); null para o Admin ou sem operador.</summary>
    int? OperadorId { get; }
    string? IpAddress { get; }
}

public class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public string? UserId => Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
    public string? UserName => Principal?.Identity?.Name;
    public bool IsAdmin => Principal?.IsInRole(Roles.Admin) ?? false;
    public int? OperadorId => int.TryParse(Principal?.FindFirstValue(AppClaims.OperadorId), out var id) ? id : null;
    public string? IpAddress => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
}

public static class Roles
{
    public const string Admin = "Admin";
    public const string Utilizador = "Utilizador";
    public static readonly string[] Todos = [Admin, Utilizador];
}

public static class AppClaims
{
    public const string OperadorId = "apptic:operador_id";
    public const string OperadorNome = "apptic:operador_nome";
}

/// <summary>
/// Junta o operador do utilizador às claims da sessão. As claims são refeitas na validação do security stamp,
/// por isso uma mudança de operador chega às sessões abertas (o AdminController renova o stamp).
/// </summary>
public class AppClaimsPrincipalFactory(UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager,
    IOptions<IdentityOptions> options, ApplicationDbContext db)
    : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole>(userManager, roleManager, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        if (user.OperadorId is { } id)
        {
            // Sem filtros: no início de sessão ainda não há utilizador autenticado.
            var nome = await db.Operadores.IgnoreQueryFilters().Where(o => o.Id == id)
                .Select(o => o.Abreviatura ?? o.Designacao).FirstOrDefaultAsync();
            identity.AddClaim(new Claim(AppClaims.OperadorId, id.ToString()));
            if (nome is not null) identity.AddClaim(new Claim(AppClaims.OperadorNome, nome));
        }
        return identity;
    }
}
