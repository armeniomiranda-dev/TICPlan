using AppTIC.Web.Data;
using AppTIC.Web.Infrastructure;
using AppTIC.Web.Models.Domain;
using AppTIC.Web.Models.ViewModels;
using AppTIC.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AppTIC.Web.Controllers;

/// <summary>
/// Importações em bloco (lotes) de paragens, circulações e calendário: do GTFS (feitas na página do feed) ou da folha
/// Excel de planeamento. Cada lote tem um Id, pelo qual se elimina, atualiza ou volta a importar.
/// </summary>
[Authorize(Roles = Roles.Admin)]
public class ImportacoesController(ApplicationDbContext db, ImportacaoService importacoes, ExcelImportService excel,
    GtfsImportService gtfsImport, ICurrentUser currentUser, IActivityLog log) : Controller
{
    public async Task<IActionResult> Index()
    {
        var data = await db.Importacoes.OrderByDescending(i => i.Id)
            .Select(i => new
            {
                i, Feed = i.GtfsFeed != null ? i.GtfsFeed.Nome : null, Dono = i.Owner!.Email, Circulacoes = i.Circulacoes.Count
            }).ToListAsync();
        return View(data.Select(x => new ImportacaoRow(x.i.Id, x.i.Origem.DisplayName(), x.i.Nome,
            x.i.Origem == ImportacaoOrigem.Excel ? x.i.NomeFicheiro : x.Feed ?? "(feed eliminado)", x.i.Estado.ToString(),
            x.i.DataInicio.ToDateTime(), x.i.DataFim.ToDateTime(), x.Circulacoes, x.i.Aplicacoes,
            x.i.AplicadaEm?.ToLocalTime(), x.i.CriadoEm.ToLocalTime(), x.Dono)).ToList());
    }

    // ---- Excel: carregar (lote novo) ou atualizar o ficheiro de um lote ----

    public async Task<IActionResult> Excel(int? id)
    {
        if (id is null) return View(new ExcelUploadViewModel());
        var existente = await db.Importacoes.FirstOrDefaultAsync(i => i.Id == id && i.Origem == ImportacaoOrigem.Excel);
        if (existente is null) return NotFound();
        return View(new ExcelUploadViewModel { Id = existente.Id, Nome = existente.Nome });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Excel(ExcelUploadViewModel model, CancellationToken ct)
    {
        var existente = model.Id is null ? null
            : await db.Importacoes.FirstOrDefaultAsync(i => i.Id == model.Id && i.Origem == ImportacaoOrigem.Excel, ct);
        if (model.Id is not null && existente is null) return NotFound();
        if (!ModelState.IsValid) return View(model);
        try
        {
            var importacao = await importacoes.GuardarExcelAsync(model.Ficheiro!, model.Nome.Trim(), currentUser.UserId!, existente, ct);
            TempData["Ok"] = $"Ficheiro carregado na importação #{importacao.Id}. Reveja as alterações e a comparação com o GTFS e depois aplique.";
            return RedirectToAction(nameof(Details), new { id = importacao.Id });
        }
        catch (InvalidDataException ex)
        {
            await log.LogAsync(CategoriaRegisto.Importacao, "Carregamento Excel",
                $"Ficheiro rejeitado ({model.Ficheiro?.FileName}): {ex.Message}", NivelRegisto.Erro);
            ModelState.AddModelError(nameof(model.Ficheiro), ex.Message);
            return View(model);
        }
    }

    /// <summary>
    /// Detalhe do lote. No Excel mostra o que a aplicação vai fazer e a comparação com o GTFS filtrado TIC de um feed
    /// (<paramref name="feedId"/>; por omissão, o feed mais recente de um operador que conste do ficheiro).
    /// </summary>
    public async Task<IActionResult> Details(int id, int? feedId, CancellationToken ct)
    {
        var importacao = await db.Importacoes.Include(i => i.GtfsFeed).Include(i => i.Owner).FirstOrDefaultAsync(i => i.Id == id, ct);
        if (importacao is null) return NotFound();

        var vm = new ImportacaoDetalhe
        {
            Importacao = importacao,
            NumCirculacoes = await db.Circulacoes.CountAsync(c => c.ImportacaoId == id, ct),
            Registos = await db.Registos.Where(r => r.Mensagem.StartsWith($"Importação #{id} ")).OrderByDescending(r => r.Id)
                .Select(r => new RegistoRow(r.Id, r.Data.ToLocalTime(), r.Nivel.ToString(), r.Categoria, r.Acao, r.Mensagem, r.GtfsFeedId, r.UserName, null))
                .ToListAsync(ct)
        };
        if (importacao.Origem != ImportacaoOrigem.Excel) return View(vm);

        try
        {
            var dados = await importacoes.LerExcelAsync(importacao, ct);
            vm.Preview = await excel.PreverAsync(importacao, dados, ct);
            await CompararAsync(vm, dados, feedId, ct);
        }
        catch (InvalidDataException ex)
        {
            vm.Erro = ex.Message;
        }
        return View(vm);
    }

    private async Task CompararAsync(ImportacaoDetalhe vm, ExcelDados dados, int? feedId, CancellationToken ct)
    {
        var feeds = await db.GtfsFeeds
            .Where(f => f.OperadorId != null && f.CaminhoFiltrado != null && f.Estado != GtfsFeedEstado.Invalido)
            .OrderByDescending(f => f.CriadoEm)
            .Select(f => new { f.Id, f.Nome, f.CriadoEm, f.Operador!.Nif, Operador = f.Operador.Abreviatura ?? f.Operador.Designacao })
            .ToListAsync(ct);
        vm.Feeds = feeds.Select(f => new LookupItem(f.Id.ToString(), $"{f.Operador} – {f.Nome} ({f.CriadoEm.ToLocalTime():dd/MM/yyyy})")).ToList();

        var nifs = dados.Circulacoes.Select(c => c.Nif).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var escolhido = feedId ?? feeds.FirstOrDefault(f => f.Nif != null && nifs.Contains(f.Nif))?.Id;
        if (escolhido is null || feeds.All(f => f.Id != escolhido)) return;

        var feed = await db.GtfsFeeds.Include(f => f.Operador).FirstAsync(f => f.Id == escolhido, ct);
        vm.FeedId = feed.Id;
        try
        {
            var gtfs = await gtfsImport.LerAsync(feed, ct);
            vm.Comparacao = ExcelGtfsComparacao.Comparar(dados, gtfs,
                nif => dados.Operadores.TryGetValue(nif, out var o) ? o.Abreviatura ?? o.Designacao : null);
            // Viagens do GTFS que correspondem ao Excel e já são circulações (importadas do GTFS com o trip_id):
            // aplicar o Excel cria-as outra vez com o viagem_id.
            var tripIds = vm.Comparacao.TripIdsCorrespondidos;
            var viagensExcel = dados.Circulacoes.Select(c => c.ViagemId).ToList();
            vm.Duplicadas = (await db.Circulacoes
                    .Where(c => c.OwnerId == vm.Importacao.OwnerId && tripIds.Contains(c.ViagemId) && !viagensExcel.Contains(c.ViagemId))
                    .GroupBy(c => c.ImportacaoId).Select(g => new { Lote = g.Key, Total = g.Count() }).ToListAsync(ct))
                .Select(g => (g.Lote, g.Total)).ToList();
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or CsvHelper.CsvHelperException)
        {
            vm.ErroComparacao = $"Não foi possível ler o GTFS filtrado TIC do feed \"{feed.Nome}\": {ex.Message}";
        }
    }

    /// <summary>Aplica (ou volta a aplicar) o ficheiro Excel guardado: cria/atualiza operadores, paragens, circulações e calendário.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Aplicar(int id, bool removerAusentes, CancellationToken ct)
    {
        var importacao = await db.Importacoes.FirstOrDefaultAsync(i => i.Id == id && i.Origem == ImportacaoOrigem.Excel, ct);
        if (importacao is null) return NotFound();
        try
        {
            var dados = await importacoes.LerExcelAsync(importacao, ct);
            var r = await excel.AplicarAsync(importacao, dados, removerAusentes, ct);
            TempData[r.Ignoradas > 0 ? "Erro" : "Ok"] =
                $"Importação #{id} aplicada: {r.OperadoresNovos} operadores novos; {r.ParagensNovas} paragens novas, {r.ParagensAtualizadas} atualizadas; "
                + $"{r.CirculacoesNovas} circulações novas, {r.CirculacoesAtualizadas} atualizadas"
                + (r.Removidas > 0 ? $", {r.Removidas} removidas" : "") + (r.Ignoradas > 0 ? $", {r.Ignoradas} ignoradas por conflito" : "")
                + $"; {r.Dias:N0} dias de calendário.";
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or DbUpdateException)
        {
            await log.LogAsync(CategoriaRegisto.Importacao, "Importação Excel",
                $"Importação #{id} não aplicada (nada foi alterado): {ex.GetBaseException().Message}", NivelRegisto.Erro, ownerId: importacao.OwnerId);
            TempData["Erro"] = $"Erro na importação (nada foi alterado): {ex.GetBaseException().Message}";
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    public async Task<IActionResult> Download(int id)
    {
        var importacao = await db.Importacoes.FindAsync(id);
        var path = importacao is null ? null : importacoes.CaminhoCompleto(importacao);
        if (path is null || !System.IO.File.Exists(path)) return NotFound();
        return PhysicalFile(path, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", importacao!.NomeFicheiro ?? $"importacao_{id}.xlsx");
    }

    /// <summary>Elimina o lote e as circulações que lhe pertencem.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var importacao = await db.Importacoes.FindAsync([id], ct);
        if (importacao is null) return NotFound();
        var n = await importacoes.EliminarAsync(importacao, ct);
        TempData["Ok"] = $"Importação #{id} eliminada, com {n} circulação(ões).";
        return RedirectToAction(nameof(Index));
    }
}
