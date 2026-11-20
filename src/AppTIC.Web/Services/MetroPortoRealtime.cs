using System.IO.Compression;
using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace AppTIC.Web.Services;

/// <summary>
/// Metro do Porto na estação de Campanhã. A Metro não publica tempo real: as partidas vêm do GTFS oficial (horário) e o
/// estado das linhas (Ok / Condicionada / Interrompida) da página inicial do site, como no projeto hass-transportes-pt.
/// </summary>
public class MetroOptions
{
    public const string Section = "Metro";

    public bool Ativo { get; set; } = true;

    /// <summary>Ligação estável do site da Metro para o GTFS em vigor.</summary>
    public string GtfsUrl { get; set; } = "https://www.metrodoporto.pt/metrodoporto/uploads/document/file/794/google_transit.zip";

    public string EstadoUrl { get; set; } = "https://www.metrodoporto.pt/pages/1";

    /// <summary>stop_id da estação no GTFS da Metro (5703 = Campanhã).</summary>
    public string Paragem { get; set; } = "5703";

    /// <summary>Partidas mostradas: próximos N minutos.</summary>
    public int MinutosJanela { get; set; } = 360;

    public int HorasAtualizacaoGtfs { get; set; } = 24;
    public int MinutosAtualizacaoEstado { get; set; } = 5;
    public int IntervaloSegundos { get; set; } = 10;
    public string FusoHorario { get; set; } = "Europe/Lisbon";
}

public record PartidaMetro(string Linha, string? Cor, string? CorTexto, string? Destino, DateTimeOffset Hora, string TripId);

public record EstadoLinhaMetro(string Linha, string Percurso, string Estado, bool Normal, string? Cor);

public record MetroSnapshot(
    IReadOnlyList<PartidaMetro> Partidas,
    IReadOnlyList<EstadoLinhaMetro> EstadoLinhas,
    string Paragem,
    string? ParagemNome,
    double? Latitude,
    double? Longitude,
    DateTimeOffset? ObtidoEm,
    string? Erro,
    DateOnly? GtfsValidoAte)
{
    public static readonly MetroSnapshot Vazio = new([], [], "5703", null, null, null, null, null, null);
}

/// <summary>Horário (GTFS) das partidas numa estação da Metro.</summary>
public class MetroHorario
{
    private record Passagem(string TripId, string RouteId, string? Destino, string ServiceId, int Segundos);

    private readonly List<Passagem> _passagens = [];
    private readonly Dictionary<string, (string Nome, string? Cor, string? CorTexto)> _rotas = [];
    private GtfsCalendario _calendario = new();

    public string? ParagemNome { get; private set; }
    public double? Latitude { get; private set; }
    public double? Longitude { get; private set; }
    public DateOnly? ValidoAte => _calendario.Fim;

    public static MetroHorario Carregar(string zipPath, string paragem)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var h = new MetroHorario { _calendario = GtfsCalendario.Carregar(zip) };

        if (GtfsZip.Ler(zip, "stops.txt").FirstOrDefault(r => r["stop_id"] == paragem) is { } s)
        {
            h.ParagemNome = s.GetValueOrDefault("stop_name");
            if (double.TryParse(s.GetValueOrDefault("stop_lat"), System.Globalization.CultureInfo.InvariantCulture, out var lat)
                && double.TryParse(s.GetValueOrDefault("stop_lon"), System.Globalization.CultureInfo.InvariantCulture, out var lon))
                (h.Latitude, h.Longitude) = (lat, lon);
        }

        foreach (var r in GtfsZip.Ler(zip, "routes.txt"))
            h._rotas[r["route_id"]] = (
                string.IsNullOrWhiteSpace(r.GetValueOrDefault("route_short_name")) ? r["route_id"] : r["route_short_name"],
                r.GetValueOrDefault("route_color") is { Length: 6 } c ? "#" + c : null,
                r.GetValueOrDefault("route_text_color") is { Length: 6 } ct ? "#" + ct : null);

        // Hora na estação e se é a última paragem da viagem (aí é chegada, não partida).
        var naParagem = new Dictionary<string, (int Seq, int Segundos)>();
        var ultimaSeq = new Dictionary<string, int>();
        foreach (var (trip, stop, seq, seg) in GtfsZip.LerHorarios(zip))
        {
            if (stop == paragem) naParagem[trip] = (seq, seg);
            if (!ultimaSeq.TryGetValue(trip, out var u) || seq > u) ultimaSeq[trip] = seq;
        }

