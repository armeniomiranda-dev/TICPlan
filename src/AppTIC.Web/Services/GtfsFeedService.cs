using AppTIC.Web.Infrastructure;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using AppTIC.Web.Data;
using AppTIC.Web.Models.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AppTIC.Web.Services;

public class GtfsOptions
{
    public const string Section = "Gtfs";

    /// <summary>Pasta onde os .zip são guardados (relativa à pasta da aplicação ou absoluta).</summary>
    public string StoragePath { get; set; } = "App_Data/gtfs";

    public long MaxUploadBytes { get; set; } = 200L * 1024 * 1024;
}

public record GtfsValidationIssue(NivelRegisto Nivel, string Mensagem);

public class GtfsFeedService(
    ApplicationDbContext db,
    IActivityLog log,
    IOptions<GtfsOptions> options,
    IWebHostEnvironment env)
{
    private static readonly string[] RequiredFiles = ["agency.txt", "stops.txt", "routes.txt", "trips.txt", "stop_times.txt"];

    private string Root => Path.IsPathRooted(options.Value.StoragePath)
        ? options.Value.StoragePath
        : Path.Combine(env.ContentRootPath, options.Value.StoragePath);

    public string GetFullPath(GtfsFeed feed) => Path.Combine(Root, feed.CaminhoArmazenamento);

    public async Task<GtfsFeed> UploadAsync(IFormFile file, string nome, string? descricao, string ownerId, CancellationToken ct = default)
    {
        if (!file.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("O feed GTFS tem de ser um ficheiro .zip.");
        if (file.Length == 0)
            throw new InvalidDataException("O ficheiro está vazio.");
        if (file.Length > options.Value.MaxUploadBytes)
            throw new InvalidDataException($"O ficheiro excede o limite de {options.Value.MaxUploadBytes / 1024 / 1024} MB.");

        var relativeDir = ownerId;
        var storedName = $"{Guid.NewGuid():N}.zip";
        Directory.CreateDirectory(Path.Combine(Root, relativeDir));
        var fullPath = Path.Combine(Root, relativeDir, storedName);

        string sha;
        await using (var target = File.Create(fullPath))
        {
            await using var source = file.OpenReadStream();
            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[81920];
            int read;
            while ((read = await source.ReadAsync(buffer, ct)) > 0)
            {
                hasher.AppendData(buffer, 0, read);
                await target.WriteAsync(buffer.AsMemory(0, read), ct);
            }
            sha = Convert.ToHexStringLower(hasher.GetHashAndReset());
        }

        var feed = new GtfsFeed
        {
            OwnerId = ownerId,
            Nome = nome,
            Descricao = descricao,
            NomeFicheiroOriginal = Path.GetFileName(file.FileName),
            CaminhoArmazenamento = Path.Combine(relativeDir, storedName),
            TamanhoBytes = file.Length,
            Sha256 = sha,
            Estado = GtfsFeedEstado.Carregado
        };
        db.GtfsFeeds.Add(feed);
        await db.SaveChangesAsync(ct);

        var duplicate = await db.GtfsFeeds.AnyAsync(f => f.Id != feed.Id && f.OwnerId == ownerId && f.Sha256 == sha, ct);
        await log.LogAsync(CategoriaRegisto.Gtfs, "Carregamento",
            $"Feed \"{feed.Nome}\" carregado ({feed.NomeFicheiroOriginal}, {feed.TamanhoBytes:N0} bytes, SHA-256 {sha[..12]}…)."
            + (duplicate ? " Já existe um feed com o mesmo conteúdo." : ""),
            duplicate ? NivelRegisto.Aviso : NivelRegisto.Info, feed.Id, ownerId);

        await ValidateAsync(feed, ct);
        return feed;
    }

    /// <summary>Validação estrutural: ficheiros obrigatórios, cabeçalhos e contagens.</summary>
    public async Task<IReadOnlyList<GtfsValidationIssue>> ValidateAsync(GtfsFeed feed, CancellationToken ct = default)
    {
        var issues = new List<GtfsValidationIssue>();
        try
        {
            using var zip = ZipFile.OpenRead(GetFullPath(feed));
            var entries = zip.Entries
                .Where(e => !string.IsNullOrEmpty(e.Name))
                .GroupBy(e => e.Name.ToLowerInvariant())
                .ToDictionary(g => g.Key, g => g.First());

            if (zip.Entries.Any(e => e.FullName.Contains('/') && e.Name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)))
                issues.Add(new(NivelRegisto.Aviso, "Os ficheiros .txt estão dentro de uma pasta no zip; o GTFS exige que estejam na raiz."));

            foreach (var required in RequiredFiles.Where(r => !entries.ContainsKey(r)))
                issues.Add(new(NivelRegisto.Erro, $"Ficheiro obrigatório em falta: {required}."));

            if (!entries.ContainsKey("calendar.txt") && !entries.ContainsKey("calendar_dates.txt"))
                issues.Add(new(NivelRegisto.Erro, "É obrigatório existir calendar.txt e/ou calendar_dates.txt."));

            feed.NumAgencias = CountRows(entries, "agency.txt", issues);
            feed.NumParagens = CountRows(entries, "stops.txt", issues);
            feed.NumRotas = CountRows(entries, "routes.txt", issues);
            feed.NumViagens = CountRows(entries, "trips.txt", issues);
            feed.NumHorarios = CountRows(entries, "stop_times.txt", issues);

            ReadFeedInfo(feed, entries, issues);
        }
        catch (InvalidDataException)
        {
            issues.Add(new(NivelRegisto.Erro, "O ficheiro não é um zip válido."));
        }
        catch (FileNotFoundException)
        {
            issues.Add(new(NivelRegisto.Erro, "O ficheiro do feed não foi encontrado no armazenamento."));
        }

        feed.Estado = issues.Any(i => i.Nivel == NivelRegisto.Erro) ? GtfsFeedEstado.Invalido
            : issues.Any(i => i.Nivel == NivelRegisto.Aviso) ? GtfsFeedEstado.ValidoComAvisos
            : GtfsFeedEstado.Valido;
        await db.SaveChangesAsync(ct);

        foreach (var issue in issues)
            await log.LogAsync(CategoriaRegisto.Gtfs, "Validação", issue.Mensagem, issue.Nivel, feed.Id, feed.OwnerId);

        await log.LogAsync(CategoriaRegisto.Gtfs, "Validação",
            $"Validação concluída: {feed.Estado.DisplayName()}. Agências {feed.NumAgencias}, paragens {feed.NumParagens}, rotas {feed.NumRotas}, viagens {feed.NumViagens}, horários {feed.NumHorarios}.",
            feed.Estado == GtfsFeedEstado.Invalido ? NivelRegisto.Erro : NivelRegisto.Info, feed.Id, feed.OwnerId);

        return issues;
    }

    public async Task DeleteAsync(GtfsFeed feed, CancellationToken ct = default)
    {
        var path = GetFullPath(feed);
        if (File.Exists(path)) File.Delete(path);

        await log.LogAsync(CategoriaRegisto.Gtfs, "Eliminação",
            $"Feed \"{feed.Nome}\" ({feed.NomeFicheiroOriginal}) eliminado.", NivelRegisto.Aviso, null, feed.OwnerId);

        db.GtfsFeeds.Remove(feed);
        await db.SaveChangesAsync(ct);
    }

    private static int CountRows(Dictionary<string, ZipArchiveEntry> entries, string name, List<GtfsValidationIssue> issues)
    {
        if (!entries.TryGetValue(name, out var entry)) return 0;
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        var header = reader.ReadLine();
        if (string.IsNullOrWhiteSpace(header))
        {
            issues.Add(new(NivelRegisto.Erro, $"{name} não tem linha de cabeçalho."));
            return 0;
        }
        var count = 0;
        while (reader.ReadLine() is { } line)
            if (!string.IsNullOrWhiteSpace(line)) count++;
        if (count == 0)
            issues.Add(new(NivelRegisto.Aviso, $"{name} não tem registos."));
        return count;
    }

    private static void ReadFeedInfo(GtfsFeed feed, Dictionary<string, ZipArchiveEntry> entries, List<GtfsValidationIssue> issues)
    {
        if (!entries.TryGetValue("feed_info.txt", out var entry))
        {
            issues.Add(new(NivelRegisto.Aviso, "feed_info.txt não existe (recomendado)."));
            return;
        }
        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        var header = SplitCsv(reader.ReadLine());
        var values = SplitCsv(reader.ReadLine());
        string? Get(string col)
        {
            var i = header.FindIndex(h => h.Equals(col, StringComparison.OrdinalIgnoreCase));
            return i >= 0 && i < values.Count && values[i].Length > 0 ? values[i] : null;
        }
        feed.Editor = Get("feed_publisher_name");
        feed.Versao = Get("feed_version");
        feed.DataInicio = ParseGtfsDate(Get("feed_start_date"));
        feed.DataFim = ParseGtfsDate(Get("feed_end_date"));
        if (feed.DataFim is { } fim && fim < DateOnly.FromDateTime(DateTime.UtcNow))
            issues.Add(new(NivelRegisto.Aviso, $"O feed expirou em {fim:dd/MM/yyyy}."));
    }

    private static DateOnly? ParseGtfsDate(string? s) =>
        DateOnly.TryParseExact(s, "yyyyMMdd", out var d) ? d : null;

    /// <summary>Divide uma linha CSV respeitando aspas (RFC 4180).</summary>
    internal static List<string> SplitCsv(string? line)
    {
        var result = new List<string>();
        if (line is null) return result;
        line = line.TrimStart('﻿');
        var sb = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else sb.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == ',') { result.Add(sb.ToString().Trim()); sb.Clear(); }
            else sb.Append(c);
        }
        result.Add(sb.ToString().Trim());
        return result;
    }
}
