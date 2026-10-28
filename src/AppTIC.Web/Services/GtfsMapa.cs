using System.Globalization;
using System.IO.Compression;

namespace AppTIC.Web.Services;

/// <summary>Converte as paragens (stops.txt) e os traçados (shapes.txt) de um GTFS em GeoJSON para o mapa.</summary>
public static class GtfsMapa
{
    public static object GeoJson(string zipPath, IReadOnlyCollection<string> ticStopIds)
    {
        var ticIds = ticStopIds.ToHashSet(StringComparer.Ordinal);
        using var zip = ZipFile.OpenRead(zipPath);
        var entries = zip.Entries.Where(e => !string.IsNullOrEmpty(e.Name))
            .GroupBy(e => e.Name.ToLowerInvariant()).ToDictionary(g => g.Key, g => g.First());
        ZipArchiveEntry? Entry(string name) => entries.GetValueOrDefault(name);

        var features = new List<object>();

        // Rota (e cor) de cada shape, via trips → routes.
        var routes = new Dictionary<string, (string Nome, string? Cor)>(StringComparer.Ordinal);
        if (Entry("routes.txt") is { } routesEntry)
            GtfsTicFilter.ForEachRow(routesEntry, row =>
            {
                if (row.Get("route_id") is not { } id) return;
                var cor = row.Get("route_color");
                routes[id] = (row.Get("route_short_name") is { Length: > 0 } n ? n : row.Get("route_long_name") ?? id,
                    cor is { Length: 6 } ? "#" + cor : null);
            });
        var shapeRotas = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        if (Entry("trips.txt") is { } tripsEntry)
            GtfsTicFilter.ForEachRow(tripsEntry, row =>
            {
                if (row.Get("shape_id") is not { Length: > 0 } s || row.Get("route_id") is not { Length: > 0 } r) return;
                if (!shapeRotas.TryGetValue(s, out var set)) shapeRotas[s] = set = new(StringComparer.Ordinal);
                set.Add(r);
            });

        // Traçados
        if (Entry("shapes.txt") is { } shapesEntry)
        {
            var pontos = new Dictionary<string, List<(int Seq, double Lon, double Lat)>>(StringComparer.Ordinal);
            GtfsTicFilter.ForEachRow(shapesEntry, row =>
            {
                if (row.Get("shape_id") is not { Length: > 0 } id
                    || !TryCoord(row.Get("shape_pt_lat"), row.Get("shape_pt_lon"), out var lat, out var lon)) return;
                int.TryParse(row.Get("shape_pt_sequence"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var seq);
                if (!pontos.TryGetValue(id, out var list)) pontos[id] = list = [];
                list.Add((seq, lon, lat));
            });
            foreach (var (id, list) in pontos.Where(p => p.Value.Count > 1))
            {
                var rotas = shapeRotas.GetValueOrDefault(id)?.Select(r => routes.GetValueOrDefault(r)).Where(r => r.Nome is not null).ToList() ?? [];
                features.Add(new
                {
                    type = "Feature",
                    geometry = new { type = "LineString", coordinates = list.OrderBy(p => p.Seq).Select(p => new[] { p.Lon, p.Lat }) },
                    properties = new
                    {
                        kind = "shape",
                        shape_id = id,
                        rotas = string.Join(", ", rotas.Select(r => r.Nome).Distinct()),
                        cor = rotas.Select(r => r.Cor).FirstOrDefault(c => c is not null)
                    }
                });
            }
        }

        // Paragens
        if (Entry("stops.txt") is { } stopsEntry)
            GtfsTicFilter.ForEachRow(stopsEntry, row =>
            {
                if (row.Get("stop_id") is not { Length: > 0 } id
                    || !TryCoord(row.Get("stop_lat"), row.Get("stop_lon"), out var lat, out var lon)) return;
                features.Add(new
                {
                    type = "Feature",
                    geometry = new { type = "Point", coordinates = new[] { lon, lat } },
                    properties = new { kind = "stop", stop_id = id, nome = row.Get("stop_name") ?? "", tic = ticIds.Contains(id) }
                });
            });

        return new { type = "FeatureCollection", features };
    }

    private static bool TryCoord(string? latS, string? lonS, out double lat, out double lon)
    {
        lon = 0;
        return double.TryParse(latS, NumberStyles.Float, CultureInfo.InvariantCulture, out lat)
            && double.TryParse(lonS, NumberStyles.Float, CultureInfo.InvariantCulture, out lon)
            && (lat != 0 || lon != 0) && Math.Abs(lat) <= 90 && Math.Abs(lon) <= 180;
    }
}
