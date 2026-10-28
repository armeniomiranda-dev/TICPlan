using AppTIC.Web.Data;
using AppTIC.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AppTIC.Web.Controllers;

/// <summary>Vista geral (FullCalendar) das chegadas e partidas na paragem do TIC.</summary>
public class CalendarioController(ApplicationDbContext db) : Controller
{
    private static readonly string[] Cores =
        ["#0d6efd", "#198754", "#fd7e14", "#6f42c1", "#d63384", "#20c997", "#0dcaf0", "#795548", "#6c757d", "#b8860b"];

    public async Task<IActionResult> Index()
    {
        var operadores = await Operadores();
        ViewBag.Operadores = operadores.Select(o => new LookupItem(o.Id.ToString(), o.Nome)).ToList();
        ViewBag.Legenda = operadores;
        return View();
    }

    /// <param name="tipos">"C" (chegadas), "P" (partidas) ou ambos ("CP").</param>
    [HttpGet]
    public async Task<IActionResult> Eventos(DateTime start, DateTime end, int? operadorId = null, string tipos = "CP")
    {
        var (from, to) = (DateOnly.FromDateTime(start), DateOnly.FromDateTime(end));
        if (to.DayNumber - from.DayNumber > 62) return BadRequest("Intervalo demasiado grande.");

        // A partida pode ser no dia seguinte ao dia da circulação (chegada antes da meia-noite), daí o dia anterior.
        var query = db.CirculacaoDias.Where(d => d.Data >= from.AddDays(-1) && d.Data < to);
        if (operadorId is not null) query = query.Where(d => d.Circulacao!.OperadorId == operadorId);

        var dias = await query.Select(d => new
        {
            d.Data,
            d.CirculacaoId,
            d.Circulacao!.ViagemId,
            d.Circulacao.LinhaId,
            d.Circulacao.DesignacaoLinha,
            d.Circulacao.OperadorId,
            Origem = d.Circulacao.Origem!.Nome,
            Destino = d.Circulacao.Destino!.Nome,
            // Horas do período do histórico que se aplica a este dia; sem período, as da circulação.
            Periodo = d.Circulacao.HorariosTic.Where(h => h.DataInicio <= d.Data && d.Data <= h.DataFim)
                .Select(h => new { h.Chegada, h.Partida }).FirstOrDefault(),
            d.Circulacao.Chegada,
            d.Circulacao.Partida
        }).ToListAsync();

        var cores = (await Operadores()).ToDictionary(o => o.Id);
        var eventos = dias.Select(d => d.Periodo is { } h ? d with { Chegada = h.Chegada, Partida = h.Partida } : d).SelectMany(d =>
        {
            var op = cores.GetValueOrDefault(d.OperadorId);
            var lista = new List<object>();
            void Evento(string tipo, DateOnly data, TimeOnly hora)
            {
                if (!tipos.Contains(tipo) || data < from || data >= to) return;
                var cor = op?.Cor ?? Cores[^1];
                lista.Add(new
                {
                    id = $"{d.CirculacaoId}-{d.Data:yyyyMMdd}-{tipo}",
                    title = $"{(tipo == "C" ? "↓" : "↑")} {op?.Nome} {d.LinhaId}",
                    start = data.ToDateTime(hora).ToString("yyyy-MM-ddTHH:mm"),
                    url = Url.Action("Details", "Circulacoes", new { id = d.CirculacaoId }),
                    // Partida: cor cheia; chegada: contorno da cor do operador.
                    backgroundColor = tipo == "P" ? cor : "#ffffff",
                    borderColor = cor,
                    textColor = tipo == "P" ? "#ffffff" : cor,
                    extendedProps = new
                    {
                        tipo,
                        operador = op?.Nome,
                        viagem = d.ViagemId,
                        linha = $"{d.LinhaId} {d.DesignacaoLinha}".Trim(),
                        percurso = $"{d.Origem ?? "?"} → {d.Destino ?? "?"}",
                        chegada = d.Chegada?.ToString("HH:mm"),
                        partida = d.Partida?.ToString("HH:mm")
                    }
                });
            }
            if (d.Chegada is { } c) Evento("C", d.Data, c);
            if (d.Partida is { } p) Evento("P", d.Chegada is { } c2 && p < c2 ? d.Data.AddDays(1) : d.Data, p);
            return lista;
        });
        return Json(eventos);
    }

    public record OperadorCor(int Id, string Nome, string Cor);

    /// <summary>Operadores visíveis, cada um com uma cor fixa (pela ordem do Id).</summary>
    private async Task<List<OperadorCor>> Operadores()
    {
        var ops = await db.Operadores.OrderBy(o => o.Id).Select(o => new { o.Id, Nome = o.Abreviatura ?? o.Designacao }).ToListAsync();
        return ops.Select((o, i) => new OperadorCor(o.Id, o.Nome, Cores[i % Cores.Length])).OrderBy(o => o.Nome).ToList();
    }
}
