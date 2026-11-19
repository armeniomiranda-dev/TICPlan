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
/// Pedidos dos operadores: alteração de uma circulação, nova circulação, ou remoção de uma circulação a partir de uma data.
/// Os utilizadores do operador pedem, o Admin responde e, ao aceitar, pode aplicar o pedido automaticamente.
/// O filtro global limita os pedidos aos do operador (ou grupo) do utilizador.
/// </summary>
public class PedidosController(ApplicationDbContext db, ICurrentUser currentUser, IActivityLog log, NumeracaoService numeracao) : Controller
{
    public async Task<IActionResult> Index(int? circulacaoId = null)
    {
        var query = db.PedidosAlteracao.AsQueryable();
        if (circulacaoId is not null) query = query.Where(p => p.CirculacaoId == circulacaoId);
        var data = await query.OrderBy(p => p.Estado != EstadoPedido.Pendente).ThenByDescending(p => p.CriadoEm)
            .Select(p => new
            {
                p.Id, p.Tipo, p.CirculacaoId, ViagemId = p.Circulacao != null ? p.Circulacao.ViagemId : p.ViagemId,
                Operador = p.Operador!.Abreviatura ?? p.Operador.Designacao,
                p.Descricao, p.Estado, PedidoPor = p.PedidoPor!.Email, p.CriadoEm, p.RespondidoEm
            }).ToListAsync();
        return View(data.Select(p => new PedidoRow(p.Id, p.Tipo.DisplayName(), p.CirculacaoId, p.ViagemId ?? "–", p.Operador, p.Descricao,
            p.Estado.DisplayName(), p.PedidoPor, p.CriadoEm.ToLocalTime(), p.RespondidoEm?.ToLocalTime())).ToList());
    }

