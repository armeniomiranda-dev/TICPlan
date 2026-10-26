using System.ComponentModel.DataAnnotations;

namespace AppTIC.Web.Models.Domain;

/// <summary>Folha "circulações": uma viagem/circulação que passa no TIC.</summary>
public class Circulacao : OwnedEntity
{
    [Required, Display(Name = "Operador")]
    public int OperadorId { get; set; }
    public Operador? Operador { get; set; }

    [StringLength(50), Display(Name = "Linha comercial/bus")]
    public string? LinhaId { get; set; }

    [StringLength(200), Display(Name = "Designação da linha")]
    public string? DesignacaoLinha { get; set; }

    [Display(Name = "Âmbito")]
    public Ambito? Ambito { get; set; }

    [StringLength(100), Display(Name = "Nº autorização do serviço")]
    public string? Autorizacao { get; set; }

    [Display(Name = "Validade da autorização")]
    public DateOnly? AutorizacaoValidade { get; set; }

    [Display(Name = "Declaração de capacidade")]
    public bool DeclaracaoCapacidade { get; set; }

    [StringLength(50), Display(Name = "ID DCP")]
    public string? IdDcp { get; set; }

    [Required, StringLength(50), Display(Name = "ID de viagem / código de circulação")]
    public string ViagemId { get; set; } = string.Empty;

    [StringLength(50), Display(Name = "ID TIC")]
    public string? IdTic { get; set; }

    [Display(Name = "Paragem de origem")]
    public int? OrigemId { get; set; }
    public Paragem? Origem { get; set; }

    [Display(Name = "Paragem de destino")]
    public int? DestinoId { get; set; }
    public Paragem? Destino { get; set; }

    [Display(Name = "Chegada ao TIC")]
    public TimeOnly? Chegada { get; set; }

    [Display(Name = "Partida do TIC")]
    public TimeOnly? Partida { get; set; }

    [Range(0, 1440), Display(Name = "Tempo embarque/desembarque (min)")]
    public int? TempoEmbarqueMinutos { get; set; }

    [Display(Name = "Comentários")]
    public string? Comentarios { get; set; }

    /// <summary>Folha "calendário": dias em que a circulação se realiza.</summary>
    public List<CirculacaoDia> Dias { get; set; } = [];

    /// <summary>Folha "percursos": sequência de paragens.</summary>
    public List<PercursoParagem> Percurso { get; set; } = [];
}

/// <summary>Dia em que a circulação se realiza (realiza-se = 1). A ausência de registo significa "não".</summary>
public class CirculacaoDia
{
    public int CirculacaoId { get; set; }
    public Circulacao? Circulacao { get; set; }
    public DateOnly Data { get; set; }
}

public class PercursoParagem
{
    public int CirculacaoId { get; set; }
    public Circulacao? Circulacao { get; set; }
    public int Sequencia { get; set; }
    public int ParagemId { get; set; }
    public Paragem? Paragem { get; set; }
}
