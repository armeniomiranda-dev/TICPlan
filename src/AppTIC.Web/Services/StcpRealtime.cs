using System.Globalization;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace AppTIC.Web.Services;

/// <summary>
/// Posições em tempo real dos autocarros da STCP (Urban Platform / FIWARE da Porto Digital, a mesma fonte do projeto
/// linha-viva), filtradas às viagens que param no TIC. Cada autocarro é associado à viagem do GTFS oficial da STCP
/// (dados abertos da Câmara do Porto) para saber em que paragem do TIC passa, a hora prevista e o atraso.
/// </summary>
public class StcpOptions
{
    public const string Section = "Stcp";

    public static readonly IReadOnlyList<string> ParagensTicPadrao = ["TIC1", "TIC2", "TIC3", "CMP1", "CMP2", "CMP5", "CMP6"];

    public bool Ativo { get; set; } = true;

    public string FiwareUrl { get; set; } = "https://broker.fiware.urbanplatform.portodigital.pt/v2/entities?q=vehicleType==bus&limit=1000";

    /// <summary>API CKAN do portal de dados abertos: devolve a lista de GTFS publicados (usa-se o mais recente).</summary>
    public string GtfsPacoteUrl { get; set; } = "https://dadosabertos.cm-porto.pt/api/3/action/package_show?id=horarios-paragens-e-rotas-stcp";

    /// <summary>URL fixo do GTFS; quando definido, não se consulta o portal.</summary>
    public string? GtfsUrl { get; set; }

    /// <summary>De quantas em quantas horas se verifica se há um GTFS novo.</summary>
    public int HorasAtualizacaoGtfs { get; set; } = 24;

    /// <summary>
    /// Paragens STCP do TIC. Vazio por omissão: o binder de configuração acrescenta aos valores iniciais de uma lista
    /// em vez de os substituir, por isso o valor padrão está em <see cref="ParagensTicPadrao"/>.
    /// </summary>
    public List<string> ParagensTic { get; set; } = [];

    public IReadOnlyList<string> ParagensTicEfetivas =>
        ParagensTic.Count > 0 ? ParagensTic.Distinct(StringComparer.OrdinalIgnoreCase).ToList() : ParagensTicPadrao;

    public int IntervaloSegundos { get; set; } = 10;

    /// <summary>Depois de passar no TIC, o autocarro continua no mapa até estar a este tempo de viagem (pelo horário) do TIC.</summary>
    public int MinutosAposTic { get; set; } = 15;

    /// <summary>A esta distância (ou menos) da paragem do TIC o autocarro conta como "no TIC".</summary>
    public int DistanciaNoTicMetros { get; set; } = 150;

    public string FusoHorario { get; set; } = "Europe/Lisbon";
}

// ---- FIWARE (NGSI v2): /v2/entities?q=vehicleType==bus ----

public record FiwareVehicle(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("annotations")] FiwareProp<List<string>>? Annotations,
    [property: JsonPropertyName("bearing")] FiwareProp<double?>? Bearing,
    [property: JsonPropertyName("heading")] FiwareProp<double?>? Heading,
    [property: JsonPropertyName("fleetVehicleId")] FiwareProp<string>? FleetVehicleId,
    [property: JsonPropertyName("location")] FiwareProp<FiwarePoint>? Location,
    [property: JsonPropertyName("observationDateTime")] FiwareProp<DateTimeOffset?>? ObservationDateTime,
    [property: JsonPropertyName("speed")] FiwareProp<double?>? Speed);

public record FiwareProp<T>([property: JsonPropertyName("value")] T Value);

public record FiwarePoint([property: JsonPropertyName("coordinates")] double[] Coordinates);

