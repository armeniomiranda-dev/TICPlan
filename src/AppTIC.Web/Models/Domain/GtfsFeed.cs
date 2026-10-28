using System.ComponentModel.DataAnnotations;

namespace AppTIC.Web.Models.Domain;

public enum GtfsFeedEstado
{
    [Display(Name = "Carregado")] Carregado = 0,
    [Display(Name = "Válido")] Valido = 1,
    [Display(Name = "Válido com avisos")] ValidoComAvisos = 2,
    [Display(Name = "Inválido")] Invalido = 3
}

/// <summary>Ficheiro GTFS (.zip) carregado por um login.</summary>
public class GtfsFeed : OwnedEntity
{
    [Display(Name = "Operador")]
    public int? OperadorId { get; set; }
    public Operador? Operador { get; set; }

    [Required, StringLength(200), Display(Name = "Nome")]
    public string Nome { get; set; } = string.Empty;

    [Display(Name = "Descrição")]
    public string? Descricao { get; set; }

    [StringLength(260), Display(Name = "Ficheiro")]
    public string NomeFicheiroOriginal { get; set; } = string.Empty;

    /// <summary>Caminho relativo à pasta de armazenamento GTFS.</summary>
    [StringLength(500)]
    public string CaminhoArmazenamento { get; set; } = string.Empty;

    [Display(Name = "Tamanho (bytes)")]
    public long TamanhoBytes { get; set; }

    [StringLength(64)]
    public string Sha256 { get; set; } = string.Empty;

    [Display(Name = "Estado")]
    public GtfsFeedEstado Estado { get; set; }

    [Display(Name = "Início do feed")]
    public DateOnly? DataInicio { get; set; }

    [Display(Name = "Fim do feed")]
    public DateOnly? DataFim { get; set; }

    [StringLength(200), Display(Name = "Editor do feed")]
    public string? Editor { get; set; }

    [StringLength(100), Display(Name = "Versão do feed")]
    public string? Versao { get; set; }

    public int NumAgencias { get; set; }
    public int NumParagens { get; set; }
    public int NumRotas { get; set; }
    public int NumViagens { get; set; }
    public int NumHorarios { get; set; }

    // ---- Filtragem pelas paragens do TIC ----

    /// <summary>stop_id(s) usados na última filtragem (separados por vírgula).</summary>
    [StringLength(1000)]
    public string? ParagensTic { get; set; }

    public int NumViagensTic { get; set; }
    public int NumHorariosTic { get; set; }
    public int NumRotasTic { get; set; }
    public int NumParagensTic { get; set; }

    /// <summary>Caminho (relativo) do GTFS filtrado só com as viagens que passam no TIC.</summary>
    [StringLength(500)]
    public string? CaminhoFiltrado { get; set; }

    public long? TamanhoFiltradoBytes { get; set; }

    public List<RegistoAtividade> Registos { get; set; } = [];
}
