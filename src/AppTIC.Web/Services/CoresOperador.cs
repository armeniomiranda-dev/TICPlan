using System.Text.RegularExpressions;

namespace AppTIC.Web.Services;

/// <summary>
/// Cor de cada operador nas legendas e gráficos: a sua; sem cor, a do grupo; sem nenhuma, uma cor fixa da paleta (pelo Id).
/// </summary>
public static partial class CoresOperador
{
    public static readonly string[] Paleta =
        ["#0d6efd", "#198754", "#fd7e14", "#6f42c1", "#d63384", "#20c997", "#0dcaf0", "#795548", "#6c757d", "#b8860b"];

    public static string PorOmissao(int id) => Paleta[Math.Abs(id) % Paleta.Length];

    public static string De(int id, string? cor, string? corGrupo = null) => Normalizar(cor) ?? Normalizar(corGrupo) ?? PorOmissao(id);

    /// <summary>"#RRGGBB" ou "#RRGGBBAA" (como devolve o ColorPicker) → "#rrggbb"; outra coisa → null.</summary>
    public static string? Normalizar(string? cor) =>
        cor is not null && Hex().Match(cor.Trim()) is { Success: true } m ? "#" + m.Groups[1].Value.ToLowerInvariant() : null;

    [GeneratedRegex("^#([0-9a-fA-F]{6})(?:[0-9a-fA-F]{2})?$")]
    private static partial Regex Hex();
}