    /// <summary>Formulário de pedido. Alteração e remoção partem de uma circulação; a nova circulação, de um operador.</summary>
    public async Task<IActionResult> Criar(int? circulacaoId, TipoPedido tipo = TipoPedido.Alteracao)
    {
        PedidoAlteracao model;
        if (tipo == TipoPedido.NovaCirculacao)
        {
            var (_, min, max) = await AnoVigenteAsync();
            model = new PedidoAlteracao
            {
                Tipo = tipo, OperadorId = currentUser.OperadorId ?? 0, DataInicio = min, DataFim = max, DiasSemana = "0,1,2,3,4,5,6"
            };
        }
        else
        {
            if (circulacaoId is null) return BadRequest();
            var c = await db.Circulacoes.FindAsync(circulacaoId);
            if (c is null) return NotFound();
            model = new PedidoAlteracao { Tipo = tipo, CirculacaoId = c.Id, OperadorId = c.OperadorId, NovaChegada = c.Chegada, NovaPartida = c.Partida };
            if (tipo == TipoPedido.Remocao) model.DataInicio = DateOnly.FromDateTime(DateTime.Today).AddDays(1);
        }
        await PrepararFormAsync(model);
        return View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Criar(PedidoAlteracao model, string? calendario)
    {
        ModelState.Remove(nameof(PedidoAlteracao.PedidoPorId));
        Circulacao? c = null;
        List<DateOnly> diasPedidos = [];
        if (model.Tipo == TipoPedido.NovaCirculacao)
        {
            // O operador tem de ser visível ao utilizador (o seu, ou um do seu grupo).
            if (!await db.Operadores.AnyAsync(o => o.Id == model.OperadorId))
                ModelState.AddModelError(nameof(PedidoAlteracao.OperadorId), "Escolha o operador.");
            if (string.IsNullOrWhiteSpace(model.ViagemId))
                ModelState.AddModelError(nameof(PedidoAlteracao.ViagemId), "Indique o ID de viagem / código de circulação.");
            if (model.NovaChegada is null && model.NovaPartida is null)
                ModelState.AddModelError(nameof(PedidoAlteracao.NovaPartida), "Indique a chegada e/ou a partida do TIC.");
            // Calendário completo, só no ano em vigor (a partir de amanhã).
            var (ano, min, max) = await AnoVigenteAsync();
            var invalidos = 0;
            foreach (var texto in (calendario ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (DateOnly.TryParseExact(texto, "yyyy-MM-dd", out var dia) && dia >= min && dia <= max) diasPedidos.Add(dia);
                else invalidos++;
            }
            diasPedidos = diasPedidos.Distinct().Order().ToList();
            if (invalidos > 0)
                ModelState.AddModelError("Calendario", $"Os pedidos de nova circulação são para {ano}: escolha dias entre {min:dd/MM/yyyy} e {max:dd/MM/yyyy}.");
            else if (diasPedidos.Count == 0)
                ModelState.AddModelError("Calendario", "Marque no calendário os dias em que a circulação se realiza.");
            model.DataInicio = diasPedidos.Count > 0 ? diasPedidos[0] : null;
            model.DataFim = diasPedidos.Count > 0 ? diasPedidos[^1] : null;
            model.DiasSemana = string.Join(",", diasPedidos.Select(d => (int)d.DayOfWeek).Distinct().Order());
            foreach (var (campo, paragemId) in new[] { (nameof(PedidoAlteracao.OrigemId), model.OrigemId), (nameof(PedidoAlteracao.DestinoId), model.DestinoId) })
                if (paragemId is not null && !await db.Paragens.AnyAsync(p => p.Id == paragemId))
                    ModelState.AddModelError(campo, "Paragem inválida.");
        }
        else
        {
            c = await db.Circulacoes.FindAsync(model.CirculacaoId);
            if (c is null) return NotFound();
            model.OperadorId = c.OperadorId;
            if (model.Tipo == TipoPedido.Remocao && model.DataInicio is null)
                ModelState.AddModelError(nameof(PedidoAlteracao.DataInicio), "Indique a partir de que dia a circulação deixa de se realizar.");
        }
        if (model.DataInicio is { } i && model.DataFim is { } f && f < i)
            ModelState.AddModelError(nameof(PedidoAlteracao.DataFim), "A data de fim tem de ser igual ou posterior à de início.");
        if (!ModelState.IsValid)
        {
            await PrepararFormAsync(model, diasPedidos);
            return View(model);
        }

        var nova = model.Tipo == TipoPedido.NovaCirculacao;
        var pedido = new PedidoAlteracao
        {
            Tipo = model.Tipo,
            OperadorId = model.OperadorId,
            CirculacaoId = c?.Id,
            Descricao = model.Descricao.Trim(),
            DataInicio = model.DataInicio,
            DataFim = model.Tipo == TipoPedido.Remocao ? null : model.DataFim,
            NovaChegada = model.Tipo == TipoPedido.Remocao ? null : model.NovaChegada,
            NovaPartida = model.Tipo == TipoPedido.Remocao ? null : model.NovaPartida,
            ViagemId = nova ? model.ViagemId?.Trim() : null,
            LinhaId = nova ? model.LinhaId : null,
            DesignacaoLinha = nova ? model.DesignacaoLinha : null,
            OrigemId = nova ? model.OrigemId : null,
            DestinoId = nova ? model.DestinoId : null,
            DiasSemana = nova ? model.DiasSemana : null,
            Estado = EstadoPedido.Pendente,
            PedidoPorId = currentUser.UserId!,
            CriadoEm = DateTime.UtcNow,
            Dias = nova ? diasPedidos.Select(d => new PedidoDia { Data = d }).ToList() : []
        };
        db.PedidosAlteracao.Add(pedido);
        await db.SaveChangesAsync();
        var alvo = c?.ViagemId ?? pedido.ViagemId;
        await log.LogAsync(CategoriaRegisto.Dados, $"Pedido: {pedido.Tipo.DisplayName().ToLowerInvariant()}", $"Circulação {alvo}: pedido #{pedido.Id}.");
        TempData["Ok"] = "Pedido enviado.";
        return c is null ? RedirectToAction(nameof(Details), new { id = pedido.Id }) : RedirectToAction("Details", "Circulacoes", new { id = c.Id });
    }

    public async Task<IActionResult> Details(int id)
    {
        var p = await db.PedidosAlteracao.Include(x => x.Circulacao).Include(x => x.Operador).ThenInclude(o => o!.Grupo)
            .Include(x => x.PedidoPor).Include(x => x.Origem).Include(x => x.Destino).Include(x => x.Dias)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (p is null) return NotFound();
        if (p.Tipo == TipoPedido.NovaCirculacao && p.Operador is not null && currentUser.IsAdmin)
            ViewBag.ProximoIdTic = await numeracao.ProximoIdTicAsync(p.Operador);
        return View(p);
    }

    /// <summary>
    /// Responde ao pedido. Ao aceitar com <paramref name="aplicar"/>:
    /// alteração → horas no TIC no período (nova versão da declaração);
    /// nova circulação → cria a circulação (ID TIC e nova declaração);
    /// remoção → deixa de se realizar a partir da data (nova versão da declaração).
    /// </summary>
    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Responder(int id, EstadoPedido estado, string? resposta, bool aplicar = false, string? viagemId = null)
    {
        var p = await db.PedidosAlteracao.Include(x => x.Operador).ThenInclude(o => o!.Grupo).Include(x => x.Dias).FirstOrDefaultAsync(x => x.Id == id);
        if (p is null) return NotFound();
        if (estado == EstadoPedido.Pendente) return BadRequest();
        if (p.Estado != EstadoPedido.Pendente)
        {
            TempData["Erro"] = "O pedido já foi respondido.";
            return RedirectToAction(nameof(Details), new { id });
        }

        await using var tx = await db.Database.BeginTransactionAsync();
        string? aplicado = null;
        if (estado == EstadoPedido.Aceite && aplicar)
        {
            try
            {
                aplicado = p.Tipo switch
                {
                    TipoPedido.NovaCirculacao => await CriarCirculacaoAsync(p, viagemId),
                    TipoPedido.Remocao => await RemoverAsync(p),
                    _ => await AlterarHorarioAsync(p)
                };
            }
            catch (PedidoInvalidoException ex)
            {
                TempData["Erro"] = ex.Message;
                return RedirectToAction(nameof(Details), new { id });
            }
            p.Aplicado = true;
        }

        p.Estado = estado;
        p.Resposta = string.IsNullOrWhiteSpace(resposta) ? null : resposta.Trim();
        p.RespondidoPor = currentUser.UserName;
        p.RespondidoEm = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await tx.CommitAsync();

        var c = p.CirculacaoId is { } cid ? await db.Circulacoes.FindAsync(cid) : null;
        await log.LogAsync(CategoriaRegisto.Dados, $"Pedido: {p.Tipo.DisplayName().ToLowerInvariant()}",
            $"Circulação {c?.ViagemId ?? p.ViagemId}: pedido #{p.Id} {estado.DisplayName().ToLowerInvariant()}." + (aplicado is null ? "" : $" {aplicado}"),
            ownerId: c?.OwnerId);
        TempData["Ok"] = $"Pedido {estado.DisplayName().ToLowerInvariant()}." + (aplicado is null ? "" : $" {aplicado}");
        return RedirectToAction(nameof(Details), new { id });
    }

    private sealed class PedidoInvalidoException(string message) : Exception(message);

    private static DateOnly Hoje => DateOnly.FromDateTime(DateTime.Today);

    private async Task<string> AlterarHorarioAsync(PedidoAlteracao p)
    {
        var c = await db.Circulacoes.Include(x => x.HorariosTic).FirstOrDefaultAsync(x => x.Id == p.CirculacaoId)
            ?? throw new PedidoInvalidoException("A circulação já não existe.");
        if (p.DataInicio is not { } inicio || (p.NovaChegada is null && p.NovaPartida is null))
            throw new PedidoInvalidoException("Para aplicar automaticamente, o pedido tem de ter a data de início e a nova chegada e/ou partida. Aceite sem aplicar e altere a circulação à mão.");
        var ultimo = await db.CirculacaoDias.Where(d => d.CirculacaoId == c.Id).MaxAsync(d => (DateOnly?)d.Data);
        var fim = p.DataFim ?? (ultimo is { } u && u >= inicio ? u : inicio.AddYears(1));
        HorariosTic.Definir(c, inicio, fim, p.NovaChegada, p.NovaPartida, OrigemHorario.Manual, Hoje);
        var d = await numeracao.NovaVersaoAsync(c, $"Pedido #{p.Id}: horário de {c.ViagemId} de {inicio:dd/MM/yyyy} a {fim:dd/MM/yyyy}.");
        return $"Horas no TIC alteradas de {inicio:dd/MM/yyyy} a {fim:dd/MM/yyyy}" + (d is null ? "." : $"; declaração {d.Codigo}.");
    }

    private async Task<string> RemoverAsync(PedidoAlteracao p)
    {
        var c = await db.Circulacoes.Include(x => x.HorariosTic).FirstOrDefaultAsync(x => x.Id == p.CirculacaoId)
            ?? throw new PedidoInvalidoException("A circulação já não existe.");
        if (p.DataInicio is not { } desde) throw new PedidoInvalidoException("O pedido não indica a data a partir da qual a circulação deixa de se realizar.");

        var dias = await db.CirculacaoDias.Where(d => d.CirculacaoId == c.Id && d.Data >= desde).ExecuteDeleteAsync();
        foreach (var h in c.HorariosTic.Where(h => h.DataFim >= desde).ToList())
        {
            if (h.DataInicio >= desde) c.HorariosTic.Remove(h);
            else h.DataFim = desde.AddDays(-1);
        }
        HorariosTic.AtualizarVigente(c, Hoje);
        var nota = $"Deixa de se realizar a partir de {desde:dd/MM/yyyy} (pedido #{p.Id}).";
        c.Comentarios = string.IsNullOrWhiteSpace(c.Comentarios) ? nota : c.Comentarios + Environment.NewLine + nota;
        var d = await numeracao.NovaVersaoAsync(c, $"Pedido #{p.Id}: {c.ViagemId} deixa de se realizar a partir de {desde:dd/MM/yyyy}.");
        return $"Removidos {dias} dia(s) a partir de {desde:dd/MM/yyyy}" + (d is null ? "." : $"; declaração {d.Codigo}.");
    }

    private async Task<string> CriarCirculacaoAsync(PedidoAlteracao p, string? viagemId)
    {
        var operador = p.Operador!;
        var codigo = string.IsNullOrWhiteSpace(viagemId) ? p.ViagemId?.Trim() : viagemId.Trim();
        if (string.IsNullOrEmpty(codigo)) throw new PedidoInvalidoException("Indique o ID de viagem da nova circulação.");
        if (await db.Circulacoes.AnyAsync(c => c.OwnerId == operador.OwnerId && c.ViagemId == codigo))
            throw new PedidoInvalidoException($"Já existe uma circulação com o ID de viagem {codigo}. Indique outro.");
        if (p.DataInicio is not { } inicio || p.DataFim is not { } fim || fim < inicio || fim.DayNumber - inicio.DayNumber > 731)
            throw new PedidoInvalidoException("O período do pedido é inválido (máximo 2 anos).");

        var c = new Circulacao
        {
            OwnerId = operador.OwnerId, Operador = operador, ViagemId = codigo, LinhaId = p.LinhaId, DesignacaoLinha = p.DesignacaoLinha,
            Ambito = operador.Ambito, OrigemId = p.OrigemId, DestinoId = p.DestinoId, DeclaracaoCapacidade = true,
            Comentarios = $"Criada pelo pedido #{p.Id}."
        };
        c.IdTic = await numeracao.ProximoIdTicAsync(operador);
        // Calendário completo do pedido; pedidos antigos (sem calendário): período e dias da semana.
        var semana = p.DiasSemanaLista();
        var diasCirculacao = p.Dias.Count > 0 ? p.Dias.Select(x => x.Data).Order().ToList()
            : Enumerable.Range(0, fim.DayNumber - inicio.DayNumber + 1).Select(i => inicio.AddDays(i))
                .Where(d => semana.Count == 0 || semana.Contains((int)d.DayOfWeek)).ToList();
        c.Dias = diasCirculacao.Select(d => new CirculacaoDia { Data = d }).ToList();
        HorariosTic.Definir(c, inicio, fim, p.NovaChegada, p.NovaPartida, OrigemHorario.Manual, Hoje);
        db.Circulacoes.Add(c);
        var d = await numeracao.AtribuirNovaDeclaracaoAsync(c, operador, $"Nova circulação {codigo} (pedido #{p.Id}).");
        await db.SaveChangesAsync();
        p.CirculacaoId = c.Id;
        p.ViagemId = codigo;
        return $"Criada a circulação {codigo}" + (c.IdTic is null ? "" : $" (ID TIC {c.IdTic})") + $" na declaração {d.Codigo}, com {c.Dias.Count} dia(s).";
    }

    /// <summary>
    /// Os pedidos de nova circulação são para o ano em vigor (o das declarações mais recentes), de amanhã a 31 de dezembro.
    /// </summary>
    private async Task<(int Ano, DateOnly Min, DateOnly Max)> AnoVigenteAsync()
    {
        var ano = await numeracao.AnoAtualAsync();
        var amanha = Hoje.AddDays(1);
        var inicio = new DateOnly(ano, 1, 1);
        return (ano, amanha > inicio ? amanha : inicio, new DateOnly(ano, 12, 31));
    }

    private async Task PrepararFormAsync(PedidoAlteracao model, List<DateOnly>? dias = null)
    {
        ViewBag.Circulacao = model.CirculacaoId is { } id ? await db.Circulacoes.FindAsync(id) : null;
        if (model.Tipo != TipoPedido.NovaCirculacao) return;
        var (ano, min, max) = await AnoVigenteAsync();
        ViewBag.AnoVigente = ano;
        ViewBag.Min = min;
        ViewBag.Max = max;
        ViewBag.Calendario = (dias ?? []).Select(d => d.ToString("yyyy-MM-dd")).ToList();
        // Operadores visíveis ao utilizador: o seu e, num grupo, os operadores do grupo.
        ViewBag.Operadores = await db.Operadores.OrderBy(o => o.Designacao)
            .Select(o => new LookupItem(o.Id.ToString(), o.Abreviatura ?? o.Designacao)).ToListAsync();
        ViewBag.Paragens = await db.Paragens.OrderBy(p => p.Codigo)
            .Select(p => new LookupItem(p.Id.ToString(), p.Codigo + " – " + p.Nome)).ToListAsync();
    }
}