/// <summary>Autocarro tal como vem da FIWARE, já com as anotações STCP separadas.</summary>
public record VeiculoStcp(
    string Veiculo, string Linha, string? Sentido, string? Viagem,
    double Latitude, double Longitude, double? Rumo, double? Velocidade, DateTimeOffset? AtualizadoEm)
{
    public static VeiculoStcp? De(FiwareVehicle v)
    {
        if (v.Location?.Value.Coordinates is not [var lon, var lat, ..]) return null;
        string? Anot(string prefixo) => v.Annotations?.Value?.FirstOrDefault(a => a.StartsWith(prefixo, StringComparison.Ordinal))?[prefixo.Length..];
        var linha = Anot("stcp:route:");
        if (string.IsNullOrEmpty(linha)) return null;
        return new VeiculoStcp(
            v.FleetVehicleId?.Value ?? v.Id, linha, Anot("stcp:sentido:"), Anot("stcp:nr_viagem:"),
            lat, lon, v.Bearing?.Value ?? v.Heading?.Value, v.Speed?.Value, v.ObservationDateTime?.Value);
    }
}

// ---- Índice do GTFS da STCP, só com as linhas que servem o TIC ----

public record ParagemStcp(string Id, string Nome, double Latitude, double Longitude);

public record RotaStcp(string RouteId, string Nome, string? Cor, string? CorTexto);

/// <summary>
/// Viagem do GTFS de uma linha que serve o TIC: paragens por ordem e horas (segundos desde o início do dia de serviço).
/// <see cref="IndiceTic"/> é -1 nas variantes que não passam no TIC (ex. viagens encurtadas).
/// </summary>
public record ViagemStcp(
    string TripId, string RouteId, string? DirecaoId, string ServiceId, string? Destino,
    string[] Paragens, int[] Horas, int IndiceTic)
{
    public bool PassaTic => IndiceTic >= 0;
}

/// <summary>Resultado da associação de um autocarro a uma viagem do GTFS.</summary>
public record ViagemAssociada(ViagemStcp Viagem, DateOnly DiaServico, int IndiceProximo, double DistanciaMetros, int AtrasoMinutos);

public class StcpGtfsIndice
{
    /// <summary>Maior distância do autocarro à paragem mais próxima da viagem para aceitar a associação.</summary>
    public const double DistanciaMaximaMetros = 400;

    /// <summary>Maior diferença entre a hora atual e a hora prevista na paragem mais próxima para aceitar a associação.</summary>
    public const int AtrasoMaximoMinutos = 30;

    public required IReadOnlyDictionary<string, ParagemStcp> Paragens { get; init; }
    public required IReadOnlyDictionary<string, RotaStcp> Rotas { get; init; }

    /// <summary>Todas as viagens (com e sem TIC) das linhas/sentidos com alguma viagem pelo TIC, por "route_id|direction_id".</summary>
    public required IReadOnlyDictionary<string, List<ViagemStcp>> PorLinha { get; init; }

    public required IReadOnlyDictionary<string, ViagemStcp> PorTripId { get; init; }

    public required GtfsCalendario Calendario { get; init; }

    public required IReadOnlyList<ParagemStcp> ParagensTic { get; init; }
    public string? Versao { get; init; }
    public DateTimeOffset CarregadoEm { get; init; }

    public static string ChaveLinha(string routeId, string? direcao) => $"{routeId}|{direcao}";

    public bool ServicoAtivo(string serviceId, DateOnly data) => Calendario.Ativo(serviceId, data);

