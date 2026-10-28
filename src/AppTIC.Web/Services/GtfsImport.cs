using System.Globalization;
using System.IO.Compression;
using AppTIC.Web.Data;
using AppTIC.Web.Models.Domain;
using Microsoft.EntityFrameworkCore;

namespace AppTIC.Web.Services;

public record GtfsParagem(string StopId, string Nome, decimal? Latitude, decimal? Longitude);
public record GtfsRota(string RouteId, string? Linha, string? Designacao);

/// <summary>Viagem que passa no TIC. DiasDesvio &gt; 0 quando a hora no TIC é ≥ 24:00 (passa no TIC no dia seguinte ao dia de serviço).</summary>
public record GtfsViagem(string TripId, string RouteId, string ServiceId, IReadOnlyList<string> Paragens,
    TimeOnly? Chegada, TimeOnly? Partida, int DiasDesvio);

/// <summary>Conteúdo do GTFS filtrado TIC, pronto a importar.</summary>
public class GtfsDadosImportacao
{
    public Dictionary<string, GtfsParagem> Paragens { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, GtfsRota> Rotas { get; } = new(StringComparer.Ordinal);
    public List<GtfsViagem> Viagens { get; } = [];
    public List<string> Avisos { get; } = [];

    /// <summary>Datas (dias de serviço) de cada service_id, de calendar.txt + calendar_dates.txt.</summary>
    public Dictionary<string, SortedSet<DateOnly>> Servicos { get; } = new(StringComparer.Ordinal);

    /// <summary>Dias em que a viagem passa no TIC, dentro de [inicio, fim].</summary>
    public IEnumerable<DateOnly> Dias(GtfsViagem v, DateOnly inicio, DateOnly fim) =>
        Servicos.TryGetValue(v.ServiceId, out var datas)
            ? datas.GetViewBetween(inicio.AddDays(-v.DiasDesvio), fim.AddDays(-v.DiasDesvio)).Select(d => d.AddDays(v.DiasDesvio))
            : [];

    public (DateOnly Inicio, DateOnly Fim)? Intervalo()
    {
        var todas = Servicos.Values.Where(s => s.Count > 0).ToList();
        return todas.Count == 0 ? null : (todas.Min(s => s.Min), todas.Max(s => s.Max));
    }
}

public record GtfsImportRotaPreview(string RouteId, string? Linha, string? Designacao, int Viagens, int Novas, int Atualizar, int Conflitos);

public record GtfsImportPreview(
    int ParagensNovas, int ParagensAtualizar, int ParagensIguais,
    IReadOnlyList<GtfsImportRotaPreview> Rotas, IReadOnlyList<string> Conflitos, IReadOnlyList<string> Avisos,
    DateOnly Inicio, DateOnly Fim, int DiasCirculacao);

public record GtfsImportResultado(int ParagensNovas, int ParagensAtualizadas, int CirculacoesNovas, int CirculacoesAtualizadas, int Ignoradas, int Dias);

/// <summary>
/// Importa paragens, circulações (com percurso) e calendário a partir do GTFS filtrado TIC de um feed.
/// Paragem ↔ stop_id (Código); Circulação ↔ trip_id (ID de viagem). Volta a importar atualiza os existentes.
/// </summary>
public class GtfsImportService(ApplicationDbContext db, GtfsFeedService gtfs, IActivityLog log)
{
    private const int MaxCodigo = 50;

    public async Task<GtfsDadosImportacao> LerAsync(GtfsFeed feed, CancellationToken ct = default)
    {
        var path = gtfs.GetFullPathFiltrado(feed);
        if (path is null || !File.Exists(path))
            throw new InvalidDataException("O feed ainda não tem GTFS filtrado TIC.");
        var operador = feed.OperadorId is null ? null : await db.Operadores.FindAsync([feed.OperadorId], ct);
        var tic = gtfs.ParagensTic(operador);
        return await Task.Run(() => Ler(path, tic), ct);
    }

