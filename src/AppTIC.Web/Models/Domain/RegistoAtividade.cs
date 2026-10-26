using System.ComponentModel.DataAnnotations;

namespace AppTIC.Web.Models.Domain;

public enum NivelRegisto
{
    Info = 0,
    Aviso = 1,
    Erro = 2
}

public static class CategoriaRegisto
{
    public const string Sessao = "Sessão";
    public const string Gtfs = "GTFS";
    public const string Dados = "Dados";
    public const string Administracao = "Administração";
}

/// <summary>Registo (log) de atividade por login: sessões, GTFS e alterações de dados.</summary>
public class RegistoAtividade : IOwnedEntity
{
    public long Id { get; set; }
    public string OwnerId { get; set; } = string.Empty;
    public ApplicationUser? Owner { get; set; }

    public DateTime Data { get; set; } = DateTime.UtcNow;
    public NivelRegisto Nivel { get; set; }

    [StringLength(50)]
    public string Categoria { get; set; } = string.Empty;

    [StringLength(100)]
    public string Acao { get; set; } = string.Empty;

    public string Mensagem { get; set; } = string.Empty;

    public int? GtfsFeedId { get; set; }
    public GtfsFeed? GtfsFeed { get; set; }

    [StringLength(256)]
    public string? UserName { get; set; }

    [StringLength(64)]
    public string? EnderecoIp { get; set; }
}