    /// <summary>
    /// Associa o autocarro a uma viagem do GTFS. Usa o trip_id exato quando a versão do horário coincide; senão escolhe,
    /// entre as viagens da mesma linha e sentido ativas no dia, a que tem a hora prevista na paragem mais próxima do
    /// autocarro mais perto da hora atual (o nº da viagem na FIWARE refere-se a versões do horário ainda não publicadas
    /// no GTFS e não é fiável entre versões). Devolve null se a linha/sentido não serve o TIC ou nada encaixa.
    /// </summary>
    public ViagemAssociada? Associar(VeiculoStcp v, DateTimeOffset agoraLocal)
    {
        var hoje = DateOnly.FromDateTime(agoraLocal.DateTime);

        if (v.Viagem is not null && PorTripId.TryGetValue(v.Viagem, out var exata))
            foreach (var dia in new[] { hoje, hoje.AddDays(-1) })
                if (ServicoAtivo(exata.ServiceId, dia) && Avaliar(exata, dia) is { } a) return a;

        if (!PorLinha.TryGetValue(ChaveLinha(v.Linha, v.Sentido), out var viagens)) return null;

        ViagemAssociada? melhor = null;
        foreach (var dia in new[] { hoje, hoje.AddDays(-1) }) // viagens depois da meia-noite pertencem ao dia anterior
        {
            var agoraSeg = (int)(agoraLocal - new DateTimeOffset(dia.ToDateTime(TimeOnly.MinValue), agoraLocal.Offset)).TotalSeconds;
            foreach (var viagem in viagens)
            {
                // Só viagens a decorrer (com margem): evita calcular distâncias para o dia inteiro.
                if (viagem.Horas.Length == 0 || agoraSeg < viagem.Horas[0] - 1800 || agoraSeg > viagem.Horas[^1] + 1800) continue;
                if (!ServicoAtivo(viagem.ServiceId, dia)) continue;
                if (Avaliar(viagem, dia) is { } a && (melhor is null || Math.Abs(a.AtrasoMinutos) < Math.Abs(melhor.AtrasoMinutos)))
                    melhor = a;
            }
        }
        return melhor;

        ViagemAssociada? Avaliar(ViagemStcp viagem, DateOnly dia)
        {
            var (indice, distancia) = ParagemMaisProxima(viagem, v.Latitude, v.Longitude);
            if (indice < 0 || distancia > DistanciaMaximaMetros || viagem.Horas[indice] < 0) return null;
            var prevista = new DateTimeOffset(dia.ToDateTime(TimeOnly.MinValue), agoraLocal.Offset).AddSeconds(viagem.Horas[indice]);
            var atraso = (int)Math.Round((agoraLocal - prevista).TotalMinutes);
            return Math.Abs(atraso) > AtrasoMaximoMinutos ? null : new ViagemAssociada(viagem, dia, indice, distancia, atraso);
        }
    }

    public (int Indice, double Distancia) ParagemMaisProxima(ViagemStcp viagem, double lat, double lon)
    {
        var indice = -1;
        var menor = double.MaxValue;
        for (var i = 0; i < viagem.Paragens.Length; i++)
        {
            if (!Paragens.TryGetValue(viagem.Paragens[i], out var p)) continue;
            var d = StcpTic.DistanciaMetros(lat, lon, p.Latitude, p.Longitude);
            if (d < menor) { menor = d; indice = i; }
        }
        return (indice, menor);
    }

    public static StcpGtfsIndice Carregar(string zipPath, IReadOnlyCollection<string> paragensTic, DateTimeOffset agora)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var tic = new HashSet<string>(paragensTic, StringComparer.OrdinalIgnoreCase);
        static double D(string s) => double.Parse(s, CultureInfo.InvariantCulture);

        var paragens = Ler(zip, "stops.txt")
            .Where(r => double.TryParse(r.GetValueOrDefault("stop_lat"), CultureInfo.InvariantCulture, out _))
            .Select(r => new ParagemStcp(r["stop_id"], r.GetValueOrDefault("stop_name") ?? r["stop_id"], D(r["stop_lat"]), D(r["stop_lon"])))
            .GroupBy(p => p.Id).ToDictionary(g => g.Key, g => g.First());

        var rotas = Ler(zip, "routes.txt").Select(r => new RotaStcp(r["route_id"],
                string.IsNullOrWhiteSpace(r.GetValueOrDefault("route_short_name")) ? r["route_id"] : r["route_short_name"],
                r.GetValueOrDefault("route_color") is { Length: 6 } c ? "#" + c : null,
                r.GetValueOrDefault("route_text_color") is { Length: 6 } ct ? "#" + ct : null))
            .GroupBy(r => r.RouteId).ToDictionary(g => g.Key, g => g.First());