        foreach (var r in GtfsZip.Ler(zip, "trips.txt"))
        {
            var trip = r["trip_id"];
            if (!naParagem.TryGetValue(trip, out var p) || p.Segundos < 0 || p.Seq == ultimaSeq[trip]) continue;
            h._passagens.Add(new Passagem(trip, r["route_id"], r.GetValueOrDefault("trip_headsign"), r["service_id"], p.Segundos));
        }
        h._passagens.Sort((a, b) => a.Segundos.CompareTo(b.Segundos));
        return h;
    }

    /// <summary>Partidas entre agora (−1 min) e agora + janela, incluindo as do dia de serviço anterior depois da meia-noite.</summary>
    public IReadOnlyList<PartidaMetro> Proximas(DateTimeOffset agora, TimeSpan janela, TimeZoneInfo fuso)
    {
        var local = TimeZoneInfo.ConvertTime(agora, fuso);
        var hoje = DateOnly.FromDateTime(local.DateTime);
        var res = new List<PartidaMetro>();
        foreach (var dia in new[] { hoje.AddDays(-1), hoje })
            foreach (var p in _passagens)
            {
                var hora = GtfsZip.Instante(dia, p.Segundos, fuso);
                if (hora < local.AddMinutes(-1) || hora > local + janela || !_calendario.Ativo(p.ServiceId, dia)) continue;
                var rota = _rotas.GetValueOrDefault(p.RouteId);
                res.Add(new PartidaMetro(rota.Nome ?? p.RouteId, rota.Cor, rota.CorTexto, p.Destino, hora, p.TripId));
            }
        return res.OrderBy(p => p.Hora).ToList();
    }
}

