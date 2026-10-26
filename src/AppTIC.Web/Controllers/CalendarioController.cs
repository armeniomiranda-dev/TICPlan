using AppTIC.Web.Data;
using AppTIC.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AppTIC.Web.Controllers;

/// <summary>Vista geral (FullCalendar) das circulações no TIC.</summary>
public class CalendarioController(ApplicationDbContext db) : Controller
{
    public async Task<IActionResult> Index()
    {
        ViewBag.Operadores = await db.Operadores.OrderBy(o => o.Designacao)
            .Select(o => new LookupItem(o.Id.ToString(), o.Abreviatura ?? o.Designacao)).ToListAsync();
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> Eventos(DateTime start, DateTime end, int? operadorId = null)
    {
        var (from, to) = (DateOnly.FromDateTime(start), DateOnly.FromDateTime(end));
        if (to.DayNumber - from.DayNumber > 62) return BadRequest("Intervalo demasiado grande.");

        var query = db.CirculacaoDias.Where(d => d.Data >= from && d.Data < to);
        if (operadorId is not null) query = query.Where(d => d.Circulacao!.OperadorId == operadorId);

        var dias = await query.Select(d => new
        {
            d.Data,
            d.CirculacaoId,
            d.Circulacao!.ViagemId,
            d.Circulacao.LinhaId,
            Operador = d.Circulacao.Operador!.Abreviatura ?? d.Circulacao.Operador.Designacao,
            d.Circulacao.Chegada,
            d.Circulacao.Partida
        }).ToListAsync();

        var eventos = dias.Select(d =>
        {
            var hora = d.Partida ?? d.Chegada ?? TimeOnly.MinValue;
            var tipo = d.Partida is not null ? "P" : "C";
            return new
            {
                id = $"{d.CirculacaoId}-{d.Data:yyyyMMdd}",
                title = $"{tipo} {d.Operador} {d.LinhaId} · {d.ViagemId}",
                start = d.Data.ToDateTime(hora).ToString("yyyy-MM-ddTHH:mm"),
                url = Url.Action("Details", "Circulacoes", new { id = d.CirculacaoId }),
                color = d.Partida is not null ? "#0d6efd" : "#6f42c1"
            };
        });
        return Json(eventos);
    }
}
