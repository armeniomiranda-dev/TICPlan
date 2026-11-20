using AppTIC.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace AppTIC.Web.Controllers;

/// <summary>Chegadas e partidas da FlixBus no TIC em tempo real (dados em cache do FlixbusRealtimeWorker).</summary>
public class TempoRealController(FlixbusRealtimeStore store) : Controller
{
    public IActionResult Index() => View(store.Atual);
}