public static partial class MetroEstado
{
    private static readonly Dictionary<string, string> Linhas = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Estádio do Dragão - Sr. de Matosinhos"] = "A",
        ["Estádio do Dragão - Póvoa de Varzim"] = "B",
        ["Campanhã - ISMAI"] = "C",
        ["Hospital S. João - Vila d'Este"] = "D",
        ["Trindade - Aeroporto"] = "E",
        ["Fânzeres - Senhora da Hora"] = "F",
        ["Casa da Música - Santo Ovídio"] = "G",
        ["Casa da Música - Sto. Ovídio"] = "G",
    };

    // Cada linha no bloco "Estado das Linhas": <h4>Percurso</h4> ... <p>Ok|Condicionada|Interrompida</p> (por vezes dentro de <a>).
    [GeneratedRegex(@"<h4[^>]*>\s*(?<percurso>[^<]+?)\s*</h4>(?<resto>.{0,600}?)(?=<h4|</ul>)", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex Bloco();

    [GeneratedRegex(@">\s*(?<estado>Ok|Condicionada|Interrompida)\s*<", RegexOptions.IgnoreCase)]
    private static partial Regex Estado();

    public static IReadOnlyList<EstadoLinhaMetro> Ler(string html, IReadOnlyDictionary<string, string?> cores)
    {
        var inicio = html.IndexOf("Estado das Linhas", StringComparison.OrdinalIgnoreCase);
        if (inicio < 0) return [];
        var res = new List<EstadoLinhaMetro>();
        foreach (Match m in Bloco().Matches(html[inicio..]))
        {
            if (Estado().Match(m.Groups["resto"].Value) is not { Success: true } e) continue;
            var percurso = WebUtility.HtmlDecode(m.Groups["percurso"].Value.Trim());
            var estado = char.ToUpperInvariant(e.Groups["estado"].Value[0]) + e.Groups["estado"].Value[1..].ToLowerInvariant();
            var linha = Linhas.GetValueOrDefault(percurso) ?? percurso;
            res.Add(new EstadoLinhaMetro(linha, percurso, estado, estado == "Ok", cores.GetValueOrDefault(linha)));
        }
        return res;
    }
}

public class MetroRealtimeStore
{
    private volatile MetroSnapshot _atual = MetroSnapshot.Vazio;
    public MetroSnapshot Atual => _atual;
    public void Definir(MetroSnapshot s) => _atual = s;
    public void RegistarErro(string erro) => _atual = _atual with { Erro = erro };
}

public class MetroClient(HttpClient http)
{
    public Task<string> ObterHtmlAsync(string url, CancellationToken ct) => http.GetStringAsync(url, ct);

    public async Task DescarregarAsync(string url, string destino, CancellationToken ct)
    {
        var tmp = destino + ".tmp";
        await using (var origem = await http.GetStreamAsync(url, ct))
        await using (var f = File.Create(tmp))
            await origem.CopyToAsync(f, ct);
        File.Move(tmp, destino, overwrite: true);
    }
}

public class MetroRealtimeWorker(
    IServiceScopeFactory scopes, MetroRealtimeStore store, IOptions<MetroOptions> options, IOptions<GtfsOptions> gtfsOptions,
    IWebHostEnvironment env, ILogger<MetroRealtimeWorker> logger, TimeProvider tempo) : BackgroundService
{
    private MetroHorario? _horario;
    private DateTimeOffset _proximoGtfs = DateTimeOffset.MinValue;
    private DateTimeOffset _proximoEstado = DateTimeOffset.MinValue;
    private IReadOnlyList<EstadoLinhaMetro> _estado = [];

    private string Zip
    {
        get
        {
            var raiz = Path.IsPathRooted(gtfsOptions.Value.StoragePath)
                ? gtfsOptions.Value.StoragePath
                : Path.Combine(env.ContentRootPath, gtfsOptions.Value.StoragePath);
            return Path.Combine(raiz, "metro", "gtfs_metro.zip");
        }
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var opt = options.Value;
        if (!opt.Ativo)
        {
            logger.LogInformation("Integração Metro do Porto desativada (Metro:Ativo = false).");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, opt.IntervaloSegundos)), tempo);
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var client = scope.ServiceProvider.GetRequiredService<MetroClient>();
                var agora = tempo.GetUtcNow();
                await AtualizarGtfsAsync(client, opt, agora, ct);
                await AtualizarEstadoAsync(client, opt, agora, ct);
                if (_horario is null)
                {
                    store.RegistarErro("Ainda não foi possível obter o horário (GTFS) da Metro do Porto.");
                    continue;
                }
                store.Definir(new MetroSnapshot(
                    _horario.Proximas(agora, TimeSpan.FromMinutes(opt.MinutosJanela), FlixbusMapper.Fuso(opt.FusoHorario)),
                    _estado, opt.Paragem, _horario.ParagemNome, _horario.Latitude, _horario.Longitude, agora, null, _horario.ValidoAte));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Falha ao atualizar os dados da Metro do Porto.");
                store.RegistarErro($"Falha nos dados da Metro do Porto ({tempo.GetLocalNow():HH:mm}): {ex.Message}");
            }
        } while (await timer.WaitForNextTickAsync(ct));
    }

    private async Task AtualizarGtfsAsync(MetroClient client, MetroOptions opt, DateTimeOffset agora, CancellationToken ct)
    {
        if (agora < _proximoGtfs && _horario is not null) return;
        _proximoGtfs = agora.AddHours(Math.Max(1, opt.HorasAtualizacaoGtfs));
        Directory.CreateDirectory(Path.GetDirectoryName(Zip)!);
        try
        {
            if (!File.Exists(Zip) || File.GetLastWriteTimeUtc(Zip) < agora.UtcDateTime.AddHours(-opt.HorasAtualizacaoGtfs))
            {
                logger.LogInformation("A descarregar o GTFS da Metro do Porto: {Url}", opt.GtfsUrl);
                await client.DescarregarAsync(opt.GtfsUrl, Zip, ct);
                _horario = null;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Não foi possível descarregar o GTFS da Metro; a usar a cópia local, se existir.");
        }
        if (_horario is null && File.Exists(Zip))
        {
            _horario = await Task.Run(() => MetroHorario.Carregar(Zip, opt.Paragem), ct);
            logger.LogInformation("GTFS da Metro do Porto carregado (válido até {Fim}).", _horario.ValidoAte);
        }
    }

    private async Task AtualizarEstadoAsync(MetroClient client, MetroOptions opt, DateTimeOffset agora, CancellationToken ct)
    {
        if (agora < _proximoEstado) return;
        _proximoEstado = agora.AddMinutes(Math.Max(1, opt.MinutosAtualizacaoEstado));
        try
        {
            var cores = _horario?.Proximas(agora, TimeSpan.FromHours(3), FlixbusMapper.Fuso(opt.FusoHorario))
                .GroupBy(p => p.Linha).ToDictionary(g => g.Key, g => g.First().Cor) ?? [];
            var estado = MetroEstado.Ler(await client.ObterHtmlAsync(opt.EstadoUrl, ct), cores);
            if (estado.Count > 0) _estado = estado;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogDebug(ex, "Estado das linhas da Metro indisponível.");
        }
    }
}