    public async Task<GtfsImportPreview> PreverAsync(GtfsFeed feed, GtfsDadosImportacao dados, DateOnly inicio, DateOnly fim, CancellationToken ct = default)
    {
        var codigos = dados.Paragens.Keys.ToList();
        var existentes = await db.Paragens.Where(p => p.OwnerId == feed.OwnerId && codigos.Contains(p.Codigo)).ToDictionaryAsync(p => p.Codigo, ct);
        int novas = 0, atualizar = 0, iguais = 0;
        foreach (var p in dados.Paragens.Values)
        {
            if (!existentes.TryGetValue(p.StopId, out var e)) novas++;
            else if (Difere(e, p)) atualizar++;
            else iguais++;
        }

        var circulacoes = await CirculacoesExistentes(feed.OwnerId, dados, ct);
        var conflitos = new List<string>();
        var rotas = dados.Viagens.GroupBy(v => v.RouteId).Select(g =>
        {
            int n = 0, u = 0, c = 0;
            foreach (var v in g)
                switch (Estado(feed, v, circulacoes, out var motivo))
                {
                    case null: n++; break;
                    case true: u++; break;
                    default: c++; if (conflitos.Count < 50) conflitos.Add(motivo!); break;
                }
            var r = dados.Rotas.GetValueOrDefault(g.Key);
            return new GtfsImportRotaPreview(g.Key, r?.Linha, r?.Designacao, g.Count(), n, u, c);
        }).OrderBy(r => r.Linha, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.RouteId).ToList();

        var dias = dados.Viagens.Sum(v => dados.Dias(v, inicio, fim).Count());
        return new GtfsImportPreview(novas, atualizar, iguais, rotas, conflitos, dados.Avisos, inicio, fim, dias);
    }