        // 1.ª passagem por stop_times (dezenas de MB): que viagens param no TIC.
        var viagensTic = new HashSet<string>();
        foreach (var (trip, paragem, _, _) in LerHorarios(zip))
            if (tic.Contains(paragem)) viagensTic.Add(trip);

        // Linhas/sentidos com alguma viagem pelo TIC: guardam-se todas as suas viagens (também as que não passam no TIC,
        // para que um autocarro numa variante encurtada não seja confundido com uma que passa).
        var trips = Ler(zip, "trips.txt").ToList();
        var linhasTic = trips.Where(r => viagensTic.Contains(r["trip_id"]))
            .Select(r => ChaveLinha(r["route_id"], r.GetValueOrDefault("direction_id"))).ToHashSet();
        var tripsLinhas = trips.Where(r => linhasTic.Contains(ChaveLinha(r["route_id"], r.GetValueOrDefault("direction_id"))))
            .ToDictionary(r => r["trip_id"]);

        // 2.ª passagem: horários dessas viagens.
        var horarios = new Dictionary<string, List<(int Seq, string Paragem, int Hora)>>();
        foreach (var (trip, paragem, seq, hora) in LerHorarios(zip))
        {
            if (!tripsLinhas.ContainsKey(trip)) continue;
            if (!horarios.TryGetValue(trip, out var lista)) horarios[trip] = lista = [];
            lista.Add((seq, paragem, hora));
        }

        var porLinha = new Dictionary<string, List<ViagemStcp>>();
        var porTrip = new Dictionary<string, ViagemStcp>();
        foreach (var (tripId, r) in tripsLinhas)
        {
            if (!horarios.TryGetValue(tripId, out var st)) continue;
            st.Sort((a, b) => a.Seq.CompareTo(b.Seq));
            var ids = st.Select(x => x.Paragem).ToArray();
            var v = new ViagemStcp(tripId, r["route_id"], r.GetValueOrDefault("direction_id"), r["service_id"],
                r.GetValueOrDefault("trip_headsign"), ids, st.Select(x => x.Hora).ToArray(), Array.FindIndex(ids, tic.Contains));
            var chave = ChaveLinha(v.RouteId, v.DirecaoId);
            if (!porLinha.TryGetValue(chave, out var l)) porLinha[chave] = l = [];
            l.Add(v);
            porTrip[tripId] = v;
        }

        var versao = Ler(zip, "feed_info.txt").FirstOrDefault() is { } fi
            ? $"{fi.GetValueOrDefault("feed_start_date")}–{fi.GetValueOrDefault("feed_end_date")}" : null;

        return new StcpGtfsIndice
        {
            Paragens = paragens, Rotas = rotas, PorLinha = porLinha, PorTripId = porTrip,
            Calendario = GtfsCalendario.Carregar(zip),
            ParagensTic = paragensTic.Where(paragens.ContainsKey).Select(id => paragens[id]).ToList(),
            Versao = versao, CarregadoEm = agora
        };
    }

    private static IEnumerable<Dictionary<string, string>> Ler(ZipArchive zip, string nome) => GtfsZip.Ler(zip, nome);
    private static IEnumerable<(string Trip, string Paragem, int Seq, int Hora)> LerHorarios(ZipArchive zip) => GtfsZip.LerHorarios(zip);
}

// ---- Resultado ----

