using System.ComponentModel.DataAnnotations;

namespace AppTIC.Web.Models.Domain;

/// <summary>Folha "paragens".</summary>
public class Paragem : OwnedEntity
{
    [Required, StringLength(50), Display(Name = "Código da paragem")]
    public string Codigo { get; set; } = string.Empty;

    [Required, StringLength(200), Display(Name = "Nome")]
    public string Nome { get; set; } = string.Empty;

    [Range(-90, 90), Display(Name = "Latitude")]
    public decimal? Latitude { get; set; }

    [Range(-180, 180), Display(Name = "Longitude")]
    public decimal? Longitude { get; set; }

    [Display(Name = "Comentários")]
    public string? Comentarios { get; set; }
}
