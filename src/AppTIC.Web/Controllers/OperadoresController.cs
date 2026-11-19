using AppTIC.Web.Infrastructure;
using AppTIC.Web.Data;
using AppTIC.Web.Models.Domain;
using AppTIC.Web.Models.ViewModels;
using AppTIC.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AppTIC.Web.Controllers;

/// <summary>Gestão dos operadores (só Admin; os utilizadores estão associados a um operador).</summary>
[Authorize(Roles = Roles.Admin)]
public class OperadoresController(ApplicationDbContext db, ICurrentUser currentUser, IActivityLog log) : Controller
{
    public async Task<IActionResult> Index()
    {
        var rows = await db.Operadores
            .OrderBy(o => o.Designacao)
            .Select(o => new
            {
                o.Id, o.Nif, o.Designacao, o.Abreviatura, o.Alvara, o.AlvaraValidade, o.Ambito, NumCirculacoes = o.Circulacoes.Count,
                Dono = currentUser.IsAdmin ? o.Owner!.Email : null, o.EGrupo,
                Grupo = o.Grupo != null ? o.Grupo.Abreviatura ?? o.Grupo.Designacao : null, NumMembros = o.Membros.Count,
                o.PrefixoIdTic, PrefixoGrupo = o.Grupo != null ? o.Grupo.PrefixoIdTic : null,
                o.Cor, CorGrupo = o.Grupo != null ? o.Grupo.Cor : null
            })
            .ToListAsync();
        return View(rows.Select(o => new OperadorRow(o.Id, o.Nif ?? "", o.Designacao, o.Abreviatura, o.Alvara, o.AlvaraValidade.ToDateTime(),
            o.Ambito?.DisplayName(), o.NumCirculacoes, o.Dono, o.EGrupo ? $"Grupo ({o.NumMembros})" : o.Grupo,
            (o.PrefixoGrupo ?? o.PrefixoIdTic) is { } p ? $"{p}XXX" : null, CoresOperador.De(o.Id, o.Cor, o.CorGrupo))).ToList());
    }

    public async Task<IActionResult> Create()
    {
        await LoadGrupos(0);
        return View("Form", new Operador());
    }

    public async Task<IActionResult> Edit(int id)
    {
        var operador = await db.Operadores.FindAsync(id);
        if (operador is null) return NotFound();
        await LoadGrupos(id);
        return View("Form", operador);
    }

    private async Task LoadGrupos(int id)
    {
        ViewBag.Grupos = await db.Operadores.Where(o => o.EGrupo && o.Id != id).OrderBy(o => o.Designacao)
            .Select(o => new LookupItem(o.Id.ToString(), o.Abreviatura ?? o.Designacao)).ToListAsync();
        ViewBag.Membros = id == 0 ? new List<string>() : await db.Operadores.Where(o => o.GrupoId == id).OrderBy(o => o.Designacao)
            .Select(o => o.Abreviatura ?? o.Designacao).ToListAsync();
        // Cor que se vê quando o operador não tem cor própria (a do grupo ou a da paleta).
        var corGrupo = id == 0 ? null : await db.Operadores.Where(o => o.Id == id).Select(o => o.Grupo != null ? o.Grupo.Cor : null).FirstOrDefaultAsync();
        ViewBag.CorEfetiva = CoresOperador.De(id, null, corGrupo);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(Operador model, bool semCor = false)
    {
        ModelState.Remove(nameof(Operador.OwnerId));
        Operador? entity = model.Id == 0 ? new Operador() : await db.Operadores.FindAsync(model.Id);
        if (entity is null) return NotFound();

        var ownerId = model.Id == 0 ? currentUser.UserId! : entity.OwnerId;
        model.Nif = string.IsNullOrWhiteSpace(model.Nif) ? null : model.Nif.Trim();
        // Ser grupo gere-se em Grupos; aqui mantém-se.
        model.EGrupo = entity.EGrupo;
        if (model.Nif is null && !model.EGrupo)
            ModelState.AddModelError(nameof(Operador.Nif), "Indique o NIF (só um grupo pode não ter NIF).");
        if (model.Nif is not null && await db.Operadores.AnyAsync(o => o.Id != model.Id && o.OwnerId == ownerId && o.Nif == model.Nif))
            ModelState.AddModelError(nameof(Operador.Nif), "Já existe um operador com este NIF.");
        if (model.EGrupo) model.GrupoId = null;
        if (model.GrupoId is { } g && (g == model.Id || !await db.Operadores.AnyAsync(o => o.Id == g && o.EGrupo)))
            ModelState.AddModelError(nameof(Operador.GrupoId), "Escolha um grupo válido.");
        if (model.PrefixoIdTic is { } p && await db.Operadores.AnyAsync(o => o.Id != model.Id && o.PrefixoIdTic == p))
            ModelState.AddModelError(nameof(Operador.PrefixoIdTic), "Este prefixo já está atribuído a outro operador.");
        if (!ModelState.IsValid)
        {
            await LoadGrupos(model.Id);
            return View("Form", model);
        }

        entity.Nif = model.Nif;
        entity.GrupoId = model.GrupoId;
        entity.PrefixoIdTic = model.PrefixoIdTic;
        entity.Cor = semCor ? null : CoresOperador.Normalizar(model.Cor);
        entity.Designacao = model.Designacao.Trim();
        entity.Alvara = model.Alvara;
        entity.AlvaraValidade = model.AlvaraValidade;
        entity.Ambito = model.Ambito;
        entity.Abreviatura = model.Abreviatura;
        entity.Comentarios = model.Comentarios;
        entity.GtfsParagensTic = string.Join(", ", Operador.SplitStopIds(model.GtfsParagensTic)) is { Length: > 0 } ids ? ids : null;
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
        if (await db.Operadores.AnyAsync(o => o.GrupoId == id))
        {
            TempData["Erro"] = "Não é possível eliminar: o grupo tem operadores.";
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
