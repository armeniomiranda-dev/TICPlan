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
/// Gestão dos grupos de operadores (ex.: Rede Nacional de Expressos). Um grupo é um operador marcado como grupo;
/// os operadores do grupo apontam para ele. Os utilizadores associados ao grupo veem todos os operadores do grupo,
/// e nos operadores do grupo vale o prefixo do ID TIC do grupo.
/// </summary>
[Authorize(Roles = Roles.Admin)]
public class GruposController(ApplicationDbContext db, ICurrentUser currentUser, IActivityLog log) : Controller
{
    public async Task<IActionResult> Index()
    {
        var grupos = await db.Operadores.Where(o => o.EGrupo).OrderBy(o => o.Designacao)
            .Select(o => new
            {
                o.Id, o.Designacao, o.Abreviatura, o.Nif, o.PrefixoIdTic, o.Cor,
                Membros = o.Membros.OrderBy(m => m.Designacao).Select(m => m.Abreviatura ?? m.Designacao).ToList(),
                Circulacoes = o.Circulacoes.Count + o.Membros.Sum(m => m.Circulacoes.Count)
            }).ToListAsync();
        var utilizadores = await db.Users.Where(u => u.OperadorId != null).GroupBy(u => u.OperadorId!.Value)
            .Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N);
        ViewBag.Promover = await db.Operadores.Where(o => !o.EGrupo && o.GrupoId == null).OrderBy(o => o.Designacao)
            .Select(o => new LookupItem(o.Id.ToString(), o.Abreviatura ?? o.Designacao)).ToListAsync();
        return View(grupos.Select(g => new GrupoRow(g.Id, g.Designacao, g.Abreviatura, g.Nif, g.PrefixoIdTic is { } p ? $"{p}XXX" : null,
            g.Membros.Count, string.Join(", ", g.Membros), g.Circulacoes, utilizadores.GetValueOrDefault(g.Id), CoresOperador.De(g.Id, g.Cor))).ToList());
    }

    public async Task<IActionResult> Create()
    {
        await LoadOperadores(0);
        return View("Form", new GrupoViewModel());
    }

    public async Task<IActionResult> Edit(int id)
    {
        var g = await db.Operadores.Include(o => o.Membros).FirstOrDefaultAsync(o => o.Id == id && o.EGrupo);
        if (g is null) return NotFound();
        await LoadOperadores(id);
        return View("Form", new GrupoViewModel
        {
            Id = g.Id, Designacao = g.Designacao, Abreviatura = g.Abreviatura, Nif = g.Nif, PrefixoIdTic = g.PrefixoIdTic,
            Comentarios = g.Comentarios, Cor = g.Cor, Membros = g.Membros.Select(m => m.Id).ToList()
        });
    }

    /// <summary>Promove um operador existente a grupo (ex.: a RNE, que também tem circulações próprias).</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Promover(int operadorId)
    {
        var o = await db.Operadores.FindAsync(operadorId);
        if (o is null) return NotFound();
        if (o.GrupoId is not null)
        {
            TempData["Erro"] = "O operador pertence a um grupo: retire-o do grupo antes.";
            return RedirectToAction(nameof(Index));
        }
        o.EGrupo = true;
        await db.SaveChangesAsync();
        await log.LogAsync(CategoriaRegisto.Dados, "Grupo", $"Operador {o.Designacao} passou a grupo.", ownerId: o.OwnerId);
        TempData["Ok"] = $"{o.Abreviatura ?? o.Designacao} passou a grupo: escolha os operadores do grupo.";
        return RedirectToAction(nameof(Edit), new { id = o.Id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(GrupoViewModel model, bool semCor = false)
    {
        var g = model.Id == 0 ? new Operador { EGrupo = true } : await db.Operadores.Include(o => o.Membros).FirstOrDefaultAsync(o => o.Id == model.Id && o.EGrupo);
        if (g is null) return NotFound();
        var ownerId = model.Id == 0 ? currentUser.UserId! : g.OwnerId;
        model.Nif = string.IsNullOrWhiteSpace(model.Nif) ? null : model.Nif.Trim();
        if (model.Nif is not null && await db.Operadores.AnyAsync(o => o.Id != model.Id && o.OwnerId == ownerId && o.Nif == model.Nif))
            ModelState.AddModelError(nameof(model.Nif), "Já existe um operador com este NIF.");
        if (model.PrefixoIdTic is { } p && await db.Operadores.AnyAsync(o => o.Id != model.Id && o.PrefixoIdTic == p))
            ModelState.AddModelError(nameof(model.PrefixoIdTic), "Este prefixo já está atribuído a outro operador.");
        var membros = model.Membros.Where(m => m != model.Id).Distinct().ToList();
        if (await db.Operadores.AnyAsync(o => membros.Contains(o.Id) && o.EGrupo))
            ModelState.AddModelError(nameof(model.Membros), "Um grupo não pode pertencer a outro grupo.");
        if (!ModelState.IsValid)
        {
            await LoadOperadores(model.Id);
            return View("Form", model);
        }

        g.Designacao = model.Designacao.Trim();
        g.Abreviatura = string.IsNullOrWhiteSpace(model.Abreviatura) ? null : model.Abreviatura.Trim();
        g.Nif = model.Nif;
        g.PrefixoIdTic = model.PrefixoIdTic;
        g.Comentarios = model.Comentarios;
        g.Cor = semCor ? null : CoresOperador.Normalizar(model.Cor);
        if (model.Id == 0) db.Operadores.Add(g);
        await db.SaveChangesAsync();

        // Membros: entram os escolhidos (saindo do grupo anterior), saem os desmarcados.
        var atuais = await db.Operadores.Where(o => o.GrupoId == g.Id || membros.Contains(o.Id)).ToListAsync();
        foreach (var o in atuais) o.GrupoId = membros.Contains(o.Id) ? g.Id : null;
        await db.SaveChangesAsync();

        await log.LogAsync(CategoriaRegisto.Dados, model.Id == 0 ? "Criar grupo" : "Editar grupo",
            $"Grupo {g.Designacao}: {membros.Count} operador(es).", ownerId: g.OwnerId);
        TempData["Ok"] = "Grupo guardado.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Deixa de ser grupo: os operadores saem do grupo. Um grupo sem circulações nem utilizadores é eliminado.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var g = await db.Operadores.Include(o => o.Membros).FirstOrDefaultAsync(o => o.Id == id && o.EGrupo);
        if (g is null) return NotFound();
        foreach (var m in g.Membros) m.GrupoId = null;
        g.EGrupo = false;
        var usado = g.Nif is not null || await db.Circulacoes.AnyAsync(c => c.OperadorId == id) || await db.Users.AnyAsync(u => u.OperadorId == id)
            || await db.Declaracoes.AnyAsync(d => d.OperadorId == id) || await db.GtfsFeeds.AnyAsync(f => f.OperadorId == id);
        if (!usado) db.Operadores.Remove(g);
        await db.SaveChangesAsync();
        await log.LogAsync(CategoriaRegisto.Dados, "Eliminar grupo",
            usado ? $"{g.Designacao} deixou de ser grupo." : $"Grupo {g.Designacao} eliminado.", NivelRegisto.Aviso, ownerId: g.OwnerId);
        TempData["Ok"] = usado ? $"{g.Abreviatura ?? g.Designacao} deixou de ser grupo (o operador mantém-se)." : "Grupo eliminado.";
        return RedirectToAction(nameof(Index));
    }

    private async Task LoadOperadores(int grupoId)
    {
        ViewBag.CorEfetiva = CoresOperador.PorOmissao(grupoId);
        // Operadores que podem entrar no grupo (não são grupos), com o grupo atual de cada um.
        ViewBag.Operadores = await db.Operadores.Where(o => !o.EGrupo).OrderBy(o => o.Designacao)
            .Select(o => new GrupoMembroOpcao(o.Id, o.Abreviatura ?? o.Designacao, o.Nif,
                o.GrupoId != null && o.GrupoId != grupoId ? o.Grupo!.Abreviatura ?? o.Grupo.Designacao : null, o.Circulacoes.Count))
            .ToListAsync();
    }
}
