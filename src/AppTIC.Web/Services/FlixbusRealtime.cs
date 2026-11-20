using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace AppTIC.Web.Services;

/// <summary>
/// Configuração da integração em tempo real com a FlixBus (API "Where is my bus", Gateway PROD).
/// Não exige chave de API. Ver manual "How to Connect GTFS" da FlixBus.
/// </summary>
public class FlixbusOptions
{
    public const string Section = "Flixbus";

    /// <summary>Liga/desliga a atualização periódica em segundo plano.</summary>
    public bool Ativo { get; set; } = true;

    public string BaseUrl { get; set; } = "https://global.api.flixbus.com/gis/";

    /// <summary>UUID da estação FlixBus "Porto (TIC - Campanhã)" (igual ao stop_id do GTFS da FlixBus).</summary>
    public string StationUuid { get; set; } = "cd022a5c-a591-46fe-86a1-f7ca0ebbaf37";

    public int IntervaloSegundos { get; set; } = 10;

    /// <summary>Janela pedida à API: horas antes e depois do momento atual.</summary>
    public int HorasAntes { get; set; } = 1;
    public int HorasDepois { get; set; } = 6;

    public string FusoHorario { get; set; } = "Europe/Lisbon";

    /// <summary>Posições GPS: só viagens cuja hora no TIC está entre -MinutosAposTic e +MinutosAntesTic do momento atual.</summary>
    public int MinutosAntesTic { get; set; } = 180;
    public int MinutosAposTic { get; set; } = 90;

    /// <summary>Máximo de viagens seguidas por ciclo (um pedido à API por viagem).</summary>
    public int MaxVeiculos { get; set; } = 80;
}

// ---- Resposta da API /v1/station/{uuid}/timetable/{departures|arrivals}/... (JSON snake_case) ----

public record FlixTimetableResponse(
    [property: JsonPropertyName("rides")] List<FlixRide>? Rides,
    [property: JsonPropertyName("station")] FlixStation? Station);

public record FlixStation(
    [property: JsonPropertyName("uuid")] string? Uuid,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("latitude")] double? Latitude,
    [property: JsonPropertyName("longitude")] double? Longitude);

// ---- /v1/ride/{ride_uuid}/tracking e /v1/ride/{ride_uuid}/route ----

public record FlixTracking(
    [property: JsonPropertyName("ride_uuid")] string RideUuid,
    [property: JsonPropertyName("coordinates")] FlixCoordinates? Coordinates,
    [property: JsonPropertyName("speed_category")] string? SpeedCategory,
    [property: JsonPropertyName("course")] double? Course,
    [property: JsonPropertyName("updated")] long? Updated,
    [property: JsonPropertyName("stale")] bool Stale,
    [property: JsonPropertyName("vehicle_type")] string? VehicleType);

public record FlixCoordinates(
    [property: JsonPropertyName("latitude")] double Latitude,
    [property: JsonPropertyName("longitude")] double Longitude);

public record FlixRoute(
    [property: JsonPropertyName("segments")] List<FlixRouteSegment>? Segments);

public record FlixRouteSegment(
    [property: JsonPropertyName("segment_sequence")] int SegmentSequence,
    [property: JsonPropertyName("polyline")] string? Polyline);

public record FlixRide(
    [property: JsonPropertyName("ride_uuid")] string RideUuid,
    [property: JsonPropertyName("through_the_stations")] string? ThroughTheStations,
    [property: JsonPropertyName("planned")] FlixPlanned Planned,
    [property: JsonPropertyName("deviation")] FlixDeviation? Deviation,
    [property: JsonPropertyName("from_stop_uuid")] string? FromStopUuid,
    [property: JsonPropertyName("to_stop_uuid")] string? ToStopUuid,
    [property: JsonPropertyName("is_cancelled")] bool IsCancelled,
    [property: JsonPropertyName("line_code")] string? LineCode,
    [property: JsonPropertyName("direction")] string? Direction,
    [property: JsonPropertyName("stops")] List<FlixStop>? Stops,
    [property: JsonPropertyName("trip_number")] int? TripNumber,
    [property: JsonPropertyName("brand_name")] string? BrandName);

public record FlixPlanned(
    [property: JsonPropertyName("timestamp")] long Timestamp,
    [property: JsonPropertyName("tz")] string? Tz);

public record FlixDeviation(
    [property: JsonPropertyName("deviation_seconds")] int DeviationSeconds,
    [property: JsonPropertyName("deviation_class")] string? DeviationClass,
    [property: JsonPropertyName("deviation_type")] string? DeviationType,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("updated")] long? Updated);

