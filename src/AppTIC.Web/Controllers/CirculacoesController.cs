using AppTIC.Web.Data;
using AppTIC.Web.Infrastructure;
using AppTIC.Web.Models.Domain;
using AppTIC.Web.Models.Terminal;
using AppTIC.Web.Models.ViewModels;
using AppTIC.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AppTIC.Web.Controllers;

public class CirculacoesController(ApplicationDbContext db, ICurrentUser currentUser, IActivityLog log, GtfsFeedService gtfs) : Controller
{
    public async Task<IActionResult> Index(int? operadorId = null)
    {
        var query = db.Circulacoes.AsQueryable();
        if (operadorId is not null) query = query.Where(c => c.OperadorId == operadorId);

        var isAdmin = currentUser.IsAdmin;
        var data = await query
            .OrderBy(c => c.Partida ?? c.Chegada)
            .Select(c => new
            {
                c.Id, c.ViagemId, Operador = c.Operador!.Abreviatura ?? c.Operador.Designacao, c.LinhaId, c.DesignacaoLinha,
                c.Ambito, Origem = c.Origem!.Nome, Destino = c.Destino!.Nome, c.Chegada, c.Partida,
                NumDias = c.Dias.Count, Primeiro = c.Dias.Min(d => (DateOnly?)d.Data), Ultimo = c.Dias.Max(d => (DateOnly?)d.Data),
                c.AutorizacaoValidade, Dono = isAdmin ? c.Owner!.Email : null
            })
            .ToListAsync();
        var rows = data.Select(c => new CirculacaoRow(c.Id, c.ViagemId, c.Operador, c.LinhaId, c.DesignacaoLinha,
            c.Ambito?.DisplayName(), c.Origem, c.Destino, c.Chegada?.ToString("HH:mm"), c.Partida?.ToString("HH:mm"),
            c.NumDias, c.Primeiro.ToDateTime(), c.Ultimo.ToDateTime(), c.AutorizacaoValidade.ToDateTime(), c.Dono)).ToList();
        return View(rows);
    }

    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Create()
    {
        var model = new Circulacao();
        await LoadLookups(currentUser.UserId!);
        return View("Form", model);
    }