public record PosicaoStcp(
    string Veiculo,
    string RouteId,
    string Linha,               // nome público (route_short_name)
    string? Cor,
    string? CorTexto,
    string? Destino,
    SentidoTic Sentido,
    string ParagemTic,          // TIC1, CMP2, ...
    string? ParagemTicNome,
    DateTimeOffset HoraTicPrevista,
    DateTimeOffset HoraTicEstimada,
    int AtrasoMinutos,          // estimado pela hora prevista na paragem mais próxima
    string? ParagemProxima,
    double DistanciaTicMetros,  // em linha reta
    double Latitude,
    double Longitude,
    double? Rumo,
    double? Velocidade,
    DateTimeOffset? AtualizadoEm,
    string TripId);

public record StcpSnapshot(
    IReadOnlyList<PosicaoStcp> Veiculos,
    IReadOnlyList<ParagemStcp> ParagensTic,
    DateTimeOffset? ObtidoEm,
    string? Erro,
    string? VersaoGtfs,
    int VeiculosEmServico,      // todos os autocarros STCP com GPS
    int SemCorrespondencia)     // autocarros de linhas do TIC sem viagem do GTFS compatível com a posição/hora
{
    public static readonly StcpSnapshot Vazio = new([], [], null, null, null, 0, 0);
}

public static class StcpTic
{
    public static double DistanciaMetros(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371000;
        static double Rad(double g) => g * Math.PI / 180;
        var dLat = Rad(lat2 - lat1);
        var dLon = Rad(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * R * Math.Asin(Math.Sqrt(a));
    }

    /// <summary>
    /// Associa cada autocarro à sua viagem no GTFS e devolve só os que param no TIC: ainda a caminho, junto ao TIC,
    /// ou que passaram e estão a menos de <c>minutosApos</c> minutos de viagem do TIC.
    /// </summary>
    public static (List<PosicaoStcp> Posicoes, int SemCorrespondencia) Filtrar(
        IEnumerable<VeiculoStcp> veiculos, StcpGtfsIndice gtfs, DateTimeOffset agora, TimeZoneInfo fuso,
        int distanciaNoTic, int minutosApos)
    {
        var local = TimeZoneInfo.ConvertTime(agora, fuso);
        var res = new List<PosicaoStcp>();
        var semCorrespondencia = 0;

        foreach (var v in veiculos)
        {
            var assoc = gtfs.Associar(v, local);
            if (assoc is null)
            {
                if (gtfs.PorLinha.ContainsKey(StcpGtfsIndice.ChaveLinha(v.Linha, v.Sentido))) semCorrespondencia++;
                continue;
            }
            var (viagem, diaServico, proxima, _, atraso) = assoc;
            if (!viagem.PassaTic) continue; // variante desta linha que não vai ao TIC

            var ticId = viagem.Paragens[viagem.IndiceTic];
            gtfs.Paragens.TryGetValue(ticId, out var paragemTic);
            var distTic = paragemTic is null ? double.NaN : DistanciaMetros(v.Latitude, v.Longitude, paragemTic.Latitude, paragemTic.Longitude);

            // Hora = meia-noite local do dia de serviço + segundos do GTFS (podem passar das 24h).
            var previstaTic = GtfsZip.Instante(diaServico, viagem.Horas[viagem.IndiceTic], fuso);
            var estimadaTic = previstaTic.AddMinutes(atraso);

            var sentido = distTic <= distanciaNoTic ? SentidoTic.NoTic
                : proxima <= viagem.IndiceTic ? SentidoTic.ACaminho
                : SentidoTic.Partiu;
            // Já passou: só se mantém enquanto estiver a menos de "minutosApos" de viagem (pelo horário) depois do TIC.
            // Não se usa a hora estimada, porque um autocarro parado no fim da linha acumula "atraso" e nunca sairia.
            if (sentido == SentidoTic.Partiu && (viagem.Horas[proxima] - viagem.Horas[viagem.IndiceTic]) / 60.0 > minutosApos) continue;

            gtfs.Rotas.TryGetValue(viagem.RouteId, out var rota);
            res.Add(new PosicaoStcp(
                v.Veiculo, viagem.RouteId, rota?.Nome ?? viagem.RouteId, rota?.Cor, rota?.CorTexto, viagem.Destino,
                sentido, ticId, paragemTic?.Nome, previstaTic, estimadaTic, atraso,
                gtfs.Paragens.GetValueOrDefault(viagem.Paragens[proxima])?.Nome,
                Math.Round(distTic), v.Latitude, v.Longitude, v.Rumo, v.Velocidade,
                v.AtualizadoEm is { } a ? TimeZoneInfo.ConvertTime(a, fuso) : null, viagem.TripId));
        }
        return (res.OrderBy(p => p.Sentido).ThenBy(p => p.HoraTicEstimada).ToList(), semCorrespondencia);
    }
}

public class StcpRealtimeStore
{
    private volatile StcpSnapshot _atual = StcpSnapshot.Vazio;
    public StcpSnapshot Atual => _atual;
    public void Definir(StcpSnapshot s) => _atual = s;
    public void RegistarErro(string erro) => _atual = _atual with { Erro = erro };
}

// ---- CKAN (dados abertos): package_show ----

public record CkanResposta<T>([property: JsonPropertyName("success")] bool Success, [property: JsonPropertyName("result")] T? Result);
public record CkanPacote([property: JsonPropertyName("resources")] List<CkanRecurso>? Resources);
public record CkanRecurso(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("url")] string? Url,
    [property: JsonPropertyName("format")] string? Format,
    [property: JsonPropertyName("created")] DateTime? Created,
    [property: JsonPropertyName("last_modified")] DateTime? LastModified);