public record FlixStop(
    [property: JsonPropertyName("uuid")] string Uuid,
    [property: JsonPropertyName("name")] string? Name);

public enum TipoPassagem { Chegada, Partida }

/// <summary>Uma chegada ou partida da FlixBus no TIC, já com horas locais e atraso calculado.</summary>
public record PassagemFlixbus(
    TipoPassagem Tipo,
    string RideUuid,
    string? Linha,
    int? Viagem,
    string? Marca,
    string? Percurso,
    string? Direcao,
    string? Extremo,        // origem (chegadas) ou destino (partidas) da viagem
    DateTimeOffset Planeada,
    DateTimeOffset Estimada,
    int? AtrasoMinutos,     // negativo = adiantado; null quando a FlixBus ainda não publicou desvio
    string? ClasseAtraso,
    string? Estado,
    bool Cancelada,
    DateTimeOffset? AtualizadoEm);

public record EstacaoFlixbus(string Nome, double Latitude, double Longitude);

public record HorarioFlixbus(IReadOnlyList<PassagemFlixbus> Passagens, EstacaoFlixbus? Estacao);

public enum SentidoTic
{
    ACaminho,   // ainda vai chegar ao TIC
    NoTic,      // já chegou (ou começa no TIC) e ainda não partiu
    Partiu      // já saiu do TIC
}

/// <summary>Viagem que passa no TIC e que vale a pena seguir no mapa.</summary>
public record ViagemSeguida(
    string RideUuid, string? Linha, int? Viagem, string? Percurso, SentidoTic Sentido,
    PassagemFlixbus? Chegada, PassagemFlixbus? Partida);

/// <summary>Posição GPS de um autocarro FlixBus numa viagem que passa no TIC.</summary>
public record PosicaoVeiculo(
    string RideUuid,
    string? Linha,
    int? Viagem,
    string? Percurso,
    SentidoTic Sentido,
    DateTimeOffset? ChegadaTic,     // estimada
    DateTimeOffset? PartidaTic,     // estimada
    int? AtrasoMinutos,             // da passagem relevante (chegada se ainda vem a caminho, senão partida)
    double Latitude,
    double Longitude,
    double? Rumo,                   // graus (0 = norte)
    string? Velocidade,             // SLOW / FAST / ... (categoria da FlixBus)
    DateTimeOffset? AtualizadoEm,
    bool Desatualizada);

public record FlixbusSnapshot(
    IReadOnlyList<PassagemFlixbus> Chegadas,
    IReadOnlyList<PassagemFlixbus> Partidas,
    DateTimeOffset? ObtidoEm,
    string? Erro,
    IReadOnlyList<PosicaoVeiculo>? Posicoes = null,
    EstacaoFlixbus? Estacao = null)
{
    public static readonly FlixbusSnapshot Vazio = new([], [], null, null);
}

public static class FlixbusPosicoes
{
    /// <summary>
    /// Junta chegadas e partidas por viagem (uma viagem de passagem aparece nas duas), descarta as canceladas
    /// e as que estão longe do momento atual, e devolve as mais próximas primeiro.
    /// </summary>
    public static IReadOnlyList<ViagemSeguida> Selecionar(
        IEnumerable<PassagemFlixbus> chegadas, IEnumerable<PassagemFlixbus> partidas,
        DateTimeOffset agora, TimeSpan antes, TimeSpan apos, int max)
    {
        var porViagem = new Dictionary<string, (PassagemFlixbus? C, PassagemFlixbus? P)>();
        foreach (var c in chegadas) porViagem[c.RideUuid] = (c, porViagem.GetValueOrDefault(c.RideUuid).P);
        foreach (var p in partidas) porViagem[p.RideUuid] = (porViagem.GetValueOrDefault(p.RideUuid).C, p);

        var res = new List<(ViagemSeguida V, TimeSpan Dist)>();
        foreach (var (id, (c, p)) in porViagem)
        {
            if (c?.Cancelada == true || p?.Cancelada == true) continue;
            var horas = new[] { c?.Estimada, p?.Estimada }.Where(h => h is not null).Select(h => h!.Value).ToList();
            if (!horas.Any(h => h >= agora - apos && h <= agora + antes)) continue;

            var sentido = c is not null && c.Estimada > agora ? SentidoTic.ACaminho
                : p is not null && p.Estimada > agora ? SentidoTic.NoTic
                : SentidoTic.Partiu;
            var ref_ = c ?? p!;
            res.Add((new ViagemSeguida(id, ref_.Linha, ref_.Viagem, ref_.Percurso, sentido, c, p),
                horas.Min(h => (h - agora).Duration())));
        }
        return res.OrderBy(x => x.Dist).Take(max).Select(x => x.V).ToList();
    }

