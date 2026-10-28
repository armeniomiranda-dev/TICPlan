using AppTIC.Web.Infrastructure;
using AppTIC.Web.Data;
using AppTIC.Web.Models.Domain;
using AppTIC.Web.Models.ViewModels;
using AppTIC.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AppTIC.Web.Controllers;

public class GtfsFeedsController(ApplicationDbContext db, GtfsFeedService gtfs, ICurrentUser currentUser, IActivityLog log) : Controller
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
        return View(rows);
    }

    public async Task<IActionResult> Upload(int? operadorId = null)
    {
        await LoadOperadores();
        return View(new GtfsUploadViewModel { OperadorId = operadorId });
    }

    [HttpPost, ValidateAntiForgeryToken, RequestFormLimits(MultipartBodyLengthLimit = long.MaxValue), DisableRequestSizeLimit]
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

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Revalidar(int id, CancellationToken ct)
    {
        var feed = await db.GtfsFeeds.FindAsync([id], ct);
        if (feed is null) return NotFound();
        await gtfs.ValidateAsync(feed, ct);
        await gtfs.FiltrarTicAsync(feed, ct);
        TempData["Ok"] = $"Validação concluída: {feed.Estado.DisplayName()}.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
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
