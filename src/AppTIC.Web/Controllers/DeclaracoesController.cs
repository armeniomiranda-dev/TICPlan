using AppTIC.Web.Infrastructure;
using AppTIC.Web.Data;
using AppTIC.Web.Models.Domain;
using AppTIC.Web.Models.ViewModels;
using AppTIC.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AppTIC.Web.Controllers;

/// <summary>
/// Backoffice das declarações (DCP, Ano.Numero.Versao): lista, detalhe com circulações e histórico de versões.
/// Os utilizadores do operador consultam as suas; o Admin gere (nova declaração, nova versão, circulações, declarante).
/// </summary>
public class DeclaracoesController(ApplicationDbContext db, ICurrentUser currentUser, IActivityLog log, NumeracaoService numeracao) : Controller
{
    /// <summary>
    /// Declarações de um ano (por omissão o atual). <paramref name="vista"/> = "circulacoes": uma linha por declaração e circulação,
    /// para exportar para Excel. Nas declarações renovadas (ano encerrado) contam as circulações à data da renovação.
    /// </summary>
    public async Task<IActionResult> Index(int? ano = null, string? vista = null)
    {
        var anos = await db.Declaracoes.Select(d => d.Ano).Distinct().OrderByDescending(a => a).ToListAsync();
        // Por omissão, o ano em vigor (o mais recente); só esse se renova e só nesse se criam declarações.
        var anoAtual = await numeracao.AnoAtualAsync();
        ano ??= anoAtual;
        ViewBag.AnoAtual = anoAtual;
        var data = await db.Declaracoes.Where(d => d.Ano == ano).OrderByDescending(d => d.Numero)
            .Select(d => new
            {
                d.Id, d.Ano, d.Numero, d.Versao, Operador = d.Operador != null ? d.Operador.Abreviatura ?? d.Operador.Designacao : null,
                Renovacao = d.Renovacao != null ? new { d.Renovacao.Ano, d.Renovacao.Numero, d.Renovacao.Versao } : null,
                Anterior = d.Anterior != null ? new { d.Anterior.Ano, d.Anterior.Numero, d.Anterior.Versao } : null,
                d.CriadoEm, d.AtualizadoEm
            }).ToListAsync();
        var membros = await MembrosAsync(ano.Value);
        var porDcp = membros.ToLookup(m => m.DeclaracaoId);
        ViewBag.Anos = anos.Contains(ano.Value) ? anos : [.. anos.Append(ano.Value).OrderDescending()];
        ViewBag.Ano = ano;
        ViewBag.Vista = vista == "circulacoes" ? "circulacoes" : "declaracoes";
        ViewBag.SemDeclaracao = await db.Circulacoes.CountAsync(c => c.DeclaracaoId == null);
        if (currentUser.IsAdmin)
        {
            ViewBag.Operadores = await OperadoresLookup();
            ViewBag.PorRenovar = data.Count(d => d.Renovacao is null && porDcp[d.Id].Any());
            ViewBag.Renovadas = data.Count(d => d.Renovacao is not null);
        }
        var linhas = data.Select(d => new DeclaracaoRow(d.Id, Declaracao.Formatar(d.Ano, d.Numero, d.Versao), d.Ano, d.Numero, d.Versao,
            d.Operador, string.Join(", ", porDcp[d.Id].Select(m => m.Operador).Distinct().Order()), porDcp[d.Id].Count(),
            d.CriadoEm.ToLocalTime(), d.AtualizadoEm.ToLocalTime(),
            d.Renovacao is { } r ? $"Renovada → {Declaracao.Formatar(r.Ano, r.Numero, r.Versao)}" : "Em vigor",
            d.Anterior is { } a ? Declaracao.Formatar(a.Ano, a.Numero, a.Versao) : null)).ToList();
        if (ViewBag.Vista == "circulacoes")
        {
            var porId = linhas.ToDictionary(l => l.Id);
            ViewBag.Circulacoes = membros.Where(m => porId.ContainsKey(m.DeclaracaoId))
                .Select(m => { var d = porId[m.DeclaracaoId]; return m with { Codigo = d.Codigo, Declarante = d.Declarante, Estado = d.Estado }; })
                .OrderBy(m => porId[m.DeclaracaoId].Numero).ThenBy(m => m.Partida ?? m.Chegada).ToList();
        }
        return View(linhas);
    }