    public static PosicaoVeiculo? Mapear(ViagemSeguida v, FlixTracking? t, TimeZoneInfo fuso)
    {
        if (t?.Coordinates is not { } xy) return null;
        var relevante = v.Sentido == SentidoTic.ACaminho ? v.Chegada : v.Partida ?? v.Chegada;
        return new PosicaoVeiculo(
            v.RideUuid, v.Linha, v.Viagem, v.Percurso, v.Sentido,
            v.Chegada?.Estimada, v.Partida?.Estimada, relevante?.AtrasoMinutos,
            xy.Latitude, xy.Longitude, t.Course, t.SpeedCategory,
            t.Updated is { } u ? TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds(u), fuso) : null,
            t.Stale);
    }
}

public static class FlixbusMapper
{
    public static TimeZoneInfo Fuso(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time"); }
    }

    public static PassagemFlixbus Mapear(FlixRide r, TipoPassagem tipo, string stationUuid, TimeZoneInfo fuso)
    {
        var planeada = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds(r.Planned.Timestamp), fuso);
        var desvio = r.Deviation?.DeviationSeconds ?? 0;
        var estimada = planeada.AddSeconds(desvio);

        // Origem da viagem nas chegadas, destino nas partidas (ignorando o próprio TIC).
        var outras = (r.Stops ?? []).Where(s => !string.Equals(s.Uuid, stationUuid, StringComparison.OrdinalIgnoreCase)).ToList();
        var extremo = tipo == TipoPassagem.Chegada ? outras.FirstOrDefault()?.Name : outras.LastOrDefault()?.Name;

        return new PassagemFlixbus(
            tipo, r.RideUuid, r.LineCode, r.TripNumber, r.BrandName, r.ThroughTheStations, r.Direction, extremo,
            planeada, estimada,
            r.Deviation is null ? null : (int)Math.Round(desvio / 60.0, MidpointRounding.AwayFromZero),
            r.Deviation?.DeviationClass, r.Deviation?.Status, r.IsCancelled,
            r.Deviation?.Updated is { } u ? TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds(u), fuso) : null);
    }
}

/// <summary>Cliente HTTP da API de horários por estação da FlixBus.</summary>
public class FlixbusClient(HttpClient http, IOptions<FlixbusOptions> options)
{
    private readonly FlixbusOptions _opt = options.Value;

    public static string Caminho(string stationUuid, TipoPassagem tipo, DateTimeOffset de, DateTimeOffset ate) =>
        $"v1/station/{Uri.EscapeDataString(stationUuid)}/timetable/{(tipo == TipoPassagem.Chegada ? "arrivals" : "departures")}" +
        $"/from-timestamp/{de.ToUnixTimeSeconds()}/to-timestamp/{ate.ToUnixTimeSeconds()}";

    public async Task<HorarioFlixbus> ObterAsync(TipoPassagem tipo, DateTimeOffset de, DateTimeOffset ate, CancellationToken ct = default)
    {
        var resp = await http.GetFromJsonAsync<FlixTimetableResponse>(Caminho(_opt.StationUuid, tipo, de, ate), ct);
        var fuso = FlixbusMapper.Fuso(_opt.FusoHorario);
        var passagens = (resp?.Rides ?? [])
            .Select(r => FlixbusMapper.Mapear(r, tipo, _opt.StationUuid, fuso))
            .OrderBy(p => p.Planeada).ThenBy(p => p.Linha)
            .ToList();
        var estacao = resp?.Station is { Latitude: { } lat, Longitude: { } lon } s
            ? new EstacaoFlixbus(s.Name ?? "TIC", lat, lon) : null;
        return new HorarioFlixbus(passagens, estacao);
    }

