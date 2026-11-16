using System.ComponentModel.DataAnnotations;

namespace AppTIC.Web.Models.Domain;

public enum ImportacaoOrigem
{
    [Display(Name = "GTFS")] Gtfs = 0,
    [Display(Name = "Excel")] Excel = 1
}

public enum ImportacaoEstado
{
    /// <summary>Ficheiro carregado, ainda não aplicado às circulações.</summary>
    [Display(Name = "Por aplicar")] Pendente = 0,
    [Display(Name = "Aplicada")] Aplicada = 1
}

/// <summary>
/// Importação em bloco de paragens, circulações e calendário (de um feed GTFS ou da folha Excel de planeamento).
/// O Id identifica o lote: as circulações criadas ou atualizadas ficam com <see cref="Circulacao.ImportacaoId"/>,
/// o que permite eliminar, atualizar ou voltar a importar o lote.
/// </summary>
public class Importacao : OwnedEntity
{
    public ImportacaoOrigem Origem { get; set; }
    public ImportacaoEstado Estado { get; set; }

    [Required, StringLength(200), Display(Name = "Nome")]
    public string Nome { get; set; } = string.Empty;

    /// <summary>Feed de origem (importações GTFS).</summary>
    public int? GtfsFeedId { get; set; }
    public GtfsFeed? GtfsFeed { get; set; }

    /// <summary>route_id(s) escolhidos na importação GTFS (um por linha), para repetir a importação.</summary>
    public string? Rotas { get; set; }

    // ---- Ficheiro Excel ----

    [StringLength(260), Display(Name = "Ficheiro")]
    public string? NomeFicheiro { get; set; }

    /// <summary>Caminho relativo à pasta de armazenamento (Gtfs:StoragePath).</summary>
    [StringLength(500)]
    public string? CaminhoArmazenamento { get; set; }

    public long TamanhoBytes { get; set; }

    [StringLength(64)]
    public string? Sha256 { get; set; }

    // ---- Resultado da última aplicação ----

    /// <summary>Intervalo de dias de calendário importado.</summary>
    public DateOnly? DataInicio { get; set; }
    public DateOnly? DataFim { get; set; }

    public int NumOperadores { get; set; }
    public int NumParagens { get; set; }
    public int NumCirculacoes { get; set; }
    public int NumDias { get; set; }

    /// <summary>Quantas vezes foi aplicada (1 = importação inicial; mais = reimportações/atualizações).</summary>
    public int Aplicacoes { get; set; }
    public DateTime? AplicadaEm { get; set; }

    public List<Circulacao> Circulacoes { get; set; } = [];
}
