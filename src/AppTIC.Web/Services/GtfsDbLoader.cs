using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AppTIC.Web.Services;

public record GtfsDbResultado(IReadOnlyDictionary<string, long> Linhas, int ShapeGeoms, IReadOnlyList<string> Ignorados);

/// <summary>
/// Carrega o conteúdo de um feed GTFS (.zip) no schema "gtfs" da base de dados: cada ficheiro .txt vai para a tabela
/// com o mesmo nome (via COPY), locations.geojson para gtfs.locations. Tudo numa transação: o conteúdo anterior do feed
/// é substituído e, se alguma linha violar tipos, chaves ou FKs, nada fica gravado.
/// </summary>
public class GtfsDbLoader(AppTIC.Web.Data.ApplicationDbContext db)
{
    private static readonly CsvConfiguration CsvConfig = new(CultureInfo.InvariantCulture)
    {
        BadDataFound = null,
        MissingFieldFound = null,
        DetectColumnCountChanges = false
    };

    /// <summary>Colunas que no GTFS podem vir vazias mas na tabela são NOT NULL DEFAULT '' (ex. agency_id com uma só agência).</summary>
    private static readonly Dictionary<string, string> VazioComoTexto = new() { ["agency"] = "agency_id" };

    public async Task<GtfsDbResultado> CarregarAsync(int feedId, string zipPath, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(db.Database.GetConnectionString());
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        // Colunas (graváveis) de cada tabela do schema gtfs.
        var tabelas = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        await using (var cmd = new NpgsqlCommand("""
            SELECT table_name, column_name FROM information_schema.columns
            WHERE table_schema = 'gtfs' AND is_generated = 'NEVER' AND is_identity = 'NO' AND column_name <> 'feed_id'
            """, conn, tx))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct))
            {
                if (!tabelas.TryGetValue(r.GetString(0), out var cols)) tabelas[r.GetString(0)] = cols = new(StringComparer.OrdinalIgnoreCase);
                cols.Add(r.GetString(1));
            }

        foreach (var tabela in tabelas.Keys)
            await Executar(conn, tx, $"DELETE FROM gtfs.{tabela} WHERE feed_id = @f", feedId, ct);

        var linhas = new Dictionary<string, long>();
        var ignorados = new List<string>();
        using var zip = ZipFile.OpenRead(zipPath);
        foreach (var entry in zip.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).GroupBy(e => e.Name.ToLowerInvariant()).Select(g => g.First()))
        {
            var nome = entry.Name.ToLowerInvariant();
            if (nome == "locations.geojson")
            {
                linhas["locations"] = await CarregarLocations(conn, tx, feedId, entry, ct);
                continue;
            }
            var tabela = Path.GetFileNameWithoutExtension(nome);
            if (!nome.EndsWith(".txt") || tabela == "shape_geoms" || !tabelas.TryGetValue(tabela, out var colunas))
            {
                ignorados.Add(entry.Name);
                continue;
            }
            try
            {
                linhas[tabela] = await Copiar(conn, feedId, tabela, colunas, entry, ignorados, ct);
            }
            catch (PostgresException ex)
            {
                throw new InvalidDataException($"{entry.Name}: {ex.MessageText}{(ex.Where is { } w ? $" ({w})" : "")}", ex);
            }
        }

        int shapeGeoms;
        await using (var cmd = new NpgsqlCommand("SELECT gtfs.gerar_shape_geoms(@f)", conn, tx))
        {
            cmd.Parameters.AddWithValue("f", feedId);
            cmd.CommandTimeout = 600;
            shapeGeoms = (int)(await cmd.ExecuteScalarAsync(ct))!;
        }

        try
        {
            // As FKs são diferidas: é aqui que são verificadas.
            await Executar(conn, tx, "SET CONSTRAINTS ALL IMMEDIATE", null, ct);
            await tx.CommitAsync(ct);
        }
        catch (PostgresException ex)
        {
            throw new InvalidDataException($"Referência inválida: {ex.MessageText}{(ex.Detail is { } d ? $" {d}" : "")}", ex);
        }
        return new GtfsDbResultado(linhas, shapeGeoms, ignorados);
    }

    /// <summary>COPY de um ficheiro CSV GTFS para gtfs.{tabela}, só com as colunas que a tabela conhece.</summary>
    private static async Task<long> Copiar(NpgsqlConnection conn, int feedId, string tabela, HashSet<string> colunasTabela,
        ZipArchiveEntry entry, List<string> ignorados, CancellationToken ct)
    {
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        using var parser = new CsvParser(reader, CsvConfig);
        if (!parser.Read()) return 0;
        var header = parser.Record!.Select(h => h.Trim().TrimStart('﻿')).ToArray();
        var indices = header.Select((h, i) => (h, i)).Where(x => colunasTabela.Contains(x.h))
            .GroupBy(x => x.h, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();
        foreach (var extra in header.Where(h => h.Length > 0 && !colunasTabela.Contains(h)))
            ignorados.Add($"{entry.Name}:{extra}");
        if (indices.Count == 0) return 0;
        var vazioComoTexto = VazioComoTexto.GetValueOrDefault(tabela);

        var cols = string.Join(", ", indices.Select(x => Quote(x.h.ToLowerInvariant())));
        long n = 0;
        await using (var writer = await conn.BeginTextImportAsync($"COPY gtfs.{tabela} (feed_id, {cols}) FROM STDIN (FORMAT csv)", ct))
        {
            var sb = new StringBuilder();
            while (parser.Read())
            {
                var record = parser.Record!;
                if (record.Length == 1 && string.IsNullOrWhiteSpace(record[0])) continue;
                sb.Clear().Append(feedId.ToString(CultureInfo.InvariantCulture));
                foreach (var (h, i) in indices)
                {
                    sb.Append(',');
                    var v = i < record.Length ? record[i].Trim() : "";
                    if (v.Length == 0 && !string.Equals(h, vazioComoTexto, StringComparison.OrdinalIgnoreCase)) continue; // NULL
                    sb.Append('"').Append(v.Replace("\"", "\"\"")).Append('"');
                }
                sb.Append('\n');
                await writer.WriteAsync(sb.ToString());
                n++;
            }
        }
        return n;
    }

    private static async Task<long> CarregarLocations(NpgsqlConnection conn, NpgsqlTransaction tx, int feedId, ZipArchiveEntry entry, CancellationToken ct)
    {
        using var doc = await JsonDocument.ParseAsync(entry.Open(), cancellationToken: ct);
        if (!doc.RootElement.TryGetProperty("features", out var features)) return 0;
        long n = 0;
        foreach (var f in features.EnumerateArray())
        {
            var props = f.TryGetProperty("properties", out var p) && p.ValueKind == JsonValueKind.Object ? p : default;
            string? Prop(string nome) => props.ValueKind == JsonValueKind.Object && props.TryGetProperty(nome, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            await using var cmd = new NpgsqlCommand("""
                INSERT INTO gtfs.locations (feed_id, location_id, stop_name, stop_desc, properties, geom)
                VALUES (@f, @id, @nome, @desc, @props::jsonb, ST_SetSRID(ST_GeomFromGeoJSON(@geom), 4326))
                """, conn, tx);
            cmd.Parameters.AddWithValue("f", feedId);
            cmd.Parameters.AddWithValue("id", f.GetProperty("id").ToString());
            cmd.Parameters.AddWithValue("nome", (object?)Prop("stop_name") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("desc", (object?)Prop("stop_desc") ?? DBNull.Value);
            cmd.Parameters.AddWithValue("props", props.ValueKind == JsonValueKind.Object ? props.GetRawText() : DBNull.Value);
            cmd.Parameters.AddWithValue("geom", f.GetProperty("geometry").GetRawText());
            await cmd.ExecuteNonQueryAsync(ct);
            n++;
        }
        return n;
    }

    private static async Task Executar(NpgsqlConnection conn, NpgsqlTransaction tx, string sql, int? feedId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(sql, conn, tx) { CommandTimeout = 600 };
        if (feedId is not null) cmd.Parameters.AddWithValue("f", feedId.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static string Quote(string identificador) => "\"" + identificador.Replace("\"", "\"\"") + "\"";
}
