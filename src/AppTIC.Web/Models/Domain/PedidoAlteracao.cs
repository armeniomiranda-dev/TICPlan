using System.ComponentModel.DataAnnotations;

namespace AppTIC.Web.Models.Domain;

public enum EstadoPedido
{
    [Display(Name = "Pendente")] Pendente = 0,
    [Display(Name = "Aceite")] Aceite = 1,
    [Display(Name = "Rejeitado")] Rejeitado = 2
}

public enum TipoPedido
{
    [Display(Name = "Alteração")] Alteracao = 0,
    [Display(Name = "Nova circulação")] NovaCirculacao = 1,
    [Display(Name = "Remoção")] Remocao = 2
}

/// <summary>
/// Pedido feito por um utilizador do operador (que não edita as circulações): alteração de uma circulação,
/// nova circulação, ou informação de que uma circulação deixa de se realizar a partir de uma data.
/// O Admin responde (aceite/rejeitado); ao aceitar, o pedido pode ser aplicado automaticamente.
/// </summary>
public class PedidoAlteracao
{
    public int Id { get; set; }

    public TipoPedido Tipo { get; set; }

    /// <summary>Operador a que o pedido diz respeito (o da circulação; na nova circulação, o escolhido).</summary>
    [Display(Name = "Operador")]
    public int OperadorId { get; set; }
    public Operador? Operador { get; set; }

    /// <summary>Circulação alterada/removida; na nova circulação, a criada quando o pedido é aceite.</summary>
    public int? CirculacaoId { get; set; }
    public Circulacao? Circulacao { get; set; }

    [Required(ErrorMessage = "Descreva o pedido."), Display(Name = "Descrição")]
    public string Descricao { get; set; } = string.Empty;

    /// <summary>Alteração: início do período; nova circulação: primeiro dia; remoção: deixa de se realizar a partir deste dia.</summary>
    [Display(Name = "A partir de")]
    public DateOnly? DataInicio { get; set; }

    [Display(Name = "Até")]
    public DateOnly? DataFim { get; set; }

    [Display(Name = "Nova chegada ao TIC")]
    public TimeOnly? NovaChegada { get; set; }

    [Display(Name = "Nova partida do TIC")]
    public TimeOnly? NovaPartida { get; set; }

    // ---- Nova circulação ----

    [StringLength(50), Display(Name = "ID de viagem / código de circulação")]
    public string? ViagemId { get; set; }

    [StringLength(50), Display(Name = "Linha comercial/bus")]
    public string? LinhaId { get; set; }

    [StringLength(200), Display(Name = "Designação da linha")]
    public string? DesignacaoLinha { get; set; }

    [Display(Name = "Paragem de origem")]
    public int? OrigemId { get; set; }
    public Paragem? Origem { get; set; }

    [Display(Name = "Paragem de destino")]
    public int? DestinoId { get; set; }
    public Paragem? Destino { get; set; }

    /// <summary>Dias da semana em que se realiza (0 = domingo … 6 = sábado), separados por vírgula; vazio = todos.</summary>
    [StringLength(20), Display(Name = "Dias da semana")]
    public string? DiasSemana { get; set; }

    public EstadoPedido Estado { get; set; }

    public string PedidoPorId { get; set; } = string.Empty;
    public ApplicationUser? PedidoPor { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;

    [Display(Name = "Resposta")]
    public string? Resposta { get; set; }

    [StringLength(256)]
    public string? RespondidoPor { get; set; }
    public DateTime? RespondidoEm { get; set; }

    /// <summary>Quando aceite: se foi aplicado automaticamente à circulação.</summary>
    public bool Aplicado { get; set; }

    /// <summary>Nova circulação: calendário completo pedido (dias em que se realiza).</summary>
    public List<PedidoDia> Dias { get; set; } = [];

    public IReadOnlyList<int> DiasSemanaLista() =>
        (DiasSemana ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(d => int.TryParse(d, out var n) ? n : -1).Where(n => n is >= 0 and <= 6).Distinct().Order().ToList();
}

/// <summary>Dia do calendário pedido para uma nova circulação.</summary>
public class PedidoDia
{
    public int PedidoId { get; set; }
    public PedidoAlteracao? Pedido { get; set; }
    public DateOnly Data { get; set; }
}