    /// <summary>Circulações das declarações de um ano: as atuais e, nas renovadas, as que tinham à data da renovação.</summary>
    private async Task<List<DeclaracaoAnoCirculacaoRow>> MembrosAsync(int ano, int? declaracaoId = null)
    {
        // Pares (declaração, circulação): atuais + histórico das renovadas.
        var pares = await db.Circulacoes.Where(c => c.Declaracao!.Ano == ano && (declaracaoId == null || c.DeclaracaoId == declaracaoId))
            .Select(c => new { DeclaracaoId = c.DeclaracaoId!.Value, CirculacaoId = c.Id })
            .Concat(db.DeclaracaoCirculacoes.Where(h => h.Declaracao!.Ano == ano && (declaracaoId == null || h.DeclaracaoId == declaracaoId))
                .Select(h => new { h.DeclaracaoId, h.CirculacaoId }))
            .ToListAsync();
        var ids = pares.Select(p => p.CirculacaoId).Distinct().ToList();
        var circulacoes = await db.Circulacoes.Where(c => ids.Contains(c.Id))
            .Select(c => new
            {
                c.Id, c.ViagemId, c.IdTic, Operador = c.Operador!.Abreviatura ?? c.Operador.Designacao, c.LinhaId, c.DesignacaoLinha,
                Origem = c.Origem != null ? c.Origem.Nome : null, Destino = c.Destino != null ? c.Destino.Nome : null, c.Chegada, c.Partida
            }).ToDictionaryAsync(c => c.Id);
        var lista = pares.Where(p => circulacoes.ContainsKey(p.CirculacaoId)).Select(p =>
        {
            var x = circulacoes[p.CirculacaoId];
            return new DeclaracaoAnoCirculacaoRow(p.DeclaracaoId, "", ano, null, "", x.Id, x.ViagemId, x.IdTic, x.Operador, x.LinhaId,
                x.DesignacaoLinha, x.Origem, x.Destino, x.Chegada?.ToString("HH:mm"), x.Partida?.ToString("HH:mm"), null, null);
        }).ToList();
        // Primeiro e último dia de cada circulação dentro do ano.
        var (inicio, fim) = (new DateOnly(ano, 1, 1), new DateOnly(ano, 12, 31));
        var limites = await db.CirculacaoDias.Where(d => ids.Contains(d.CirculacaoId) && d.Data >= inicio && d.Data <= fim)
            .GroupBy(d => d.CirculacaoId).Select(g => new { g.Key, Primeiro = g.Min(d => d.Data), Ultimo = g.Max(d => d.Data) })
            .ToDictionaryAsync(x => x.Key);
        return lista.Select(l => limites.TryGetValue(l.CirculacaoId, out var x)
            ? l with { Primeiro = x.Primeiro.ToDateTime(TimeOnly.MinValue), Ultimo = x.Ultimo.ToDateTime(TimeOnly.MinValue) } : l).ToList();
    }

    public async Task<IActionResult> Details(int id)
    {
        var d = await db.Declaracoes.Include(x => x.Operador).Include(x => x.Renovacao).Include(x => x.Anterior)
            .Include(x => x.Versoes.OrderByDescending(v => v.Data).ThenByDescending(v => v.Versao))
            .FirstOrDefaultAsync(x => x.Id == id);
        if (d is null) return NotFound();
        ViewBag.Circulacoes = (await MembrosAsync(d.Ano, d.Id)).OrderBy(c => c.Partida ?? c.Chegada).ToList();
        if (currentUser.IsAdmin) ViewBag.Operadores = await OperadoresLookup();
        return View(d);
    }

