using System.Diagnostics;
using AppTIC.Web.Data;
using AppTIC.Web.Models;
using AppTIC.Web.Models.Domain;
using AppTIC.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AppTIC.Web.Controllers;

public class HomeController(ApplicationDbContext db, OcupacaoTerminal ocupacao) : Controller
{
    public async Task<IActionResult> Index()
    {
        var hoje = DateOnly.FromDateTime(DateTime.Today);
        var limite = hoje.AddDays(60);

        ViewBag.NumOperadores = await db.Operadores.CountAsync();
        ViewBag.NumParagens = await db.Paragens.CountAsync();
        ViewBag.NumCirculacoes = await db.Circulacoes.CountAsync();
        ViewBag.NumHoje = await db.CirculacaoDias.CountAsync(d => d.Data == hoje);
        ViewBag.NumFeeds = await db.GtfsFeeds.CountAsync();
        ViewBag.NumPedidosPendentes = await db.PedidosAlteracao.CountAsync(p => p.Estado == EstadoPedido.Pendente);

        ViewBag.AlvarasAExpirar = await db.Operadores
            .Where(o => o.AlvaraValidade != null && o.AlvaraValidade <= limite)
            .OrderBy(o => o.AlvaraValidade).Take(10)
            .Select(o => new ValueTuple<int, string, DateOnly>(o.Id, o.Designacao, o.AlvaraValidade!.Value)).ToListAsync();
        ViewBag.AutorizacoesAExpirar = await db.Circulacoes
            .Where(c => c.AutorizacaoValidade != null && c.AutorizacaoValidade <= limite)
            .OrderBy(c => c.AutorizacaoValidade).Take(10)
            .Select(c => new ValueTuple<int, string, DateOnly>(c.Id, c.ViagemId, c.AutorizacaoValidade!.Value)).ToListAsync();
        ViewBag.UltimosFeeds = await db.GtfsFeeds.OrderByDescending(f => f.CriadoEm).Take(5).ToListAsync();
        return View();
    }

    /// <summary>Ocupação do terminal num dia (por omissão hoje), em intervalos de 15 minutos.</summary>
    [HttpGet, Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Ocupacao(DateOnly? dia, CancellationToken ct) =>
        Json(await ocupacao.DoDiaAsync(dia ?? DateOnly.FromDateTime(DateTime.Today), ct));

    /// <summary>Tipo de viagem no TIC e destinos mais frequentes no dia, no mês e no ano do dia indicado.</summary>
    [HttpGet, Authorize(Roles = Roles.Admin)]
    public async Task<IActionResult> Resumo(DateOnly? dia, CancellationToken ct)
    {
        var d = dia ?? DateOnly.FromDateTime(DateTime.Today);
        var mes = new DateOnly(d.Year, d.Month, 1);
        return Json(new
        {
            Dia = await ocupacao.DoPeriodoAsync(d, d, ct: ct),
            Mes = await ocupacao.DoPeriodoAsync(mes, mes.AddMonths(1).AddDays(-1), ct: ct),
            Ano = await ocupacao.DoPeriodoAsync(new DateOnly(d.Year, 1, 1), new DateOnly(d.Year, 12, 31), ct: ct)
        });
    }

    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() =>
        View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
}