public class StcpClient(HttpClient http)
{
    public async Task<IReadOnlyList<VeiculoStcp>> ObterVeiculosAsync(string url, CancellationToken ct)
    {
        var lista = await http.GetFromJsonAsync<List<FiwareVehicle>>(url, ct) ?? [];
        return lista.Select(VeiculoStcp.De).OfType<VeiculoStcp>().ToList();
    }

    /// <summary>URL do GTFS publicado mais recentemente no portal de dados abertos.</summary>
    public async Task<string?> UrlGtfsMaisRecenteAsync(string pacoteUrl, CancellationToken ct)
    {
        var resp = await http.GetFromJsonAsync<CkanResposta<CkanPacote>>(pacoteUrl, ct);
        return MaisRecente(resp?.Result?.Resources);
    }

    public static string? MaisRecente(IEnumerable<CkanRecurso>? recursos) => recursos?
        .Where(r => r.Url is not null && (r.Format?.Contains("ZIP", StringComparison.OrdinalIgnoreCase) == true
                                          || r.Format?.Contains("GTFS", StringComparison.OrdinalIgnoreCase) == true))
        .OrderByDescending(r => r.Created ?? r.LastModified ?? DateTime.MinValue)
        .FirstOrDefault()?.Url;

    public async Task DescarregarAsync(string url, string destino, CancellationToken ct)
    {
        var tmp = destino + ".tmp";
        await using (var origem = await http.GetStreamAsync(url, ct))
        await using (var f = File.Create(tmp))
            await origem.CopyToAsync(f, ct);
        File.Move(tmp, destino, overwrite: true);
    }
}

