using System.ComponentModel.DataAnnotations;

namespace AppTIC.Web.Models.Domain;

public enum EstadoPedido
{
    [Display(Name = "Pendente")] Pendente = 0,
    [Display(Name = "Aceite")] Aceite = 1,
    [Display(Name = "Rejeitado")] Rejeitado = 2
}

/// <summary>
/// Pedido de alteração de uma circulação feito por um utilizador do operador (que não a pode editar).
/// O Admin responde (aceite/rejeitado) e faz a alteração na circulação.
/// </summary>
public class PedidoAlteracao
{
    public int Id { get; set; }

    public int CirculacaoId { get; set; }
    public Circulacao? Circulacao { get; set; }

    [Required(ErrorMessage = "Descreva a alteração pretendida."), Display(Name = "Alteração pretendida")]
    public string Descricao { get; set; } = string.Empty;

    [Display(Name = "A partir de")]
    public DateOnly? DataInicio { get; set; }

    [Display(Name = "Até")]
    public DateOnly? DataFim { get; set; }

    [Display(Name = "Nova chegada ao TIC")]
    public TimeOnly? NovaChegada { get; set; }

    [Display(Name = "Nova partida do TIC")]
    public TimeOnly? NovaPartida { get; set; }

    public EstadoPedido Estado { get; set; }

    public string PedidoPorId { get; set; } = string.Empty;
    public ApplicationUser? PedidoPor { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;

    [Display(Name = "Resposta")]
    public string? Resposta { get; set; }

    [StringLength(256)]
    public string? RespondidoPor { get; set; }
    public DateTime? RespondidoEm { get; set; }
}
