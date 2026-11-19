using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace AppTIC.Web.Models.Terminal;

/// <summary>Classificação de atribuição de cais: Lisboa 1–4, Internacional 5–8, Norte de Portugal 9–10, Comum 11–17 (restantes e reserva das zonas lotadas).</summary>
public enum ZonaCais
{
    [Display(Name = "Lisboa")] Lisboa = 0,
    [Display(Name = "Internacional")] Internacional = 1,
    [Display(Name = "Norte de Portugal")] Norte = 2,
    [Display(Name = "Comum")] Comum = 3
}

public enum EstadoAutocarro
{
    Agendado,
    Atracado,
    Concluido
}

public record RegistoCais(
    string AutocarroId,
    ZonaCais Zona,
    int ChegadaMinutos,
    int PartidaMinutos,
    int? CaisAlocado,
    EstadoAutocarro Estado);

public record AutocarroDto(
    string AutocarroId,
    ZonaCais Zona,
    int ChegadaMinutos,
    int PartidaMinutos,
    int? CaisFixo);

public record AlocacaoResposta(string AutocarroId, int Cais);

public record NovaChegadaRequest(
    [property: JsonRequired] string Id,
    [property: JsonRequired] ZonaCais Zona,
    [property: JsonRequired] int ChegadaMinutos,
    [property: JsonRequired] int PartidaMinutos);

/// <summary>Resultado do planeamento do dia: cais atribuídos e autocarros que não couberam.</summary>
public record PlaneamentoDia(IReadOnlyList<AlocacaoResposta> Alocacoes, IReadOnlyList<string> SemCais, bool Otimo);

/// <summary>Preferência de posição dentro da categoria: junto à saída (paragens rápidas) ou no fundo do canal (esperas longas).</summary>
public enum PreferenciaCais
{
    Nenhuma,
    Saida,
    Fundo
}

/// <summary>Tipologia de cais configurável na simulação: os cais de uma categoria de reserva recebem o excesso das outras.</summary>
public record CategoriaCaisDto(
    [property: JsonRequired] string Id,
    [property: JsonRequired] IReadOnlyList<int> Cais,
    bool Reserva = false,
    PreferenciaCais Preferencia = PreferenciaCais.Nenhuma);

public record AutocarroPlanoDto(
    [property: JsonRequired] string AutocarroId,
    [property: JsonRequired] string Categoria,
    [property: JsonRequired] int ChegadaMinutos,
    [property: JsonRequired] int PartidaMinutos,
    int? CaisFixo = null);

/// <summary>
/// Pedido de planeamento com categorias configuráveis (por região ou por tempo de permanência).
/// UltimoRecurso: permite, em último caso, um cais de outra categoria.
/// OrdemSaida: os cais, do mais próximo ao mais distante da saída.
/// </summary>
public record PlaneamentoPedido(
    [property: JsonRequired] IReadOnlyList<CategoriaCaisDto> Categorias,
    [property: JsonRequired] IReadOnlyList<AutocarroPlanoDto> Autocarros,
    bool UltimoRecurso = false,
    IReadOnlyList<int>? OrdemSaida = null);
