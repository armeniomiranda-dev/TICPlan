using System.Security.Cryptography;
using AppTIC.Web.Data;
using AppTIC.Web.Models.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AppTIC.Web.Services;

/// <summary>Lotes de importação (GTFS ou Excel): ficheiro Excel guardado, registo do lote GTFS e eliminação do lote.</summary>
public class ImportacaoService(ApplicationDbContext db, IOptions<GtfsOptions> options, IWebHostEnvironment env, IActivityLog log)
{
    /// <summary>Limite do ficheiro Excel (a folha de planeamento de um ano tem menos de 1 MB).</summary>
    public const long MaxExcelBytes = 30L * 1024 * 1024;

    private string Root => Path.IsPathRooted(options.Value.StoragePath)
        ? options.Value.StoragePath
        : Path.Combine(env.ContentRootPath, options.Value.StoragePath);

    public string? CaminhoCompleto(Importacao importacao) =>
        importacao.CaminhoArmazenamento is null ? null : Path.Combine(Root, importacao.CaminhoArmazenamento);

    /// <summary>Lê o ficheiro Excel guardado da importação.</summary>
    public async Task<ExcelDados> LerExcelAsync(Importacao importacao, CancellationToken ct = default)
    {
        var path = CaminhoCompleto(importacao);
        if (path is null || !File.Exists(path))
            throw new InvalidDataException("O ficheiro Excel desta importação já não está no armazenamento. Carregue-o de novo em \"Atualizar ficheiro\".");
        return await Task.Run(() => ExcelPlaneamento.Ler(path), ct);
    }

    /// <summary>
    /// Guarda o ficheiro Excel numa importação nova ou, com <paramref name="existente"/>, substitui o ficheiro dessa
    /// importação (mesmo Id). Fica "por aplicar": as circulações só mudam quando a importação é aplicada.
    /// </summary>
    public async Task<Importacao> GuardarExcelAsync(IFormFile file, string nome, string ownerId, Importacao? existente, CancellationToken ct = default)
    {
        if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("O ficheiro tem de ser um Excel .xlsx.");
        if (file.Length == 0)
            throw new InvalidDataException("O ficheiro está vazio.");
        if (file.Length > MaxExcelBytes)
            throw new InvalidDataException($"O ficheiro excede o limite de {MaxExcelBytes / 1024 / 1024} MB.");

        var relativo = Path.Combine("excel", existente?.OwnerId ?? ownerId, $"{Guid.NewGuid():N}.xlsx");
        var destino = Path.Combine(Root, relativo);
        Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
        string sha;
        await using (var target = File.Create(destino))
        {
            await file.CopyToAsync(target, ct);
            target.Position = 0;
            sha = Convert.ToHexStringLower(await SHA256.HashDataAsync(target, ct));
        }
        try
        {
            // Rejeita já o que não for a folha de planeamento.
            await Task.Run(() => ExcelPlaneamento.Ler(destino), ct);
        }
        catch
        {
            File.Delete(destino);
            throw;
        }

        var importacao = existente ?? new Importacao { OwnerId = ownerId, Origem = ImportacaoOrigem.Excel };
        var anterior = existente is null ? null : CaminhoCompleto(existente);
        importacao.Nome = nome;
        importacao.NomeFicheiro = Path.GetFileName(file.FileName);
        importacao.CaminhoArmazenamento = relativo;
        importacao.TamanhoBytes = file.Length;
        importacao.Sha256 = sha;
        importacao.Estado = ImportacaoEstado.Pendente;
        if (existente is null) db.Importacoes.Add(importacao);
        await db.SaveChangesAsync(ct);
        if (anterior is not null && File.Exists(anterior)) File.Delete(anterior);

        await log.LogAsync(CategoriaRegisto.Importacao, existente is null ? "Carregamento Excel" : "Atualização Excel",
            $"Importação #{importacao.Id} \"{importacao.Nome}\": ficheiro {importacao.NomeFicheiro} ({importacao.TamanhoBytes:N0} bytes, SHA-256 {sha[..12]}…) "
            + (existente is null ? "carregado." : "substituiu o anterior.") + " Por aplicar.",
            ownerId: importacao.OwnerId);
        return importacao;
    }

    /// <summary>Elimina o lote: as circulações que lhe pertencem (com calendário, percurso e horas no TIC) e o ficheiro guardado.</summary>
    public async Task<int> EliminarAsync(Importacao importacao, CancellationToken ct = default)
    {
        var path = CaminhoCompleto(importacao);
        int circulacoes;
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            circulacoes = await db.Circulacoes.Where(c => c.ImportacaoId == importacao.Id).ExecuteDeleteAsync(ct);
            db.Importacoes.Remove(importacao);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        if (path is not null && File.Exists(path)) File.Delete(path);

        await log.LogAsync(CategoriaRegisto.Importacao, "Eliminação",
            $"Importação #{importacao.Id} \"{importacao.Nome}\" ({importacao.Origem}) eliminada com {circulacoes} circulação(ões). "
            + "As paragens e os operadores mantêm-se.",
            NivelRegisto.Aviso, ownerId: importacao.OwnerId);
        return circulacoes;
    }
}