    [Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Edit(int id)
    {
        var c = await db.Circulacoes.FindAsync(id);
        if (c is null) return NotFound();
        await LoadLookups(c.OwnerId, c.Id);
        return View("Form", c);
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Save(Circulacao model)
    {
        ModelState.Remove(nameof(Circulacao.OwnerId));
        var entity = model.Id == 0 ? new Circulacao() : await db.Circulacoes.FindAsync(model.Id);
        if (entity is null) return NotFound();
        var ownerId = model.Id == 0 ? currentUser.UserId! : entity.OwnerId;

        // As referências têm de pertencer ao mesmo login que a circulação.
        if (!await db.Operadores.AnyAsync(o => o.Id == model.OperadorId && o.OwnerId == ownerId))
            ModelState.AddModelError(nameof(Circulacao.OperadorId), "Escolha um operador.");
        foreach (var (field, paragemId) in new[] { (nameof(Circulacao.OrigemId), model.OrigemId), (nameof(Circulacao.DestinoId), model.DestinoId) })
            if (paragemId is not null && !await db.Paragens.AnyAsync(p => p.Id == paragemId && p.OwnerId == ownerId))
                ModelState.AddModelError(field, "Paragem inválida.");
        if (await db.Circulacoes.AnyAsync(c => c.Id != model.Id && c.OwnerId == ownerId && c.ViagemId == model.ViagemId))
            ModelState.AddModelError(nameof(Circulacao.ViagemId), "Já existe uma circulação com este ID de viagem.");
        if (model.Id == 0 && model.Chegada is null && model.Partida is null)
            ModelState.AddModelError(nameof(Circulacao.Partida), "Indique a chegada e/ou a partida do TIC.");

        if (!ModelState.IsValid)
        {
            await LoadLookups(ownerId, model.Id);
            return View("Form", model);
        }

        entity.OperadorId = model.OperadorId;
        entity.LinhaId = model.LinhaId;
        entity.DesignacaoLinha = model.DesignacaoLinha;
        entity.Ambito = model.Ambito;
        entity.Categoria = model.Categoria;
        entity.Autorizacao = model.Autorizacao;
        entity.AutorizacaoValidade = model.AutorizacaoValidade;
        entity.DeclaracaoCapacidade = model.DeclaracaoCapacidade;
        entity.IdDcp = model.IdDcp;
        entity.ViagemId = model.ViagemId.Trim();
        entity.IdTic = model.IdTic;
        entity.OrigemId = model.OrigemId;
        entity.DestinoId = model.DestinoId;
        // Com histórico, as horas no TIC mudam-se no detalhe (por período); sem histórico, aqui.
        if (model.Id == 0 || !await db.CirculacaoHorariosTic.AnyAsync(h => h.CirculacaoId == model.Id))
        {
            entity.Chegada = model.Chegada;
            entity.Partida = model.Partida;
        }
        entity.TempoEmbarqueMinutos = model.TempoEmbarqueMinutos;
        entity.Comentarios = model.Comentarios;
        if (model.Id == 0) db.Circulacoes.Add(entity);
        await db.SaveChangesAsync();

        await log.LogAsync(CategoriaRegisto.Dados, model.Id == 0 ? "Criar circulação" : "Editar circulação",
            $"Circulação {entity.ViagemId}.", ownerId: entity.OwnerId);
        TempData["Ok"] = "Circulação guardada.";
        return model.Id == 0 ? RedirectToAction(nameof(Details), new { id = entity.Id }) : RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Delete(int id)
    {
        var c = await db.Circulacoes.FindAsync(id);
        if (c is null) return NotFound();
        db.Circulacoes.Remove(c);
        await db.SaveChangesAsync();
        await log.LogAsync(CategoriaRegisto.Dados, "Eliminar circulação", $"Circulação {c.ViagemId}.", NivelRegisto.Aviso, ownerId: c.OwnerId);
        TempData["Ok"] = "Circulação eliminada.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Detalhe com calendário (dias em que se realiza) e percurso (sequência de paragens).</summary>
    public async Task<IActionResult> Details(int id)
    {
        var c = await db.Circulacoes
            .Include(x => x.Operador).Include(x => x.Origem).Include(x => x.Destino)
            .Include(x => x.Percurso.OrderBy(p => p.Sequencia)).ThenInclude(p => p.Paragem)
            .Include(x => x.HorariosTic.OrderByDescending(h => h.DataInicio))
            .Include(x => x.GtfsFeed)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (c is null) return NotFound();
        var tic = gtfs.ParagensTic(c.Operador).ToHashSet(StringComparer.Ordinal);
        ViewBag.Paragens = await ParagensLookup(c.OwnerId);
        ViewBag.ParagensTic = await db.Paragens.Where(p => p.OwnerId == c.OwnerId && tic.Contains(p.Codigo))
            .OrderBy(p => p.Codigo).Select(p => new LookupItem(p.Id.ToString(), p.Codigo + " – " + p.Nome)).ToListAsync();
        // Só se mostra o percurso a partir da paragem do TIC (inclusive).
        ViewBag.InicioPercurso = c.Percurso.FindIndex(p => tic.Contains(p.Paragem!.Codigo));
        ViewBag.PrimeiroDia = await db.CirculacaoDias.Where(d => d.CirculacaoId == id).MinAsync(d => (DateOnly?)d.Data);
        ViewBag.UltimoDia = await db.CirculacaoDias.Where(d => d.CirculacaoId == id).MaxAsync(d => (DateOnly?)d.Data);
        ViewBag.Pedidos = await db.PedidosAlteracao.Where(p => p.CirculacaoId == id).OrderByDescending(p => p.CriadoEm)
            .Include(p => p.PedidoPor).ToListAsync();
        return View(c);
    }

    // ---- Horas no TIC (histórico por período) ----

    public record HorarioTicRequest(DateOnly Inicio, DateOnly Fim, TimeOnly? Chegada, TimeOnly? Partida);

    /// <summary>Define as horas no TIC para um período (substitui o que estava definido nesses dias).</summary>
    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> DefinirHorarioTic(int id, [FromBody] HorarioTicRequest req)
    {
        var c = await db.Circulacoes.Include(x => x.HorariosTic).FirstOrDefaultAsync(x => x.Id == id);
        if (c is null) return NotFound();
        if (req.Fim < req.Inicio || req.Fim.DayNumber - req.Inicio.DayNumber > 3660)
            return BadRequest("Período inválido (a data de fim tem de ser igual ou posterior à de início; máximo 10 anos).");
        if (req.Chegada is null && req.Partida is null)
            return BadRequest("Indique a chegada e/ou a partida do TIC.");

        HorariosTic.Definir(c, req.Inicio, req.Fim, Minuto(req.Chegada), Minuto(req.Partida), OrigemHorario.Manual, DateOnly.FromDateTime(DateTime.Today));
        await db.SaveChangesAsync();
        await log.LogAsync(CategoriaRegisto.Dados, "Horário TIC",
            $"Circulação {c.ViagemId}: de {req.Inicio:dd/MM/yyyy} a {req.Fim:dd/MM/yyyy} chegada {req.Chegada:HH\\:mm} / partida {req.Partida:HH\\:mm}.",
            ownerId: c.OwnerId);
        return Json(new { periodos = c.HorariosTic.Count });
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> RemoverHorarioTic(int id, int periodoId)
    {
        var c = await db.Circulacoes.Include(x => x.HorariosTic).FirstOrDefaultAsync(x => x.Id == id);
        var p = c?.HorariosTic.FirstOrDefault(h => h.Id == periodoId);
        if (c is null || p is null) return NotFound();
        HorariosTic.Remover(c, p, DateOnly.FromDateTime(DateTime.Today));
        await db.SaveChangesAsync();
        await log.LogAsync(CategoriaRegisto.Dados, "Horário TIC",
            $"Circulação {c.ViagemId}: removido o período de {p.DataInicio:dd/MM/yyyy} a {p.DataFim:dd/MM/yyyy}.", NivelRegisto.Aviso, ownerId: c.OwnerId);
        TempData["Ok"] = "Período removido do histórico.";
        return RedirectToAction(nameof(Details), new { id });
    }

    // ---- Calendário ----

    /// <summary>Eventos FullCalendar (fundo) para os dias em que a circulação se realiza.</summary>
    [HttpGet]
    public async Task<IActionResult> Dias(int id, DateTime start, DateTime end)
    {
        var (from, to) = (DateOnly.FromDateTime(start), DateOnly.FromDateTime(end));
        var dias = await db.CirculacaoDias
            .Where(d => d.CirculacaoId == id && d.Data >= from && d.Data < to)
            .Select(d => d.Data).ToListAsync();
        return Json(dias.Select(d => new { start = d.ToString("yyyy-MM-dd"), display = "background", color = "#198754" }));
    }

    public record DiasRequest(DateOnly Inicio, DateOnly Fim, int[]? DiasSemana, bool Realiza);

    /// <summary>Marca ou desmarca dias num intervalo (opcionalmente só alguns dias da semana; 0 = domingo).</summary>
    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> DefinirDias(int id, [FromBody] DiasRequest req)
    {
        var c = await db.Circulacoes.FindAsync(id);
        if (c is null) return NotFound();
        if (req.Fim < req.Inicio || req.Fim.DayNumber - req.Inicio.DayNumber > 731)
            return BadRequest("Intervalo inválido (máximo 2 anos).");

        var dias = Enumerable.Range(0, req.Fim.DayNumber - req.Inicio.DayNumber + 1)
            .Select(i => req.Inicio.AddDays(i))
            .Where(d => req.DiasSemana is not { Length: > 0 } || req.DiasSemana.Contains((int)d.DayOfWeek))
            .ToList();

        var existentes = await db.CirculacaoDias
            .Where(d => d.CirculacaoId == id && d.Data >= req.Inicio && d.Data <= req.Fim)
            .ToListAsync();
        var existentesSet = existentes.Select(d => d.Data).ToHashSet();

        if (req.Realiza)
            db.CirculacaoDias.AddRange(dias.Where(d => !existentesSet.Contains(d)).Select(d => new CirculacaoDia { CirculacaoId = id, Data = d }));
        else
        {
            var remover = dias.ToHashSet();
            db.CirculacaoDias.RemoveRange(existentes.Where(d => remover.Contains(d.Data)));
        }
        await db.SaveChangesAsync();

        await log.LogAsync(CategoriaRegisto.Dados, "Calendário",
            $"Circulação {c.ViagemId}: {(req.Realiza ? "marcados" : "desmarcados")} {dias.Count} dia(s) entre {req.Inicio:dd/MM/yyyy} e {req.Fim:dd/MM/yyyy}.",
            ownerId: c.OwnerId);
        return Json(new { total = await db.CirculacaoDias.CountAsync(d => d.CirculacaoId == id) });
    }

    // ---- Percurso ----

    /// <summary>Guarda o percurso (ids das paragens por ordem); tem de começar na paragem do TIC.</summary>
    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> GuardarPercurso(int id, [FromBody] int[] percurso)
    {
        var c = await db.Circulacoes.Include(x => x.Percurso).Include(x => x.Operador).FirstOrDefaultAsync(x => x.Id == id);
        if (c is null) return NotFound();

        var ids = percurso.Distinct().ToList();
        var codigos = await db.Paragens.Where(p => p.OwnerId == c.OwnerId && ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Codigo);
        if (ids.Any(p => !codigos.ContainsKey(p))) return BadRequest("Paragem inválida no percurso.");
        var tic = gtfs.ParagensTic(c.Operador);
        if (percurso.Length > 0 && tic.Count > 0 && !tic.Contains(codigos[percurso[0]]))
            return BadRequest($"O percurso tem de começar na paragem do TIC ({string.Join(", ", tic)}).");

        db.Percursos.RemoveRange(c.Percurso);
        await db.SaveChangesAsync();
        db.Percursos.AddRange(percurso.Select((p, i) => new PercursoParagem { CirculacaoId = id, Sequencia = i + 1, ParagemId = p }));
        await db.SaveChangesAsync();

        await log.LogAsync(CategoriaRegisto.Dados, "Percurso",
            $"Circulação {c.ViagemId}: percurso com {percurso.Length} paragem(ns).", ownerId: c.OwnerId);
        return Json(new { total = percurso.Length });
    }

    private static TimeOnly? Minuto(TimeOnly? t) => t is { } x ? new TimeOnly(x.Hour, x.Minute) : null;

    private async Task LoadLookups(string ownerId, int circulacaoId = 0)
    {
        ViewBag.Operadores = await db.Operadores.Where(o => o.OwnerId == ownerId).OrderBy(o => o.Designacao)
            .Select(o => new LookupItem(o.Id.ToString(), o.Nif + " – " + (o.Abreviatura ?? o.Designacao))).ToListAsync();
        ViewBag.Paragens = await ParagensLookup(ownerId);
        ViewBag.Ambitos = EnumExtensions.ToLookup<Ambito>();
        ViewBag.Categorias = EnumExtensions.ToLookup<ZonaCais>();
        ViewBag.TemHistoricoTic = circulacaoId != 0 && await db.CirculacaoHorariosTic.AnyAsync(h => h.CirculacaoId == circulacaoId);
    }

    private Task<List<LookupItem>> ParagensLookup(string ownerId) =>
        db.Paragens.Where(p => p.OwnerId == ownerId).OrderBy(p => p.Codigo)
            .Select(p => new LookupItem(p.Id.ToString(), p.Codigo + " – " + p.Nome)).ToListAsync();
}
