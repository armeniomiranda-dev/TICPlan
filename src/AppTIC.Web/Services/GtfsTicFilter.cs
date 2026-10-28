using System.Globalization;
using System.IO.Compression;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;

namespace AppTIC.Web.Services;

public record GtfsTicResult(
    int Viagens, int Horarios, int Rotas, int Paragens,
    IReadOnlyList<(string StopId, string Nome)> ParagensTicEncontradas,
    IReadOnlyList<string> ParagensTicEmFalta);

/// <summary>
/// Filtra um GTFS para as viagens que passam numa das paragens do TIC (mesma lógica do GTFS_Agg):
/// stop_times → trips → routes/stops/calendars/shapes referenciados. Os ficheiros são lidos em streaming,
/// e as linhas mantidas são escritas tal como estão no original.
/// </summary>
public static class GtfsTicFilter
{
    private static readonly CsvConfiguration CsvConfig = new(CultureInfo.InvariantCulture)
    {
        BadDataFound = null,
        MissingFieldFound = null,
        DetectColumnCountChanges = false
    };

    public static GtfsTicResult Filter(string inputZip, string outputZip, IReadOnlyCollection<string> ticStopIds)
    {
        var ticIds = ticStopIds.ToHashSet(StringComparer.Ordinal);
        using var input = ZipFile.OpenRead(inputZip);
        var entries = input.Entries.Where(e => !string.IsNullOrEmpty(e.Name))
            .GroupBy(e => e.Name.ToLowerInvariant()).ToDictionary(g => g.Key, g => g.First());
        ZipArchiveEntry? Entry(string name) => entries.GetValueOrDefault(name);

        // Paragens do TIC presentes no feed (para o registo).
        var encontradas = new List<(string, string)>();
        if (Entry("stops.txt") is { } stopsEntry)
            ForEachRow(stopsEntry, row =>
            {
                var id = row.Get("stop_id");
                if (id is not null && ticIds.Contains(id)) encontradas.Add((id, row.Get("stop_name") ?? ""));
            });
        var emFalta = ticIds.Except(encontradas.Select(e => e.Item1)).ToList();

        // 1) viagens com pelo menos um stop_time numa paragem do TIC
        var tripIds = new HashSet<string>(StringComparer.Ordinal);
        if (Entry("stop_times.txt") is { } stopTimes)
            ForEachRow(stopTimes, row =>
            {
                if (row.Get("stop_id") is { } s && ticIds.Contains(s) && row.Get("trip_id") is { } t) tripIds.Add(t);
            });

        var routeIds = new HashSet<string>(StringComparer.Ordinal);
        var serviceIds = new HashSet<string>(StringComparer.Ordinal);
        var shapeIds = new HashSet<string>(StringComparer.Ordinal);
        var stopIds = new HashSet<string>(StringComparer.Ordinal);
        var agencyIds = new HashSet<string>(StringComparer.Ordinal);
        int viagens = 0, horarios = 0, rotas = 0, paragens = 0;

        if (File.Exists(outputZip)) File.Delete(outputZip);
        using var output = ZipFile.Open(outputZip, ZipArchiveMode.Create);

        // 2) trips
        if (Entry("trips.txt") is { } trips)
            viagens = Copy(trips, output, row => row.Get("trip_id") is { } t && tripIds.Contains(t), row =>
            {
                AddIfAny(routeIds, row.Get("route_id"));
                AddIfAny(serviceIds, row.Get("service_id"));
                AddIfAny(shapeIds, row.Get("shape_id"));
            });

        // 3) stop_times das viagens mantidas (todas as paragens da viagem, não só a do TIC)
        if (Entry("stop_times.txt") is { } stopTimes2)
            horarios = Copy(stopTimes2, output, row => row.Get("trip_id") is { } t && tripIds.Contains(t),
                row => AddIfAny(stopIds, row.Get("stop_id")));

        // 4) stops referenciados + estações-mãe
        if (Entry("stops.txt") is { } stops)
        {
            ForEachRow(stops, row =>
            {
                if (row.Get("stop_id") is { } id && stopIds.Contains(id)) AddIfAny(stopIds, row.Get("parent_station"));
            });
            paragens = Copy(stops, output, row => row.Get("stop_id") is { } id && stopIds.Contains(id));
        }

        // 5) routes e agency
        if (Entry("routes.txt") is { } routes)
            rotas = Copy(routes, output, row => row.Get("route_id") is { } r && routeIds.Contains(r),
                row => AddIfAny(agencyIds, row.Get("agency_id")));
        if (Entry("agency.txt") is { } agency)
            Copy(agency, output, row => agencyIds.Count == 0 || row.Get("agency_id") is not { Length: > 0 } a || agencyIds.Contains(a));

        // 6) calendários, shapes, frequências e transferências
        if (Entry("calendar.txt") is { } calendar)
            Copy(calendar, output, row => row.Get("service_id") is { } s && serviceIds.Contains(s));
        if (Entry("calendar_dates.txt") is { } calendarDates)
            Copy(calendarDates, output, row => row.Get("service_id") is { } s && serviceIds.Contains(s));
        if (Entry("shapes.txt") is { } shapes)
            Copy(shapes, output, row => row.Get("shape_id") is { } s && shapeIds.Contains(s));
        if (Entry("frequencies.txt") is { } frequencies)
            Copy(frequencies, output, row => row.Get("trip_id") is { } t && tripIds.Contains(t));
        if (Entry("transfers.txt") is { } transfers)
            Copy(transfers, output, row =>
                row.Get("from_stop_id") is { } f && stopIds.Contains(f) && row.Get("to_stop_id") is { } t && stopIds.Contains(t));

        // 7) restantes ficheiros (feed_info, fare_*, ...) copiados sem alterações
        string[] tratados = ["trips.txt", "stop_times.txt", "stops.txt", "routes.txt", "agency.txt", "calendar.txt",
            "calendar_dates.txt", "shapes.txt", "frequencies.txt", "transfers.txt"];
        foreach (var (name, entry) in entries.Where(e => !tratados.Contains(e.Key)))
        {
            using var src = entry.Open();
            using var dst = output.CreateEntry(entry.Name, CompressionLevel.Optimal).Open();
            src.CopyTo(dst);
        }

        return new GtfsTicResult(viagens, horarios, rotas, paragens, encontradas, emFalta);
    }

