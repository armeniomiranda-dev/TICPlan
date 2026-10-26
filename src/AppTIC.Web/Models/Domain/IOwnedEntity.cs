namespace AppTIC.Web.Models.Domain;

/// <summary>
/// Registo que pertence a um login. Os dados são filtrados por OwnerId,
/// exceto para utilizadores com o perfil Admin.
/// </summary>
public interface IOwnedEntity
{
    string OwnerId { get; set; }
    ApplicationUser? Owner { get; set; }
}

public abstract class OwnedEntity : IOwnedEntity
{
    public int Id { get; set; }
    public string OwnerId { get; set; } = string.Empty;
    public ApplicationUser? Owner { get; set; }
    public DateTime CriadoEm { get; set; }
    public DateTime AtualizadoEm { get; set; }
}
