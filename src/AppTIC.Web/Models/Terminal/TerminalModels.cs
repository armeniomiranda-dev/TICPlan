using System.Text.Json.Serialization;

namespace AppTIC.Web.Models.Terminal;

public enum ZonaCais
{
    Lisboa,
    Fora,
    Norte,
    Outros
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
