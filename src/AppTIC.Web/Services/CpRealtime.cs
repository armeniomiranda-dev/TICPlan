using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace AppTIC.Web.Services;

/// <summary>
/// CP – Comboios de Portugal em tempo real na estação de Porto-Campanhã, via API pública comboios.live (a mesma usada pelo
/// projeto hass-transportes-pt): quadro de chegadas/partidas com atrasos e posições GPS dos comboios.
/// </summary>
public class CpOptions
{
    public const string Section = "Cp";

    public bool Ativo { get; set; } = true;

    public string BaseUrl { get; set; } = "https://comboios.live/api/";

    /// <summary>Estação no formato GTFS da CP (94_2006 = Porto Campanhã). Na API comboios.live usa-se "94-2006".</summary>
    public string Estacao { get; set; } = "94_2006";

    public double Latitude { get; set; } = 41.14893;
    public double Longitude { get; set; } = -8.58534;

    public int IntervaloSegundos { get; set; } = 10;

    /// <summary>Durante quantos minutos um comboio que já partiu continua na lista/mapa.</summary>
    public int MinutosAposPartida { get; set; } = 15;

    public string FusoHorario { get; set; } = "Europe/Lisbon";

    public string EstacaoApi => Estacao.Replace('_', '-');
}

// ---- comboios.live ----

public record CpQuadro([property: JsonPropertyName("arrivals")] List<CpPassagem>? Arrivals);

public record CpPassagem(
    [property: JsonPropertyName("trainNumber")] int TrainNumber,
    [property: JsonPropertyName("trainService")] CpCodigo? TrainService,
    [property: JsonPropertyName("trainOrigin")] CpCodigo? TrainOrigin,
    [property: JsonPropertyName("trainDestination")] CpCodigo? TrainDestination,
    [property: JsonPropertyName("arrivalTime")] string? ArrivalTime,
    [property: JsonPropertyName("departureTime")] string? DepartureTime,
    [property: JsonPropertyName("platform")] string? Platform,
    [property: JsonPropertyName("delay")] int? Delay,
    [property: JsonPropertyName("supression")] object? Supression,
    [property: JsonPropertyName("ETA")] string? Eta,
    [property: JsonPropertyName("ETD")] string? Etd);

public record CpCodigo(
    [property: JsonPropertyName("code")] string? Code,
    [property: JsonPropertyName("designation")] string? Designation);

public record CpVeiculos([property: JsonPropertyName("vehicles")] List<CpVeiculo>? Vehicles);

public record CpVeiculo(
    [property: JsonPropertyName("trainNumber")] int TrainNumber,
    [property: JsonPropertyName("runDate")] string? RunDate,
    [property: JsonPropertyName("delay")] int? Delay,          // segundos
    [property: JsonPropertyName("lastStation")] string? LastStation,
    [property: JsonPropertyName("latitude")] string? Latitude,
    [property: JsonPropertyName("longitude")] string? Longitude,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("bearing")] double? Bearing,
    [property: JsonPropertyName("timestamp")] long? Timestamp);  // milissegundos

/// <summary>Comboio que passa (ou começa/termina) em Campanhã.</summary>
public record ComboioCp(
    int Numero,
    string? Servico,            // AP, IC, IR, R, U, IN...
    string? ServicoNome,
    string? Origem,
    string? Destino,
    DateTimeOffset? ChegadaPrevista,
    DateTimeOffset? PartidaPrevista,
    DateTimeOffset? ChegadaEstimada,
    DateTimeOffset? PartidaEstimada,
    int? AtrasoMinutos,
    string? Plataforma,
    bool Suprimido,
    SentidoTic Sentido,
    double? Latitude,
    double? Longitude,
    double? Rumo,
    string? EstadoGps,          // IN_TRANSIT, AT_STATION, ...
    DateTimeOffset? AtualizadoEm)
{
    /// <summary>Hora relevante para ordenar: chegada (ou partida, se começa aqui), estimada se houver.</summary>
    public DateTimeOffset? Hora => ChegadaEstimada ?? ChegadaPrevista ?? PartidaEstimada ?? PartidaPrevista;
}

