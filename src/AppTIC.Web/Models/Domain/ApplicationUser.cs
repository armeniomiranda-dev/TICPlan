using Microsoft.AspNetCore.Identity;

namespace AppTIC.Web.Models.Domain;

public class ApplicationUser : IdentityUser
{
    public string? NomeCompleto { get; set; }
    public string? Entidade { get; set; }
    public bool Ativo { get; set; } = true;
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;

    /// <summary>Operador do utilizador (perfil Utilizador): só vê a informação deste operador.</summary>
    public int? OperadorId { get; set; }
    public Operador? Operador { get; set; }
}
