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