public record CpSnapshot(
    IReadOnlyList<ComboioCp> Comboios,
    string Estacao,
    double Latitude,
    double Longitude,
    DateTimeOffset? ObtidoEm,
    string? Erro)
{
    public static readonly CpSnapshot Vazio = new([], "94_2006", 41.14893, -8.58534, null, null);
}

public static class CpTic
{
    private static readonly Dictionary<string, string> Servicos = new()
    {
        ["AP"] = "Alfa Pendular", ["IC"] = "Intercidades", ["IR"] = "InterRegional", ["R"] = "Regional",
        ["U"] = "Urbano", ["IN"] = "Internacional"
    };

    /// <summary>
    /// "HH:mm" do quadro → data/hora local. O quadro cobre poucas horas à volta do momento atual: uma hora muito anterior
    /// é do dia seguinte (passagem da meia-noite) e uma muito posterior é do dia anterior.
    /// </summary>
    public static DateTimeOffset? Hora(string? hhmm, DateTimeOffset agoraLocal)
    {
        if (string.IsNullOrWhiteSpace(hhmm) || !TimeOnly.TryParseExact(hhmm.Trim(), ["HH:mm", "H:mm", "HH:mm:ss"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var t))
            return null;
        var h = new DateTimeOffset(DateOnly.FromDateTime(agoraLocal.DateTime).ToDateTime(t), agoraLocal.Offset);
        if (h < agoraLocal.AddHours(-12)) h = h.AddDays(1);
        else if (h > agoraLocal.AddHours(12)) h = h.AddDays(-1);
        return h;
    }

    /// <summary>"supression" vem a null quando o comboio circula; qualquer outro valor (texto, objeto, true) indica supressão.</summary>
    public static bool Suprimido(object? supressao) => supressao switch
    {
        null => false,
        System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.Null or System.Text.Json.JsonValueKind.Undefined or System.Text.Json.JsonValueKind.False } => false,
        System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.String } e => !string.IsNullOrWhiteSpace(e.GetString()),
        _ => true
    };

    public static IReadOnlyList<ComboioCp> Combinar(
        IEnumerable<CpPassagem> quadro, IEnumerable<CpVeiculo> veiculos, string estacaoApi,
        DateTimeOffset agora, TimeZoneInfo fuso, int minutosAposPartida)
    {
        var local = TimeZoneInfo.ConvertTime(agora, fuso);
        var hoje = DateOnly.FromDateTime(local.DateTime).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        // Mesmo nº de comboio pode existir em dias diferentes: preferir o do dia.
        var gps = veiculos.GroupBy(v => v.TrainNumber)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(v => v.RunDate == hoje).First());

        var res = new List<ComboioCp>();
        foreach (var p in quadro)
        {
            var chegada = Hora(p.ArrivalTime, local);
            var partida = Hora(p.DepartureTime, local);
            var eta = Hora(p.Eta, local) ?? (chegada is { } c && p.Delay is { } d1 ? c.AddMinutes(d1) : null);
            var etd = Hora(p.Etd, local) ?? (partida is { } s && p.Delay is { } d2 ? s.AddMinutes(d2) : null);
            var suprimido = Suprimido(p.Supression);

            gps.TryGetValue(p.TrainNumber, out var v);
            var naEstacao = v is { Status: "AT_STATION" or "AT_ORIGIN" } && v.LastStation == estacaoApi;
            var saida = etd ?? partida;
            var entrada = eta ?? chegada;
            var sentido = naEstacao ? SentidoTic.NoTic
                : entrada is { } e && e > local ? SentidoTic.ACaminho
                : saida is { } sd && sd > local ? SentidoTic.NoTic      // já chegou (ou começa aqui) e ainda não partiu
                : SentidoTic.Partiu;
            // Termina aqui e já chegou: some depois de alguns minutos, como os que partiram.
            var fim = saida ?? entrada;
            if (sentido == SentidoTic.Partiu && fim is { } f && f < local.AddMinutes(-minutosAposPartida)) continue;

            double? lat = null, lon = null;
            if (v is not null
                && double.TryParse(v.Latitude, CultureInfo.InvariantCulture, out var la)
                && double.TryParse(v.Longitude, CultureInfo.InvariantCulture, out var lo))
                (lat, lon) = (la, lo);

            var codigo = p.TrainService?.Code;
            res.Add(new ComboioCp(
                p.TrainNumber, codigo,
                p.TrainService?.Designation ?? (codigo is not null ? Servicos.GetValueOrDefault(codigo) : null),
                p.TrainOrigin?.Designation, p.TrainDestination?.Designation,
                chegada, partida, eta, etd, p.Delay, p.Platform, suprimido || v?.Status == "CANCELLED", sentido,
                lat, lon, v?.Bearing, v?.Status,
                v?.Timestamp is { } ms ? TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(ms), fuso) : null));
        }
        return res.OrderBy(c => c.Hora).ThenBy(c => c.Numero).ToList();
    }
}