    private static void AddIfAny(HashSet<string> set, string? value)
    {
        if (!string.IsNullOrEmpty(value)) set.Add(value);
    }

    private sealed class Row(Dictionary<string, int> header, string[] fields)
    {
        public string? Get(string column) =>
            header.TryGetValue(column, out var i) && i < fields.Length ? fields[i].Trim() : null;
    }

    private static void ForEachRow(ZipArchiveEntry entry, Action<Row> action) =>
        Read(entry, (_, row, _) => action(row));

    private static int Copy(ZipArchiveEntry entry, ZipArchive output, Func<Row, bool> keep, Action<Row>? onKept = null)
    {
        var kept = 0;
        using var writer = new StreamWriter(output.CreateEntry(entry.Name, CompressionLevel.Optimal).Open(), new UTF8Encoding(false));
        Read(entry, headerRaw => writer.Write(EnsureNewLine(headerRaw)), (raw, row, _) =>
        {
            if (!keep(row)) return;
            writer.Write(EnsureNewLine(raw));
            onKept?.Invoke(row);
            kept++;
        });
        return kept;
    }

    private static void Read(ZipArchiveEntry entry, Action<string, Row, int> action) => Read(entry, null, action);

    /// <summary>Lê um ficheiro CSV do zip; dá o texto original de cada registo para poder ser copiado sem alterações.</summary>
    private static void Read(ZipArchiveEntry entry, Action<string>? onHeader, Action<string, Row, int> action)
    {
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        using var parser = new CsvParser(reader, CsvConfig);
        if (!parser.Read()) return;
        var header = parser.Record!
            .Select((name, i) => (name: name.Trim().TrimStart('﻿'), i))
            .GroupBy(x => x.name).ToDictionary(g => g.Key, g => g.First().i, StringComparer.OrdinalIgnoreCase);
        onHeader?.Invoke(parser.RawRecord.TrimStart('﻿'));
        var n = 0;
        while (parser.Read())
        {
            var record = parser.Record!;
            if (record.Length == 1 && string.IsNullOrWhiteSpace(record[0])) continue;
            action(parser.RawRecord, new Row(header, record), n++);
        }
    }

    private static string EnsureNewLine(string raw) =>
        raw.EndsWith('\n') ? raw : raw + "\r\n";
}
