using System.ComponentModel.DataAnnotations;

namespace AppTIC.Web.Models.ViewModels;

public class GrupoViewModel
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Indique a designação."), StringLength(200), Display(Name = "Designação")]
    public string Designacao { get; set; } = string.Empty;

    [StringLength(30), Display(Name = "Abreviatura")]
    public string? Abreviatura { get; set; }

    [StringLength(20), Display(Name = "NIF (opcional)")]
    public string? Nif { get; set; }

    [Range(1, 999, ErrorMessage = "O prefixo tem de estar entre 1 e 999."), Display(Name = "Prefixo do ID TIC")]
    public int? PrefixoIdTic { get; set; }

    [Display(Name = "Comentários")]
    public string? Comentarios { get; set; }

    /// <summary>Cor nas legendas e gráficos; os operadores do grupo sem cor própria usam esta.</summary>
    [StringLength(9)]
    public string? Cor { get; set; }

    [Display(Name = "Operadores do grupo")]
    public List<int> Membros { get; set; } = [];
}

public record GrupoRow(int Id, string Designacao, string? Abreviatura, string? Nif, string? NumeracaoIdTic, int NumMembros, string Membros,
    int NumCirculacoes, int NumUtilizadores, string Cor = "#6c757d");

/// <summary>Operador que pode entrar num grupo; GrupoAtual = outro grupo a que já pertence.</summary>
public record GrupoMembroOpcao(int Id, string Nome, string? Nif, string? GrupoAtual, int NumCirculacoes);

/// <summary>Item de lista com grupos e operadores juntos (agrupados por Tipo na dropdown).</summary>
public record OperadorOuGrupoItem(string Value, string Text, string Tipo);

/// <summary>Seletor de cor (Syncfusion ColorPicker) de um operador ou grupo. CorEfetiva = a que se vê sem cor própria.</summary>
public record CorPickerModel(string? Cor, string CorEfetiva, string SemCorTexto);
