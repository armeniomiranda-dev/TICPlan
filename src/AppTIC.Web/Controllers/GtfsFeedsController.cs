using AppTIC.Web.Infrastructure;
using AppTIC.Web.Data;
using AppTIC.Web.Models.Domain;
using AppTIC.Web.Models.ViewModels;
using AppTIC.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AppTIC.Web.Controllers;

public class GtfsFeedsController(ApplicationDbContext db, GtfsFeedService gtfs, GtfsImportService importacao, ICurrentUser currentUser, IActivityLog log) : Controller
{
    public async Task<IActionResult> Index()
    {
        var isAdmin = currentUser.IsAdmin;
        var data = await db.GtfsFeeds.OrderByDescending(f => f.CriadoEm)
            .Select(f => new { f, Operador = f.Operador != null ? f.Operador.Abreviatura ?? f.Operador.Designacao : null, Dono = isAdmin ? f.Owner!.Email : null })
            .ToListAsync();
        var rows = data.Select(x => new GtfsFeedRow(x.f.Id, x.f.Nome, x.Operador, x.f.NomeFicheiroOriginal,
            Math.Round(x.f.TamanhoBytes / 1024d / 1024d, 2), x.f.Estado.ToString(),
            x.f.DataInicio.ToDateTime(), x.f.DataFim.ToDateTime(), x.f.NumRotas, x.f.NumViagens, x.f.NumViagensTic,
            x.f.CriadoEm.ToLocalTime(), x.Dono)).ToList();

        // Mapa geral: por operador, o feed mais recente com GTFS filtrado TIC.
        ViewBag.MapaFeeds = (await db.GtfsFeeds
                .Where(f => f.OperadorId != null && f.CaminhoFiltrado != null && f.Estado != GtfsFeedEstado.Invalido)
                .Select(f => new { f.Id, f.Nome, f.OperadorId, f.CriadoEm, Operador = f.Operador!.Abreviatura ?? f.Operador.Designacao })
                .ToListAsync())
            .GroupBy(f => f.OperadorId).Select(g => g.MaxBy(f => f.CriadoEm)!)
            .OrderBy(f => f.Operador).Select(f => new { id = f.Id, nome = f.Nome, operador = f.Operador })
            .ToList<object>();
        return View(rows);
    }

    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Upload(int? operadorId = null)
    {
        await LoadOperadores();
        return View(new GtfsUploadViewModel { OperadorId = operadorId });
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = Roles.Admin), RequestFormLimits(MultipartBodyLengthLimit = long.MaxValue), DisableRequestSizeLimit]
    public async Task<IActionResult> Upload(GtfsUploadViewModel model, CancellationToken ct)
    {
        if (model.OperadorId is not null && !await db.Operadores.AnyAsync(o => o.Id == model.OperadorId && o.OwnerId == currentUser.UserId, ct))
            ModelState.AddModelError(nameof(model.OperadorId), "Operador inválido.");
        if (!ModelState.IsValid)
        {
            await LoadOperadores();
            return View(model);
        }
        try
        {
            var feed = await gtfs.UploadAsync(model.Ficheiro!, model.Nome.Trim(), model.Descricao, currentUser.UserId!, model.OperadorId!.Value, ct);
            TempData[feed.Estado == GtfsFeedEstado.Invalido ? "Erro" : "Ok"] =
                $"Feed carregado. Estado: {feed.Estado.DisplayName()}. Consulte o registo para detalhes.";
            return RedirectToAction(nameof(Details), new { id = feed.Id });
        }
        catch (InvalidDataException ex)
        {
            await log.LogAsync(CategoriaRegisto.Gtfs, "Carregamento", $"Carregamento rejeitado ({model.Ficheiro?.FileName}): {ex.Message}", NivelRegisto.Erro);
            ModelState.AddModelError(nameof(model.Ficheiro), ex.Message);
            await LoadOperadores();
            return View(model);
        }
    }

    public async Task<IActionResult> Details(int id)
    {
        var feed = await db.GtfsFeeds.Include(f => f.Owner).Include(f => f.Operador).FirstOrDefaultAsync(f => f.Id == id);
        if (feed is null) return NotFound();
        ViewBag.Registos = await db.Registos.Where(r => r.GtfsFeedId == id).OrderByDescending(r => r.Id)
            .Select(r => new RegistoRow(r.Id, r.Data.ToLocalTime(), r.Nivel.ToString(), r.Categoria, r.Acao, r.Mensagem, r.GtfsFeedId, r.UserName, null))
            .ToListAsync();
        return View(feed);
    }

    public async Task<IActionResult> Download(int id)
    {
        var feed = await db.GtfsFeeds.FindAsync(id);
        if (feed is null) return NotFound();
        var path = gtfs.GetFullPath(feed);
        if (!System.IO.File.Exists(path)) return NotFound();
        await log.LogAsync(CategoriaRegisto.Gtfs, "Descarga", $"Feed \"{feed.Nome}\" descarregado.", gtfsFeedId: feed.Id, ownerId: feed.OwnerId);
        return PhysicalFile(path, "application/zip", feed.NomeFicheiroOriginal);
    }

    /// <summary>GTFS filtrado só com as viagens que passam no TIC.</summary>
    public async Task<IActionResult> DownloadTic(int id)
    {
        var feed = await db.GtfsFeeds.FindAsync(id);
        if (feed is null) return NotFound();
        var path = gtfs.GetFullPathFiltrado(feed);
        if (path is null || !System.IO.File.Exists(path)) return NotFound();
        await log.LogAsync(CategoriaRegisto.Gtfs, "Descarga", $"GTFS filtrado TIC do feed \"{feed.Nome}\" descarregado.", gtfsFeedId: feed.Id, ownerId: feed.OwnerId);
        return PhysicalFile(path, "application/zip", Path.GetFileNameWithoutExtension(feed.NomeFicheiroOriginal) + "_TIC.zip");
    }

    /// <summary>Paragens e traçados do feed (original ou filtrado TIC) em GeoJSON, para o mapa.</summary>
    public async Task<IActionResult> Mapa(int id, bool tic = false, CancellationToken ct = default)
    {
        var feed = await db.GtfsFeeds.Include(f => f.Operador).FirstOrDefaultAsync(f => f.Id == id, ct);
        if (feed is null) return NotFound();
        var path = tic ? gtfs.GetFullPathFiltrado(feed) : gtfs.GetFullPath(feed);
        if (path is null || !System.IO.File.Exists(path)) return NotFound();
        var paragensTic = gtfs.ParagensTic(feed.Operador);
        try
        {
            return Json(await Task.Run(() => GtfsMapa.GeoJson(path, paragensTic), ct));
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or CsvHelper.CsvHelperException)
        {
            return Problem($"Não foi possível ler o feed: {ex.Message}");
        }
    }

    // ---- Importação de paragens, circulações e calendário ----

    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Importar(int id, DateOnly? inicio, DateOnly? fim, CancellationToken ct)
    {
        var feed = await db.GtfsFeeds.Include(f => f.Operador).FirstOrDefaultAsync(f => f.Id == id, ct);
        if (feed is null) return NotFound();
        if (feed.CaminhoFiltrado is null || feed.OperadorId is null)
        {
            TempData["Erro"] = "O feed tem de ter operador e GTFS filtrado TIC para importar.";
            return RedirectToAction(nameof(Details), new { id });
        }
        var dados = await importacao.LerAsync(feed, ct);
        var intervalo = dados.Intervalo();
        var (i, f) = (inicio ?? feed.DataInicio ?? intervalo?.Inicio ?? DateOnly.FromDateTime(DateTime.Today),
                      fim ?? feed.DataFim ?? intervalo?.Fim ?? DateOnly.FromDateTime(DateTime.Today));
        if (f < i) f = i;
        if (f.DayNumber - i.DayNumber > MaxDiasImportacao) f = i.AddDays(MaxDiasImportacao);
        ViewBag.Feed = feed;
        ViewBag.Intervalo = intervalo;
        return View(await importacao.PreverAsync(feed, dados, i, f, ct));
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Importar(int id, DateOnly inicio, DateOnly fim, string[] rotas, CancellationToken ct)
    {
        var feed = await db.GtfsFeeds.Include(f => f.Operador).FirstOrDefaultAsync(f => f.Id == id, ct);
        if (feed is null) return NotFound();
        if (fim < inicio || fim.DayNumber - inicio.DayNumber > MaxDiasImportacao || rotas.Length == 0)
        {
            TempData["Erro"] = $"Escolha pelo menos uma rota e um intervalo válido (máximo {MaxDiasImportacao + 1} dias).";
            return RedirectToAction(nameof(Importar), new { id, inicio = inicio.ToString("yyyy-MM-dd"), fim = fim.ToString("yyyy-MM-dd") });
        }
        try
        {
            var dados = await importacao.LerAsync(feed, ct);
            var r = await importacao.ImportarAsync(feed, dados, rotas.ToHashSet(StringComparer.Ordinal), inicio, fim, ct);
            TempData[r.Ignoradas > 0 ? "Erro" : "Ok"] =
                $"Importação concluída: {r.ParagensNovas} paragens novas, {r.ParagensAtualizadas} atualizadas; {r.CirculacoesNovas} circulações novas, "
                + $"{r.CirculacoesAtualizadas} atualizadas" + (r.Ignoradas > 0 ? $", {r.Ignoradas} ignoradas por conflito" : "") + $"; {r.Dias} dias de calendário.";
            return RedirectToAction("Index", "Circulacoes", new { operadorId = feed.OperadorId });
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or CsvHelper.CsvHelperException or DbUpdateException)
        {
            await log.LogAsync(CategoriaRegisto.Gtfs, "Importação", $"Erro na importação: {ex.GetBaseException().Message}", NivelRegisto.Erro, feed.Id, feed.OwnerId);
            TempData["Erro"] = $"Erro na importação: {ex.GetBaseException().Message}";
            return RedirectToAction(nameof(Details), new { id });
        }
    }

    private const int MaxDiasImportacao = 730;

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Revalidar(int id, CancellationToken ct)
    {
        var feed = await db.GtfsFeeds.FindAsync([id], ct);
        if (feed is null) return NotFound();
        await gtfs.ValidateAsync(feed, ct);
        await gtfs.FiltrarTicAsync(feed, ct);
        await gtfs.CarregarBdAsync(feed, ct);
        TempData["Ok"] = $"Validação concluída: {feed.Estado.DisplayName()}.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var feed = await db.GtfsFeeds.FindAsync([id], ct);
        if (feed is null) return NotFound();
        await gtfs.DeleteAsync(feed, ct);
        TempData["Ok"] = "Feed eliminado.";
        return RedirectToAction(nameof(Index));
    }

    private async Task LoadOperadores() =>
        ViewBag.Operadores = await db.Operadores.Where(o => o.OwnerId == currentUser.UserId).OrderBy(o => o.Designacao)
            .Select(o => new LookupItem(o.Id.ToString(), o.Nif + " – " + (o.Abreviatura ?? o.Designacao))).ToListAsync();
}
