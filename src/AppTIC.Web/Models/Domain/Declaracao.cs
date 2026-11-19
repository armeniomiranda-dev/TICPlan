using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace AppTIC.Web.Models.Domain;

/// <summary>
/// Declaração de capacidade (DCP): agrega circulações. Identifica-se por Ano.Numero.Versao (ex.: 2026.62.01).
/// O número é atribuído (sequencial no ano) a cada nova circulação criada; a versão sobe sempre que muda
/// o horário de pelo menos uma das circulações da declaração.
/// </summary>
public partial class Declaracao
{
    public int Id { get; set; }

    [Display(Name = "Ano")]
    public int Ano { get; set; }

    [Display(Name = "Número")]
    public int Numero { get; set; }

    [Display(Name = "Versão")]
    public int Versao { get; set; }

    /// <summary>Operador declarante (o grupo, quando o operador pertence a um grupo).</summary>
    [Display(Name = "Operador")]
    public int? OperadorId { get; set; }
    public Operador? Operador { get; set; }

    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
    public DateTime AtualizadoEm { get; set; } = DateTime.UtcNow;

    /// <summary>Declaração do ano anterior de que esta é a renovação.</summary>
    public int? AnteriorId { get; set; }
    public Declaracao? Anterior { get; set; }
    /// <summary>Renovação no ano seguinte (quando existe, esta declaração está encerrada).</summary>
    public Declaracao? Renovacao { get; set; }

    /// <summary>Circulações atuais (as de uma declaração renovada passaram para a renovação).</summary>
    public List<Circulacao> Circulacoes { get; set; } = [];
    /// <summary>Circulações à data da renovação (histórico do ano encerrado).</summary>
    public List<DeclaracaoCirculacao> Historico { get; set; } = [];
    public List<DeclaracaoVersao> Versoes { get; set; } = [];

    /// <summary>Código Ano.Numero.Versao (versão com 2 dígitos).</summary>
    public string Codigo => Formatar(Ano, Numero, Versao);

    public static string Formatar(int ano, int numero, int versao) => $"{ano}.{numero}.{versao:00}";

    /// <summary>
    /// Lê um id_DCP: "2026.62.01" (ano.número.versão), "62.01" (número.versão) ou "62" (número; versão 00).
    /// Sem ano, usa <paramref name="anoPorOmissao"/>. Devolve null se não for reconhecido.
    /// </summary>
    public static (int Ano, int Numero, int Versao)? Ler(string? valor, int anoPorOmissao)
    {
        if (string.IsNullOrWhiteSpace(valor)) return null;
        var m = Formato().Match(valor.Trim());
        if (!m.Success) return null;
        var partes = m.Groups["p"].Captures.Select(c => int.Parse(c.Value)).ToList();
        return partes.Count switch
        {
            3 when partes[0] >= 2000 => (partes[0], partes[1], partes[2]),
            2 => (anoPorOmissao, partes[0], partes[1]),
            1 => (anoPorOmissao, partes[0], 0),
            _ => null
        };
    }

    [GeneratedRegex(@"^(?:(?<p>\d{1,6})\.){0,2}(?<p>\d{1,6})$")]
    private static partial Regex Formato();
}

/// <summary>Circulação que fazia parte de uma declaração quando esta foi renovada para o ano seguinte.</summary>
public class DeclaracaoCirculacao
{
    public int DeclaracaoId { get; set; }
    public Declaracao? Declaracao { get; set; }
    public int CirculacaoId { get; set; }
    public Circulacao? Circulacao { get; set; }
}

/// <summary>Histórico das versões de uma declaração (o que levou à nova versão).</summary>
public class DeclaracaoVersao
{
    public int Id { get; set; }
    public int DeclaracaoId { get; set; }
    public Declaracao? Declaracao { get; set; }
    public int Versao { get; set; }
    public DateTime Data { get; set; } = DateTime.UtcNow;

    [StringLength(500)]
    public string Motivo { get; set; } = string.Empty;

    [StringLength(256)]
    public string? Utilizador { get; set; }

    /// <summary>Circulação cuja alteração originou a versão (quando se aplica).</summary>
    public int? CirculacaoId { get; set; }
}