/// <summary>Mantém o GTFS da STCP atualizado (uma vez por dia) e lê as posições dos autocarros a cada 10 s.</summary>
public class StcpRealtimeWorker(
    IServiceScopeFactory scopes, StcpRealtimeStore store, IOptions<StcpOptions> options, IOptions<GtfsOptions> gtfsOptions,
    IWebHostEnvironment env, RastoStore rastos, ILogger<StcpRealtimeWorker> logger, TimeProvider tempo) : BackgroundService
{
    private StcpGtfsIndice? _gtfs;
    private DateTimeOffset _proximaVerificacaoGtfs = DateTimeOffset.MinValue;

    private string PastaGtfs
    {
        get
        {
            var raiz = Path.IsPathRooted(gtfsOptions.Value.StoragePath)
                ? gtfsOptions.Value.StoragePath
                : Path.Combine(env.ContentRootPath, gtfsOptions.Value.StoragePath);
            return Path.Combine(raiz, "stcp");
        }
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var opt = options.Value;
        if (!opt.Ativo)
        {
            logger.LogInformation("Integração STCP em tempo real desativada (Stcp:Ativo = false).");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, opt.IntervaloSegundos)), tempo);
        do
        {
            await AtualizarAsync(opt, ct);
        } while (await timer.WaitForNextTickAsync(ct));
    }

    private async Task AtualizarAsync(StcpOptions opt, CancellationToken ct)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var client = scope.ServiceProvider.GetRequiredService<StcpClient>();
            var agora = tempo.GetUtcNow();

            if (agora >= _proximaVerificacaoGtfs || _gtfs is null)
                await AtualizarGtfsAsync(client, opt, agora, ct);
            if (_gtfs is null)
            {
                store.RegistarErro("Ainda não foi possível obter o GTFS da STCP.");
                return;
            }

            var veiculos = await client.ObterVeiculosAsync(opt.FiwareUrl, ct);
            var (posicoes, semCorrespondencia) = StcpTic.Filtrar(veiculos, _gtfs, agora, FlixbusMapper.Fuso(opt.FusoHorario),
                opt.DistanciaNoTicMetros, opt.MinutosAposTic);
            store.Definir(new StcpSnapshot(posicoes, _gtfs.ParagensTic, agora, null, _gtfs.Versao, veiculos.Count, semCorrespondencia));
            rastos.Registar("stcp", posicoes.Select(p => (p.Veiculo, p.Latitude, p.Longitude, p.AtualizadoEm)));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha ao obter dados da STCP em tempo real.");
            store.RegistarErro($"Falha ao contactar a STCP ({tempo.GetLocalNow():HH:mm}): {ex.Message}");
        }
    }

    /// <summary>Descarrega o GTFS se houver um mais recente; usa a cópia em disco se o portal estiver em baixo.</summary>
    private async Task AtualizarGtfsAsync(StcpClient client, StcpOptions opt, DateTimeOffset agora, CancellationToken ct)
    {
        Directory.CreateDirectory(PastaGtfs);
        var zip = Path.Combine(PastaGtfs, "gtfs_stcp.zip");
        var urlFicheiro = Path.Combine(PastaGtfs, "gtfs_stcp.url");
        try
        {
            var url = opt.GtfsUrl ?? await client.UrlGtfsMaisRecenteAsync(opt.GtfsPacoteUrl, ct);
            var urlAtual = File.Exists(urlFicheiro) ? await File.ReadAllTextAsync(urlFicheiro, ct) : null;
            if (url is not null && (url != urlAtual || !File.Exists(zip)))
            {
                logger.LogInformation("A descarregar o GTFS da STCP: {Url}", url);
                await client.DescarregarAsync(url, zip, ct);
                await File.WriteAllTextAsync(urlFicheiro, url, ct);
                _gtfs = null; // força nova leitura
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException && !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Não foi possível verificar o GTFS da STCP; a usar a cópia local, se existir.");
        }
        _proximaVerificacaoGtfs = agora.AddHours(Math.Max(1, opt.HorasAtualizacaoGtfs));

        if (_gtfs is null && File.Exists(zip))
        {
            var inicio = tempo.GetTimestamp();
            _gtfs = await Task.Run(() => StcpGtfsIndice.Carregar(zip, opt.ParagensTicEfetivas, agora), ct);
            logger.LogInformation("GTFS da STCP carregado ({Versao}): {Linhas} linhas/sentidos do TIC, {Viagens} viagens, em {Ms} ms.",
                _gtfs.Versao, _gtfs.PorLinha.Count, _gtfs.PorTripId.Count, (int)tempo.GetElapsedTime(inicio).TotalMilliseconds);
        }
    }
}