public class CpRealtimeStore
{
    private volatile CpSnapshot _atual = CpSnapshot.Vazio;
    public CpSnapshot Atual => _atual;
    public void Definir(CpSnapshot s) => _atual = s;
    public void RegistarErro(string erro) => _atual = _atual with { Erro = erro };
}

public class CpClient(HttpClient http)
{
    public async Task<IReadOnlyList<CpPassagem>> ObterQuadroAsync(string estacaoApi, CancellationToken ct) =>
        (await http.GetFromJsonAsync<CpQuadro>($"stations/{Uri.EscapeDataString(estacaoApi)}/arrivals", ct))?.Arrivals ?? [];

    public async Task<IReadOnlyList<CpVeiculo>> ObterVeiculosAsync(CancellationToken ct) =>
        (await http.GetFromJsonAsync<CpVeiculos>("vehicles", ct))?.Vehicles ?? [];
}

public class CpRealtimeWorker(
    IServiceScopeFactory scopes, CpRealtimeStore store, IOptions<CpOptions> options, RastoStore rastos,
    ILogger<CpRealtimeWorker> logger, TimeProvider tempo) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var opt = options.Value;
        if (!opt.Ativo)
        {
            logger.LogInformation("Integração CP em tempo real desativada (Cp:Ativo = false).");
            return;
        }
        store.Definir(CpSnapshot.Vazio with { Estacao = opt.Estacao, Latitude = opt.Latitude, Longitude = opt.Longitude });

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, opt.IntervaloSegundos)), tempo);
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var client = scope.ServiceProvider.GetRequiredService<CpClient>();
                var quadroT = client.ObterQuadroAsync(opt.EstacaoApi, ct);
                var veiculosT = client.ObterVeiculosAsync(ct);
                var agora = tempo.GetUtcNow();
                IReadOnlyList<CpVeiculo> veiculos;
                try { veiculos = await veiculosT; }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException && !ct.IsCancellationRequested)
                {
                    logger.LogDebug(ex, "Posições dos comboios indisponíveis; a mostrar só o quadro.");
                    veiculos = [];
                }
                var comboios = CpTic.Combinar(await quadroT, veiculos, opt.EstacaoApi, agora,
                    FlixbusMapper.Fuso(opt.FusoHorario), opt.MinutosAposPartida);
                store.Definir(new CpSnapshot(comboios, opt.Estacao, opt.Latitude, opt.Longitude, agora, null));
                rastos.Registar("cp", comboios.Where(c => c.Latitude is not null && c.Longitude is not null)
                    .Select(c => (c.Numero.ToString(), c.Latitude!.Value, c.Longitude!.Value, c.AtualizadoEm)));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Falha ao obter dados da CP em tempo real.");
                store.RegistarErro($"Falha ao contactar a CP ({tempo.GetLocalNow():HH:mm}): {ex.Message}");
            }
        } while (await timer.WaitForNextTickAsync(ct));
    }
}
