using AppTIC.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace AppTIC.Web.Controllers;

public record TempoRealViewModel(
    FlixbusSnapshot Flixbus, StcpSnapshot Stcp, CpSnapshot Cp, MetroSnapshot Metro,
    IReadOnlyList<LinhaTempoReal> Linhas, int Horas);

/// <summary>Tempo real no TIC: FlixBus, STCP, CP e Metro do Porto numa só tabela (dados em cache dos workers em segundo plano).</summary>
public class TempoRealController(
    FlixbusRealtimeStore flixbus, StcpRealtimeStore stcp, CpRealtimeStore cp, MetroRealtimeStore metro, TimeProvider tempo) : Controller
{
    public static readonly int[] HorasPossiveis = [1, 2, 4, 6];

    /// <param name="horas">Janela da tabela: próximas N horas (e os últimos 15 min).</param>
    public IActionResult Index(int horas = 1)
    {
        if (!HorasPossiveis.Contains(horas)) horas = 1;
        var (f, s, c, m) = (flixbus.Atual, stcp.Atual, cp.Atual, metro.Atual);
        var linhas = TempoRealAgregado.Agregar(f, s, c, m, tempo.GetUtcNow(), TimeSpan.FromMinutes(15), TimeSpan.FromHours(horas));
        return View(new TempoRealViewModel(f, s, c, m, linhas, horas));
    }
}
