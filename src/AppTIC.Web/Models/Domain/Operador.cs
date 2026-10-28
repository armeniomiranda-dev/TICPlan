using System.ComponentModel.DataAnnotations;

namespace AppTIC.Web.Models.Domain;

/// <summary>Folha "info_operador".</summary>
public class Operador : OwnedEntity
{
    [Required, StringLength(20), Display(Name = "NIF")]
    public string Nif { get; set; } = string.Empty;

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

    public List<Circulacao> Circulacoes { get; set; } = [];
    public List<GtfsFeed> GtfsFeeds { get; set; } = [];

    public static IReadOnlyList<string> SplitStopIds(string? value) =>
        (value ?? "").Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct().ToList();
}
