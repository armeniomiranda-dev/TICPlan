using AppTIC.Web.Data;
using AppTIC.Web.Models.Domain;
using AppTIC.Web.Models.ViewModels;
using AppTIC.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AppTIC.Web.Controllers;

/// <summary>
/// Backoffice da numeração do ID TIC: prefixos por operador/grupo (ex.: FLIXBUS 1XXX, REDE EXPRESSOS 8XXX),
/// utilização de cada intervalo, próximo número, circulações sem ID TIC e IDs fora da numeração ou repetidos.
/// </summary>
[Authorize(Roles = Roles.Admin)]
public class NumeracaoIdTicController(ApplicationDbContext db, IActivityLog log, NumeracaoService numeracao) : Controller
{
    private const int Capacidade = 999;

    public async Task<IActionResult> Index()
    {
        var operadores = await db.Operadores.Include(o => o.Grupo).Include(o => o.Membros).OrderBy(o => o.Designacao).ToListAsync();
        var circulacoes = await db.Circulacoes
            .Select(c => new { c.Id, c.ViagemId, c.IdTic, c.OperadorId, Operador = c.Operador!.Abreviatura ?? c.Operador.Designacao })
            .ToListAsync();

        // Prefixo que vale para cada operador (o do grupo, quando pertence a um grupo com prefixo).
        int? Prefixo(Operador o) => o.Grupo?.PrefixoIdTic ?? o.PrefixoIdTic;
        var prefixoDe = operadores.ToDictionary(o => o.Id, Prefixo);

        var linhas = new List<NumeracaoIdTicRow>();
        foreach (var o in operadores.Where(o => o.PrefixoIdTic is not null && (o.Grupo?.PrefixoIdTic is null)))
        {
            var p = o.PrefixoIdTic!.Value;
            var texto = p.ToString();
            var usados = circulacoes.Where(c => Encaixa(c.IdTic, texto)).Select(c => int.Parse(c.IdTic!.AsSpan(texto.Length))).Distinct().ToList();
            var ultimo = usados.Count == 0 ? (int?)null : usados.Max();
            var abrangidos = operadores.Where(x => prefixoDe[x.Id] == p).Select(x => x.Id).ToHashSet();
            var proximo = (ultimo ?? 0) >= Capacidade ? null : texto + ((ultimo ?? 0) + 1).ToString("000");
            linhas.Add(new NumeracaoIdTicRow(o.Id, o.Abreviatura ?? o.Designacao, o.EGrupo,
                o.Membros.Count == 0 ? null : string.Join(", ", o.Membros.Select(m => m.Abreviatura ?? m.Designacao).Order()),
                p, $"{texto}001–{texto}999", usados.Count, ultimo is null ? null : texto + ultimo.Value.ToString("000"), proximo,
                Capacidade - (ultimo ?? 0), circulacoes.Count(c => c.IdTic is null && abrangidos.Contains(c.OperadorId))));
        }

        // Problemas: ID TIC repetido, ou fora do intervalo do operador.
        var problemas = new List<IdTicProblemaRow>();
        foreach (var g in circulacoes.Where(c => c.IdTic is not null).GroupBy(c => c.IdTic).Where(g => g.Count() > 1))
            problemas.AddRange(g.Select(c => new IdTicProblemaRow(c.Id, c.ViagemId, c.IdTic, c.Operador, $"ID TIC repetido ({g.Count()} circulações)")));
        foreach (var c in circulacoes.Where(c => c.IdTic is not null && prefixoDe.GetValueOrDefault(c.OperadorId) is { } p && !Encaixa(c.IdTic, p.ToString())))
            problemas.Add(new IdTicProblemaRow(c.Id, c.ViagemId, c.IdTic, c.Operador, $"fora da numeração do operador ({prefixoDe[c.OperadorId]}XXX)"));

        ViewBag.Problemas = problemas.OrderBy(p => p.IdTic).ToList();
        ViewBag.SemPrefixo = operadores.Where(o => Prefixo(o) is null)
            .Select(o => new { o.Id, Nome = o.Abreviatura ?? o.Designacao, o.EGrupo, Sem = circulacoes.Count(c => c.OperadorId == o.Id && c.IdTic is null) })
            .Select(o => (o.Id, o.Nome, o.EGrupo, o.Sem)).ToList();
        return View(linhas.OrderBy(l => l.Prefixo).ToList());
    }

    private static bool Encaixa(string? idTic, string prefixo) =>
        idTic is not null && idTic.Length == prefixo.Length + NumeracaoService.DigitosIdTic && idTic.StartsWith(prefixo, StringComparison.Ordinal)
        && idTic.AsSpan(prefixo.Length).IndexOfAnyExceptInRange('0', '9') < 0;

    /// <summary>Define (ou retira) o prefixo de um operador ou grupo.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DefinirPrefixo(int operadorId, int? prefixo)
    {
        var o = await db.Operadores.FindAsync(operadorId);
        if (o is null) return NotFound();
        if (prefixo is < 1 or > 999)
            TempData["Erro"] = "O prefixo tem de estar entre 1 e 999.";
        else if (prefixo is not null && await db.Operadores.AnyAsync(x => x.Id != operadorId && x.PrefixoIdTic == prefixo))
            TempData["Erro"] = $"O prefixo {prefixo} já está atribuído a outro operador.";
        else
        {
            o.PrefixoIdTic = prefixo;
            await db.SaveChangesAsync();
            await log.LogAsync(CategoriaRegisto.Dados, "ID TIC", $"Operador {o.Designacao}: prefixo do ID TIC {(prefixo is null ? "retirado" : $"{prefixo}XXX")}.");
            TempData["Ok"] = "Prefixo guardado.";
        }
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Atribui o ID TIC seguinte a todas as circulações sem ID TIC dos operadores abrangidos por um prefixo.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AtribuirEmFalta(int operadorId)
    {
        var dono = await db.Operadores.FindAsync(operadorId);
        if (dono?.PrefixoIdTic is null) return NotFound();
        var ids = await db.Operadores.Where(o => o.Id == operadorId || o.GrupoId == operadorId).Select(o => o.Id).ToListAsync();
        var semId = await db.Circulacoes.Include(c => c.Operador).ThenInclude(o => o!.Grupo)
            .Where(c => c.IdTic == null && ids.Contains(c.OperadorId)).OrderBy(c => c.Partida ?? c.Chegada).ToListAsync();
        var n = 0;
        try
        {
            foreach (var c in semId)
            {
                // Membros com grupo sem prefixo usam o seu; só se atribui aqui o que cai neste prefixo.
                if (await numeracao.PrefixoIdTicAsync(c.Operador!) != dono.PrefixoIdTic) continue;
                c.IdTic = await numeracao.ProximoIdTicAsync(c.Operador!);
                n++;
            }
        }
        catch (InvalidOperationException ex)
        {
            TempData["Erro"] = ex.Message;
        }
        await db.SaveChangesAsync();
        await log.LogAsync(CategoriaRegisto.Dados, "ID TIC", $"{dono.Designacao}: atribuídos {n} ID TIC ({dono.PrefixoIdTic}XXX).");
        TempData["Ok"] = $"Atribuídos {n} ID TIC.";
        return RedirectToAction(nameof(Index));
    }
}
