using AppTIC.Web.Data;
using AppTIC.Web.Models.Domain;
using AppTIC.Web.Models.ViewModels;
using AppTIC.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AppTIC.Web.Controllers;

public class ParagensController(ApplicationDbContext db, ICurrentUser currentUser, IActivityLog log) : Controller
{
    public async Task<IActionResult> Index()
    {
        var rows = await db.Paragens
            .OrderBy(p => p.Codigo)
            .Select(p => new ParagemRow(p.Id, p.Codigo, p.Nome, p.Latitude, p.Longitude, p.Comentarios,
                currentUser.IsAdmin ? p.Owner!.Email : null))
            .ToListAsync();
        return View(rows);
    }

    [Authorize(Roles = Roles.Admin)]
    public IActionResult Create() => View("Form", new Paragem());

    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Edit(int id)
    {
        var paragem = await db.Paragens.FindAsync(id);
        return paragem is null ? NotFound() : View("Form", paragem);
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Save(Paragem model)
    {
        ModelState.Remove(nameof(Paragem.OwnerId));
        Paragem? entity = model.Id == 0 ? new Paragem() : await db.Paragens.FindAsync(model.Id);
        if (entity is null) return NotFound();

        var ownerId = model.Id == 0 ? currentUser.UserId! : entity.OwnerId;
        if (await db.Paragens.AnyAsync(p => p.Id != model.Id && p.OwnerId == ownerId && p.Codigo == model.Codigo))
            ModelState.AddModelError(nameof(Paragem.Codigo), "Já existe uma paragem com este código.");
        if (!ModelState.IsValid) return View("Form", model);

        entity.Codigo = model.Codigo.Trim();
        entity.Nome = model.Nome.Trim();
        entity.Latitude = model.Latitude;
        entity.Longitude = model.Longitude;
        entity.Comentarios = model.Comentarios;
        if (model.Id == 0) db.Paragens.Add(entity);
        await db.SaveChangesAsync();

        await log.LogAsync(CategoriaRegisto.Dados, model.Id == 0 ? "Criar paragem" : "Editar paragem",
            $"Paragem {entity.Codigo} – {entity.Nome}.", ownerId: entity.OwnerId);
        TempData["Ok"] = "Paragem guardada.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Delete(int id)
    {
        var paragem = await db.Paragens.FindAsync(id);
        if (paragem is null) return NotFound();
        if (await db.Percursos.AnyAsync(p => p.ParagemId == id))
        {
            TempData["Erro"] = "Não é possível eliminar: a paragem faz parte de percursos.";
            return RedirectToAction(nameof(Index));
        }
        db.Paragens.Remove(paragem);
        await db.SaveChangesAsync();
        await log.LogAsync(CategoriaRegisto.Dados, "Eliminar paragem", $"Paragem {paragem.Codigo} – {paragem.Nome}.",
            NivelRegisto.Aviso, ownerId: paragem.OwnerId);
        TempData["Ok"] = "Paragem eliminada.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Lista para DropDownList (paragens do dono indicado).</summary>
    [HttpGet]
    public async Task<IActionResult> Lookup(string? ownerId = null)
    {
        var owner = currentUser.IsAdmin && ownerId is not null ? ownerId : currentUser.UserId;
        var items = await db.Paragens.Where(p => p.OwnerId == owner).OrderBy(p => p.Codigo)
            .Select(p => new { Value = p.Id, Text = p.Codigo + " – " + p.Nome }).ToListAsync();
        return Json(items);
    }
}