    /// <summary>Última posição GPS da viagem; null quando a FlixBus ainda não tem localização (HTTP 404).</summary>
    public async Task<FlixTracking?> ObterPosicaoAsync(string rideUuid, CancellationToken ct = default)
    {
        using var resp = await http.GetAsync($"v1/ride/{Uri.EscapeDataString(rideUuid)}/tracking", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<FlixTracking>(ct);
    }

    /// <summary>Traçado da viagem como polylines codificadas (formato Google, precisão 5), por ordem.</summary>
    public async Task<IReadOnlyList<string>> ObterRotaAsync(string rideUuid, CancellationToken ct = default)
    {
        using var resp = await http.GetAsync($"v1/ride/{Uri.EscapeDataString(rideUuid)}/route", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return [];
        resp.EnsureSuccessStatusCode();
        var rota = await resp.Content.ReadFromJsonAsync<FlixRoute>(ct);
        return (rota?.Segments ?? []).OrderBy(s => s.SegmentSequence)
            .Where(s => !string.IsNullOrEmpty(s.Polyline)).Select(s => s.Polyline!).ToList();
    }
}

/// <summary>Último estado obtido da FlixBus (em memória, partilhado por toda a aplicação).</summary>
public class FlixbusRealtimeStore
{
    private volatile FlixbusSnapshot _atual = FlixbusSnapshot.Vazio;
    public FlixbusSnapshot Atual => _atual;

    public void Definir(FlixbusSnapshot s) => _atual = s;

    /// <summary>Mantém os dados anteriores e regista só o erro, para a página não ficar vazia numa falha pontual.</summary>
    public void RegistarErro(string erro) => _atual = _atual with { Erro = erro };
}

/// <summary>Atualiza periodicamente as chegadas e partidas da FlixBus no TIC.</summary>
public class FlixbusRealtimeWorker(
    IServiceScopeFactory scopes, FlixbusRealtimeStore store, IOptions<FlixbusOptions> options, RastoStore rastos,
    ILogger<FlixbusRealtimeWorker> logger, TimeProvider tempo) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var opt = options.Value;
        if (!opt.Ativo)
        {
            logger.LogInformation("Integração FlixBus em tempo real desativada (Flixbus:Ativo = false).");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, opt.IntervaloSegundos)), tempo);
        do
        {
            await AtualizarAsync(opt, ct);
        } while (await timer.WaitForNextTickAsync(ct));
    }

    public async Task AtualizarAsync(FlixbusOptions opt, CancellationToken ct)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var client = scope.ServiceProvider.GetRequiredService<FlixbusClient>();
            var agora = tempo.GetUtcNow();
            var de = agora.AddHours(-opt.HorasAntes);
            var ate = agora.AddHours(opt.HorasDepois);
            var chegadasT = client.ObterAsync(TipoPassagem.Chegada, de, ate, ct);
            var partidasT = client.ObterAsync(TipoPassagem.Partida, de, ate, ct);
            var (chegadas, partidas) = (await chegadasT, await partidasT);

            var posicoes = await ObterPosicoesAsync(client, opt, chegadas.Passagens, partidas.Passagens, agora, ct);
            store.Definir(new FlixbusSnapshot(chegadas.Passagens, partidas.Passagens, agora, null,
                posicoes, chegadas.Estacao ?? partidas.Estacao));
            rastos.Registar("flixbus", posicoes.Select(p => (p.RideUuid, p.Latitude, p.Longitude, p.AtualizadoEm)));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha ao obter dados da FlixBus em tempo real.");
            store.RegistarErro($"Falha ao contactar a FlixBus ({tempo.GetLocalNow():HH:mm}): {ex.Message}");
        }
    }

    private async Task<IReadOnlyList<PosicaoVeiculo>> ObterPosicoesAsync(
        FlixbusClient client, FlixbusOptions opt,
        IReadOnlyList<PassagemFlixbus> chegadas, IReadOnlyList<PassagemFlixbus> partidas,
        DateTimeOffset agora, CancellationToken ct)
    {
        var viagens = FlixbusPosicoes.Selecionar(chegadas, partidas, agora,
            TimeSpan.FromMinutes(opt.MinutosAntesTic), TimeSpan.FromMinutes(opt.MinutosAposTic), opt.MaxVeiculos);
        var fuso = FlixbusMapper.Fuso(opt.FusoHorario);
        var posicoes = new System.Collections.Concurrent.ConcurrentBag<PosicaoVeiculo>();

        // Um pedido por viagem, no máximo 6 em simultâneo. Uma falha numa viagem não impede as restantes.
        await Parallel.ForEachAsync(viagens, new ParallelOptions { MaxDegreeOfParallelism = 6, CancellationToken = ct }, async (v, token) =>
        {
            try
            {
                if (FlixbusPosicoes.Mapear(v, await client.ObterPosicaoAsync(v.RideUuid, token), fuso) is { } p)
                    posicoes.Add(p);
            }
            catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or TaskCanceledException && !token.IsCancellationRequested)
            {
                logger.LogDebug(ex, "Sem posição para a viagem FlixBus {Ride}.", v.RideUuid);
            }
        });
        return posicoes.OrderBy(p => p.Sentido).ThenBy(p => p.ChegadaTic ?? p.PartidaTic).ToList();
    }
}
