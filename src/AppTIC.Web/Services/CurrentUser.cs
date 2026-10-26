using System.Security.Claims;

namespace AppTIC.Web.Services;

public interface ICurrentUser
{
    string? UserId { get; }
    string? UserName { get; }
    bool IsAdmin { get; }
    string? IpAddress { get; }
}

public class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public string? UserId => Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
    public string? UserName => Principal?.Identity?.Name;
    public bool IsAdmin => Principal?.IsInRole(Roles.Admin) ?? false;
    public string? IpAddress => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
}

public static class Roles
{
    public const string Admin = "Admin";
    public const string Utilizador = "Utilizador";
    public static readonly string[] Todos = [Admin, Utilizador];
}