    /// <summary>Renova todas as declarações (com circulações) de um ano para o ano seguinte.</summary>
    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Renovar(int ano, bool manterNumeros = false)
    {
        NumeracaoService.ResultadoRenovacao r;
        try
        {
            r = await numeracao.RenovarAnoAsync(ano, manterNumeros);
        }
        catch (InvalidOperationException ex)
        {
            TempData["Erro"] = ex.Message;
            return RedirectToAction(nameof(Index), new { ano });
        }
        var msg = $"Renovadas {r.Renovadas} declaração(ões) de {ano} para {ano + 1}, com {r.Circulacoes} circulação(ões)."
            + (r.JaRenovadas > 0 ? $" {r.JaRenovadas} já estavam renovadas." : "")
            + (r.SemCirculacoes > 0 ? $" {r.SemCirculacoes} sem circulações ficaram por renovar." : "");
        await log.LogAsync(CategoriaRegisto.Dados, "Renovação de declarações", msg, r.NumerosOcupados.Count > 0 ? NivelRegisto.Aviso : NivelRegisto.Info);
        TempData["Ok"] = msg;
        if (r.NumerosOcupados.Count > 0)
            TempData["Erro"] = $"Por renovar (o número já existe em {ano + 1}): {string.Join(", ", r.NumerosOcupados)}. Renove-as sem manter os números.";
        return RedirectToAction(nameof(Index), new { ano = ano + 1 });
    }

    /// <summary>Declaração renovada (ano encerrado): só consulta.</summary>
    private async Task<bool> EncerradaAsync(int id)
    {
        if (!await db.Declaracoes.AnyAsync(d => d.AnteriorId == id)) return false;
        TempData["Erro"] = "A declaração está encerrada (foi renovada para o ano seguinte): altere a renovação.";
        return true;
    }
    private Task<List<LookupItem>> OperadoresLookup() =>
        db.Operadores.OrderBy(o => o.Designacao)
            .Select(o => new LookupItem(o.Id.ToString(), (o.EGrupo ? "[grupo] " : "") + (o.Abreviatura ?? o.Designacao))).ToListAsync();

    /// <summary>Nova declaração (número seguinte no ano), opcionalmente já com circulações (ID de viagem ou ID TIC).</summary>
    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Criar(int operadorId, string? circulacoes)
    {
        // As novas declarações ficam sempre no ano em vigor (o mais recente).
        var operador = await db.Operadores.FindAsync(operadorId);
        if (operador is null)
        {
            TempData["Erro"] = "Escolha o operador declarante.";
            return RedirectToAction(nameof(Index));
        }
        var d = await numeracao.NovaDeclaracaoAsync(operador, "Criada no backoffice.");
        await db.SaveChangesAsync();
        var (associadas, erros) = await AssociarAsync(d, circulacoes);
        await log.LogAsync(CategoriaRegisto.Dados, "Declaração", $"Nova declaração {d.Codigo}" + (associadas.Count > 0 ? $" com {string.Join(", ", associadas)}." : "."));
        TempData["Ok"] = $"Criada a declaração {d.Codigo}" + (associadas.Count > 0 ? $" com {associadas.Count} circulação(ões)." : ".");
        if (erros.Count > 0) TempData["Erro"] = string.Join(" ", erros);
        return RedirectToAction(nameof(Details), new { id = d.Id });
    }

