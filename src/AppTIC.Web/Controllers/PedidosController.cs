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
/// Pedidos de alteração de circulações: os utilizadores do operador pedem, o Admin responde.
/// O filtro global limita os pedidos às circulações do operador do utilizador.
/// </summary>
public class PedidosController(ApplicationDbContext db, ICurrentUser currentUser, IActivityLog log) : Controller
{
    public async Task<IActionResult> Index(int? circulacaoId = null)
    {
        var query = db.PedidosAlteracao.AsQueryable();
        if (circulacaoId is not null) query = query.Where(p => p.CirculacaoId == circulacaoId);
        var data = await query.OrderBy(p => p.Estado != EstadoPedido.Pendente).ThenByDescending(p => p.CriadoEm)
            .Select(p => new
            {
                p.Id, p.CirculacaoId, p.Circulacao!.ViagemId, Operador = p.Circulacao.Operador!.Abreviatura ?? p.Circulacao.Operador.Designacao,
                p.Descricao, p.Estado, PedidoPor = p.PedidoPor!.Email, p.CriadoEm, p.RespondidoEm
            }).ToListAsync();
        return View(data.Select(p => new PedidoRow(p.Id, p.CirculacaoId, p.ViagemId, p.Operador, p.Descricao, p.Estado.DisplayName(),
            p.PedidoPor, p.CriadoEm.ToLocalTime(), p.RespondidoEm?.ToLocalTime())).ToList());
    }

    public async Task<IActionResult> Criar(int circulacaoId)
    {
        var c = await db.Circulacoes.FindAsync(circulacaoId);
        if (c is null) return NotFound();
        ViewBag.Circulacao = c;
        return View(new PedidoAlteracao { CirculacaoId = c.Id, NovaChegada = c.Chegada, NovaPartida = c.Partida });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Criar(PedidoAlteracao model)
    {
        // A circulação tem de ser visível ao utilizador (do seu operador).
        var c = await db.Circulacoes.FindAsync(model.CirculacaoId);
        if (c is null) return NotFound();
        ModelState.Remove(nameof(PedidoAlteracao.PedidoPorId));
        if (model.DataInicio is { } i && model.DataFim is { } f && f < i)
            ModelState.AddModelError(nameof(PedidoAlteracao.DataFim), "A data de fim tem de ser igual ou posterior à de início.");
        if (!ModelState.IsValid)
        {
            ViewBag.Circulacao = c;
            return View(model);
        }

        var pedido = new PedidoAlteracao
        {
            CirculacaoId = c.Id,
            Descricao = model.Descricao.Trim(),
            DataInicio = model.DataInicio,
            DataFim = model.DataFim,
            NovaChegada = model.NovaChegada,
            NovaPartida = model.NovaPartida,
            Estado = EstadoPedido.Pendente,
            PedidoPorId = currentUser.UserId!,
            CriadoEm = DateTime.UtcNow
        };
        db.PedidosAlteracao.Add(pedido);
        await db.SaveChangesAsync();
        await log.LogAsync(CategoriaRegisto.Dados, "Pedido de alteração", $"Circulação {c.ViagemId}: pedido #{pedido.Id}.");
        TempData["Ok"] = "Pedido de alteração enviado.";
        return RedirectToAction("Details", "Circulacoes", new { id = c.Id });
    }

    public async Task<IActionResult> Details(int id)
    {
        var p = await db.PedidosAlteracao.Include(x => x.Circulacao).ThenInclude(c => c!.Operador).Include(x => x.PedidoPor)
            .FirstOrDefaultAsync(x => x.Id == id);
        return p is null ? NotFound() : View(p);
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Responder(int id, EstadoPedido estado, string? resposta)
    {
        var p = await db.PedidosAlteracao.Include(x => x.Circulacao).FirstOrDefaultAsync(x => x.Id == id);
        if (p is null) return NotFound();
        if (estado == EstadoPedido.Pendente) return BadRequest();
        p.Estado = estado;
        p.Resposta = string.IsNullOrWhiteSpace(resposta) ? null : resposta.Trim();
        p.RespondidoPor = currentUser.UserName;
        p.RespondidoEm = DateTime.UtcNow;
        await db.SaveChangesAsync();
        await log.LogAsync(CategoriaRegisto.Dados, "Pedido de alteração",
            $"Circulação {p.Circulacao!.ViagemId}: pedido #{p.Id} {estado.DisplayName().ToLowerInvariant()}.", ownerId: p.Circulacao.OwnerId);
        TempData["Ok"] = $"Pedido {estado.DisplayName().ToLowerInvariant()}.";
        return RedirectToAction(nameof(Index));
    }
}
