using AppTIC.Web.Infrastructure;
using AppTIC.Web.Data;
using AppTIC.Web.Models.Domain;
using AppTIC.Web.Models.ViewModels;
using AppTIC.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AppTIC.Web.Controllers;

public class OperadoresController(ApplicationDbContext db, ICurrentUser currentUser, IActivityLog log) : Controller
{
    public async Task<IActionResult> Index()
    {
        var rows = await db.Operadores
            .OrderBy(o => o.Designacao)
            .Select(o => new OperadorRow(o.Id, o.Nif, o.Designacao, o.Abreviatura, o.Alvara,
                o.AlvaraValidade.HasValue ? o.AlvaraValidade.Value.ToDateTime(TimeOnly.MinValue) : null,
                o.Ambito.HasValue ? o.Ambito.Value.DisplayName() : null,
                o.Circulacoes.Count, currentUser.IsAdmin ? o.Owner!.Email : null))
            .ToListAsync();
        return View(rows);
    }

    public IActionResult Create() => View("Form", new Operador());

    public async Task<IActionResult> Edit(int id)
    {
        var operador = await db.Operadores.FindAsync(id);
        return operador is null ? NotFound() : View("Form", operador);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(Operador model)
    {
        ModelState.Remove(nameof(Operador.OwnerId));
        Operador? entity = model.Id == 0 ? new Operador() : await db.Operadores.FindAsync(model.Id);
        if (entity is null) return NotFound();

        var ownerId = model.Id == 0 ? currentUser.UserId! : entity.OwnerId;
        if (await db.Operadores.AnyAsync(o => o.Id != model.Id && o.OwnerId == ownerId && o.Nif == model.Nif))
            ModelState.AddModelError(nameof(Operador.Nif), "Já existe um operador com este NIF.");
        if (!ModelState.IsValid) return View("Form", model);

        entity.Nif = model.Nif.Trim();
        entity.Designacao = model.Designacao.Trim();
        entity.Alvara = model.Alvara;
        entity.AlvaraValidade = model.AlvaraValidade;
        entity.Ambito = model.Ambito;
        entity.Abreviatura = model.Abreviatura;
        entity.Comentarios = model.Comentarios;
        if (model.Id == 0) db.Operadores.Add(entity);
        await db.SaveChangesAsync();

        await log.LogAsync(CategoriaRegisto.Dados, model.Id == 0 ? "Criar operador" : "Editar operador",
            $"Operador {entity.Nif} – {entity.Designacao}.", ownerId: entity.OwnerId);
        TempData["Ok"] = "Operador guardado.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var operador = await db.Operadores.FindAsync(id);
        if (operador is null) return NotFound();
        if (await db.Circulacoes.AnyAsync(c => c.OperadorId == id))
        {
            TempData["Erro"] = "Não é possível eliminar: o operador tem circulações.";
            return RedirectToAction(nameof(Index));
        }
        db.Operadores.Remove(operador);
        await db.SaveChangesAsync();
        await log.LogAsync(CategoriaRegisto.Dados, "Eliminar operador", $"Operador {operador.Nif} – {operador.Designacao}.",
            NivelRegisto.Aviso, ownerId: operador.OwnerId);
        TempData["Ok"] = "Operador eliminado.";
        return RedirectToAction(nameof(Index));
    }
}