    /// <summary>
    /// Associa à declaração as circulações indicadas (ID de viagem ou ID TIC, separados por vírgula, espaço ou linha).
    /// Uma circulação que estava noutra declaração passa para esta.
    /// </summary>
    private async Task<(List<string> Associadas, List<string> Erros)> AssociarAsync(Declaracao d, string? codigos)
    {
        var lista = (codigos ?? "").Split([',', ';', ' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct().ToList();
        var associadas = new List<string>();
        var erros = new List<string>();
        if (lista.Count == 0) return (associadas, erros);
        var candidatas = await db.Circulacoes.Where(c => lista.Contains(c.ViagemId) || (c.IdTic != null && lista.Contains(c.IdTic))).ToListAsync();
        foreach (var codigo in lista)
        {
            var porTic = candidatas.Where(c => c.IdTic == codigo).ToList();
            var encontradas = porTic.Count > 0 ? porTic : candidatas.Where(c => c.ViagemId == codigo).ToList();
            if (encontradas.Count != 1)
            {
                erros.Add(encontradas.Count == 0 ? $"\"{codigo}\" não encontrada." : $"\"{codigo}\" é ambíguo ({encontradas.Count} circulações): use o ID TIC.");
                continue;
            }
            var c = encontradas[0];
            if (c.DeclaracaoId == d.Id) continue;
            var anterior = c.IdDcp;
            c.DeclaracaoId = d.Id;
            c.IdDcp = d.Codigo;
            associadas.Add(c.ViagemId);
            await log.LogAsync(CategoriaRegisto.Dados, "Declaração",
                $"Circulação {c.ViagemId} associada à declaração {d.Codigo}" + (anterior is null ? "." : $" (antes {anterior})."), ownerId: c.OwnerId);
        }
        await db.SaveChangesAsync();
        return (associadas, erros);
    }

    /// <summary>Nova versão manual (ex.: alteração feita fora da aplicação).</summary>
    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> NovaVersao(int id, string? motivo)
    {
        if (await EncerradaAsync(id)) return RedirectToAction(nameof(Details), new { id });
        var d = await db.Declaracoes.FindAsync(id);
        if (d is null) return NotFound();
        d.Versao++;
        d.AtualizadoEm = DateTime.UtcNow;
        d.Versoes.Add(new DeclaracaoVersao
        {
            Versao = d.Versao, Motivo = string.IsNullOrWhiteSpace(motivo) ? "Nova versão (manual)." : motivo.Trim(), Utilizador = currentUser.UserName
        });
        await db.SaveChangesAsync();
        await numeracao.SincronizarCodigoAsync(d);
        await log.LogAsync(CategoriaRegisto.Dados, "Declaração", $"Declaração {d.Codigo}: nova versão.");
        TempData["Ok"] = $"Declaração passou a {d.Codigo}.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> AlterarOperador(int id, int? operadorId)
    {
        var d = await db.Declaracoes.FindAsync(id);
        if (d is null) return NotFound();
        d.OperadorId = operadorId;
        d.AtualizadoEm = DateTime.UtcNow;
        await db.SaveChangesAsync();
        TempData["Ok"] = "Operador declarante alterado.";
        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>Associa circulações (ID de viagem ou ID TIC; várias separadas por vírgula) à declaração.</summary>
    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> AdicionarCirculacao(int id, string? codigo)
    {
        if (await EncerradaAsync(id)) return RedirectToAction(nameof(Details), new { id });
        var d = await db.Declaracoes.FindAsync(id);
        if (d is null) return NotFound();
        var (associadas, erros) = await AssociarAsync(d, codigo);
        if (associadas.Count > 0) TempData["Ok"] = $"Associada(s) à declaração: {string.Join(", ", associadas)}.";
        if (erros.Count > 0) TempData["Erro"] = string.Join(" ", erros);
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> RetirarCirculacao(int id, int circulacaoId)
    {
        if (await EncerradaAsync(id)) return RedirectToAction(nameof(Details), new { id });
        var c = await db.Circulacoes.FirstOrDefaultAsync(x => x.Id == circulacaoId && x.DeclaracaoId == id);
        if (c is null) return NotFound();
        c.DeclaracaoId = null;
        c.IdDcp = null;
        await db.SaveChangesAsync();
        await log.LogAsync(CategoriaRegisto.Dados, "Declaração", $"Circulação {c.ViagemId} retirada da declaração #{id}.", NivelRegisto.Aviso, ownerId: c.OwnerId);
        TempData["Ok"] = $"Circulação {c.ViagemId} retirada da declaração.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Delete(int id)
    {
        if (await EncerradaAsync(id)) return RedirectToAction(nameof(Details), new { id });
        var d = await db.Declaracoes.FindAsync(id);
        if (d is null) return NotFound();
        if (await db.Circulacoes.AnyAsync(c => c.DeclaracaoId == id))
        {
            TempData["Erro"] = "Não é possível eliminar: a declaração tem circulações.";
            return RedirectToAction(nameof(Details), new { id });
        }
        db.Declaracoes.Remove(d);
        await db.SaveChangesAsync();
        await log.LogAsync(CategoriaRegisto.Dados, "Declaração", $"Declaração {d.Codigo} eliminada.", NivelRegisto.Aviso);
        TempData["Ok"] = $"Declaração {d.Codigo} eliminada.";
        return RedirectToAction(nameof(Index));
    }
}