    public async Task<GtfsImportResultado> ImportarAsync(GtfsFeed feed, GtfsDadosImportacao dados, ISet<string> routeIds,
        DateOnly inicio, DateOnly fim, CancellationToken ct = default)
    {
        var operador = await db.Operadores.FirstAsync(o => o.Id == feed.OperadorId, ct);
        var tic = gtfs.ParagensTic(operador).ToHashSet(StringComparer.Ordinal);
        var hoje = DateOnly.FromDateTime(DateTime.Today);
        var viagens = dados.Viagens.Where(v => routeIds.Contains(v.RouteId)).ToList();
        var circulacoes = await CirculacoesExistentes(feed.OwnerId, dados, ct);
        var aImportar = viagens.Where(v => Estado(feed, v, circulacoes, out _) is not false).ToList();

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // 1) Paragens usadas pelas viagens importadas
        var codigos = aImportar.SelectMany(v => v.Paragens).Distinct().ToList();
        var paragens = await db.Paragens.Where(p => p.OwnerId == feed.OwnerId && codigos.Contains(p.Codigo)).ToDictionaryAsync(p => p.Codigo, ct);
        int paragensNovas = 0, paragensAtualizadas = 0;
        foreach (var codigo in codigos)
        {
            var g = dados.Paragens[codigo];
            if (!paragens.TryGetValue(codigo, out var p))
            {
                paragens[codigo] = p = new Paragem { OwnerId = feed.OwnerId, Codigo = codigo };
                db.Paragens.Add(p);
                paragensNovas++;
            }
            else if (Difere(p, g)) paragensAtualizadas++;
            else continue;
            p.Nome = Truncar(g.Nome, 200) ?? codigo;
            p.Latitude = g.Latitude;
            p.Longitude = g.Longitude;
        }
        await db.SaveChangesAsync(ct);

        // 2) Circulações existentes: substitui o percurso e os dias dentro do intervalo importado
        var idsAtualizar = aImportar.Where(v => circulacoes.ContainsKey(v.TripId)).Select(v => circulacoes[v.TripId].Id).ToList();
        await db.CirculacaoHorariosTic.Where(h => idsAtualizar.Contains(h.CirculacaoId)).LoadAsync(ct);
        await db.Percursos.Where(p => idsAtualizar.Contains(p.CirculacaoId)).ExecuteDeleteAsync(ct);
        await db.CirculacaoDias.Where(d => idsAtualizar.Contains(d.CirculacaoId) && d.Data >= inicio && d.Data <= fim).ExecuteDeleteAsync(ct);

        // 3) Circulações, percurso e calendário
        int novas = 0, totalDias = 0;
        foreach (var v in aImportar)
        {
            if (!circulacoes.TryGetValue(v.TripId, out var c))
            {
                c = new Circulacao
                {
                    OwnerId = feed.OwnerId, OperadorId = operador.Id, ViagemId = v.TripId, Ambito = operador.Ambito,
                    Comentarios = $"Importado do feed GTFS \"{feed.Nome}\"."
                };
                db.Circulacoes.Add(c);
                novas++;
            }
            var rota = dados.Rotas.GetValueOrDefault(v.RouteId);
            c.LinhaId = Truncar(rota?.Linha, 50);
            c.DesignacaoLinha = Truncar(rota?.Designacao, 200);
            c.Origem = paragens[v.Paragens[0]];
            c.Destino = paragens[v.Paragens[^1]];
            c.GtfsFeedId = feed.Id;
            // Percurso a partir da paragem do TIC (inclusive).
            var noTic = Math.Max(0, v.Paragens.ToList().FindIndex(tic.Contains));
            c.Percurso = v.Paragens.Skip(noTic).Select((p, i) => new PercursoParagem { Sequencia = i + 1, Paragem = paragens[p] }).ToList();
            var dias = dados.Dias(v, inicio, fim).ToList();
            totalDias += dias.Count;
            c.Dias = dias.Select(d => new CirculacaoDia { Data = d }).ToList();
            // Horas no TIC para o período dos dias importados; os períodos editados à mão mantêm-se.
            if (dias.Count > 0)
                HorariosTic.Definir(c, dias.Min(), dias.Max(), v.Chegada, v.Partida, OrigemHorario.Gtfs, hoje, manterManuais: true);
            else if (c.HorariosTic.Count == 0)
            {
                c.Chegada = v.Chegada;
                c.Partida = v.Partida;
            }
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        var r = new GtfsImportResultado(paragensNovas, paragensAtualizadas, novas, aImportar.Count - novas, viagens.Count - aImportar.Count, totalDias);
        await log.LogAsync(CategoriaRegisto.Gtfs, "Importação",
            $"Importação ({inicio:dd/MM/yyyy}–{fim:dd/MM/yyyy}, {routeIds.Count} rota(s)): paragens {r.ParagensNovas} novas / {r.ParagensAtualizadas} atualizadas; "
            + $"circulações {r.CirculacoesNovas} novas / {r.CirculacoesAtualizadas} atualizadas / {r.Ignoradas} ignoradas (conflito); {r.Dias} dias de calendário.",
            r.Ignoradas > 0 ? NivelRegisto.Aviso : NivelRegisto.Info, feed.Id, feed.OwnerId);
        return r;
    }

    private Task<Dictionary<string, Circulacao>> CirculacoesExistentes(string ownerId, GtfsDadosImportacao dados, CancellationToken ct)
    {
        var ids = dados.Viagens.Select(v => v.TripId).ToList();
        return db.Circulacoes.Where(c => c.OwnerId == ownerId && ids.Contains(c.ViagemId)).ToDictionaryAsync(c => c.ViagemId, ct);
    }

    /// <summary>null = nova, true = atualiza existente, false = conflito (não importada).</summary>
    private static bool? Estado(GtfsFeed feed, GtfsViagem v, Dictionary<string, Circulacao> existentes, out string? motivo)
    {
        motivo = null;
        if (v.TripId.Length > MaxCodigo)
        {
            motivo = $"trip_id \"{v.TripId}\" tem mais de {MaxCodigo} caracteres.";
            return false;
        }
        if (!existentes.TryGetValue(v.TripId, out var c)) return null;
        if (c.OperadorId == feed.OperadorId) return true;
        motivo = $"Já existe a circulação {v.TripId} de outro operador.";
        return false;
    }

    private static bool Difere(Paragem e, GtfsParagem g) =>
        e.Nome != (Truncar(g.Nome, 200) ?? g.StopId) || e.Latitude != g.Latitude || e.Longitude != g.Longitude;

    private static string? Truncar(string? s, int max) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Length <= max ? s : s[..max];

    // ---- Leitura do GTFS ----

    public static GtfsDadosImportacao Ler(string zipPath, IReadOnlyCollection<string> ticStopIds)
    {
        var tic = ticStopIds.ToHashSet(StringComparer.Ordinal);
        var dados = new GtfsDadosImportacao();
        using var zip = ZipFile.OpenRead(zipPath);
        var entries = zip.Entries.Where(e => !string.IsNullOrEmpty(e.Name))
            .GroupBy(e => e.Name.ToLowerInvariant()).ToDictionary(g => g.Key, g => g.First());
        ZipArchiveEntry? Entry(string name) => entries.GetValueOrDefault(name);

        if (Entry("stops.txt") is { } stops)
            GtfsTicFilter.ForEachRow(stops, row =>
            {
                if (row.Get("stop_id") is not { Length: > 0 } id) return;
                if (id.Length > MaxCodigo) { dados.Avisos.Add($"stop_id \"{id}\" tem mais de {MaxCodigo} caracteres; ignorado."); return; }
                dados.Paragens[id] = new GtfsParagem(id, row.Get("stop_name") ?? id, Coord(row.Get("stop_lat")), Coord(row.Get("stop_lon")));
            });

        if (Entry("routes.txt") is { } routes)
            GtfsTicFilter.ForEachRow(routes, row =>
            {
                if (row.Get("route_id") is not { Length: > 0 } id) return;
                var curto = row.Get("route_short_name");
                var longo = row.Get("route_long_name");
                dados.Rotas[id] = new GtfsRota(id, string.IsNullOrEmpty(curto) ? id : curto, string.IsNullOrEmpty(longo) ? null : longo);
            });

        var trips = new Dictionary<string, (string Route, string Service)>(StringComparer.Ordinal);
        if (Entry("trips.txt") is { } tripsEntry)
            GtfsTicFilter.ForEachRow(tripsEntry, row =>
            {
                if (row.Get("trip_id") is { Length: > 0 } id)
                    trips[id] = (row.Get("route_id") ?? "", row.Get("service_id") ?? "");
            });

        var horarios = new Dictionary<string, List<(int Seq, string Stop, string? Chegada, string? Partida)>>(StringComparer.Ordinal);
        if (Entry("stop_times.txt") is { } stopTimes)
            GtfsTicFilter.ForEachRow(stopTimes, row =>
            {
                if (row.Get("trip_id") is not { Length: > 0 } t || row.Get("stop_id") is not { Length: > 0 } s) return;
                int.TryParse(row.Get("stop_sequence"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var seq);
                if (!horarios.TryGetValue(t, out var list)) horarios[t] = list = [];
                list.Add((seq, s, row.Get("arrival_time"), row.Get("departure_time")));
            });

        foreach (var (tripId, (routeId, serviceId)) in trips)
        {
            if (!horarios.TryGetValue(tripId, out var list)) continue;
            list.Sort((a, b) => a.Seq.CompareTo(b.Seq));
            var noTic = list.Select((h, i) => (h, i)).Where(x => tic.Contains(x.h.Stop)).Select(x => x.i).ToList();
            if (noTic.Count == 0) continue;
            var paragens = list.Select(h => h.Stop).Where(dados.Paragens.ContainsKey).ToList();
            if (paragens.Count == 0) continue;

            // Chegada ao TIC (se não for a origem) e partida do TIC (se não for o destino).
            var chegada = noTic[0] > 0 ? Hora(list[noTic[0]].Chegada ?? list[noTic[0]].Partida) : null;
            var partida = noTic[^1] < list.Count - 1 ? Hora(list[noTic[^1]].Partida ?? list[noTic[^1]].Chegada) : null;
            if (chegada is null && partida is null) partida = Hora(list[noTic[0]].Partida);
            var (hPartida, dPartida) = partida ?? (null, 0);
            var (hChegada, dChegada) = chegada ?? (null, 0);
            dados.Viagens.Add(new GtfsViagem(tripId, routeId, serviceId, paragens, hChegada, hPartida, partida is not null ? dPartida : dChegada));
        }

        LerCalendario(dados, Entry("calendar.txt"), Entry("calendar_dates.txt"));
        return dados;
    }

    private static void LerCalendario(GtfsDadosImportacao dados, ZipArchiveEntry? calendar, ZipArchiveEntry? calendarDates)
    {
        SortedSet<DateOnly> Servico(string id) =>
            dados.Servicos.TryGetValue(id, out var s) ? s : dados.Servicos[id] = [];
        string[] semana = ["sunday", "monday", "tuesday", "wednesday", "thursday", "friday", "saturday"];

        if (calendar is not null)
            GtfsTicFilter.ForEachRow(calendar, row =>
            {
                if (row.Get("service_id") is not { Length: > 0 } id
                    || Data(row.Get("start_date")) is not { } inicio || Data(row.Get("end_date")) is not { } fim || fim < inicio) return;
                var dias = semana.Select(d => row.Get(d) == "1").ToArray();
                var set = Servico(id);
                for (var d = inicio; d <= fim; d = d.AddDays(1))
                    if (dias[(int)d.DayOfWeek]) set.Add(d);
            });

        if (calendarDates is not null)
            GtfsTicFilter.ForEachRow(calendarDates, row =>
            {
                if (row.Get("service_id") is not { Length: > 0 } id || Data(row.Get("date")) is not { } d) return;
                if (row.Get("exception_type") == "1") Servico(id).Add(d);
                else if (row.Get("exception_type") == "2") Servico(id).Remove(d);
            });
    }

    /// <summary>"HH:MM:SS" do GTFS (pode passar das 24:00) → hora do dia e nº de dias a somar.</summary>
    private static (TimeOnly?, int)? Hora(string? s)
    {
        var partes = s?.Split(':');
        if (partes is not { Length: >= 2 }
            || !int.TryParse(partes[0], out var h) || !int.TryParse(partes[1], out var m) || h < 0 || m is < 0 or > 59) return null;
        var seg = partes.Length > 2 && int.TryParse(partes[2], out var x) && x is >= 0 and < 60 ? x : 0;
        return (new TimeOnly(h % 24, m, seg), h / 24);
    }

    private static DateOnly? Data(string? s) =>
        DateOnly.TryParseExact(s, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    private static decimal? Coord(string? s) =>
        decimal.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? Math.Round(d, 6) : null;
}
