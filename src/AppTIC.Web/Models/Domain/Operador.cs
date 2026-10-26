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

    public List<Circulacao> Circulacoes { get; set; } = [];
}
