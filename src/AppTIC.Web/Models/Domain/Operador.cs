using System.ComponentModel.DataAnnotations;

namespace AppTIC.Web.Models.Domain;

/// <summary>Folha "info_operador".</summary>
public class Operador : OwnedEntity
{
    /// <summary>Obrigatório exceto nos grupos.</summary>
    [StringLength(20), Display(Name = "NIF")]
    public string? Nif { get; set; }

    [Required, StringLength(200), Display(Name = "Designação")]
    public string Designacao { get; set; } = string.Empty;

    [StringLength(100), Display(Name = "Alvará (licença comunitária)")]
    public string? Alvara { get; set; }

    [Display(Name = "Validade do alvará")]
    public DateOnly? AlvaraValidade { get; set; }

    [Display(Name = "Âmbito")]
    public Ambito? Ambito { get; set; }

    [StringLength(30), Display(Name = "Abreviatura")]
    public string? Abreviatura { get; set; }

    [Display(Name = "Comentários")]
    public string? Comentarios { get; set; }

    /// <summary>
    /// stop_id(s) da paragem do TIC no GTFS deste operador (separados por vírgula ou linha), ex. "100" na RNE.
    /// Vazio = usa a lista Gtfs:ParagensTicPorOmissao.
    /// </summary>
    [StringLength(1000), Display(Name = "Paragens do TIC no GTFS (stop_id)")]
    public string? GtfsParagensTic { get; set; }

    /// <summary>Representa um grupo de operadores (ex.: Rede Nacional de Expressos). Os utilizadores do grupo veem os operadores do grupo.</summary>
    [Display(Name = "É um grupo de operadores")]
    public bool EGrupo { get; set; }

    /// <summary>Grupo a que o operador pertence.</summary>
    [Display(Name = "Grupo")]
    public int? GrupoId { get; set; }
    public Operador? Grupo { get; set; }
    public List<Operador> Membros { get; set; } = [];

    /// <summary>
    /// Prefixo da numeração do ID TIC: ID = prefixo seguido de 3 dígitos sequenciais (ex.: 8 → 8001…8999; 11 → 11001…11999).
    /// Num operador de um grupo vale o prefixo do grupo.
    /// </summary>
    [Range(1, 999, ErrorMessage = "O prefixo tem de estar entre 1 e 999."), Display(Name = "Prefixo do ID TIC")]
    public int? PrefixoIdTic { get; set; }

    /// <summary>Cor nas legendas e gráficos (#rrggbb). Sem cor: a do grupo; sem grupo, uma cor por omissão.</summary>
    [StringLength(9), Display(Name = "Cor")]
    public string? Cor { get; set; }

    public List<Circulacao> Circulacoes { get; set; } = [];
    public List<GtfsFeed> GtfsFeeds { get; set; } = [];

    public static IReadOnlyList<string> SplitStopIds(string? value) =>
        (value ?? "").Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct().ToList();
}
