using System.Globalization;
using AppTIC.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AppTIC.Web.Controllers;

[Authorize]
public class SimulacaoController(SimulacaoService simulacao) : Controller
{
    public IActionResult Index() => View();

    [HttpGet]
    public async Task<IActionResult> Dados(string? data, CancellationToken ct)
    {
        if (!DateOnly.TryParseExact(data, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dia) ||
            dia.Year < 2 || dia.Year > 9998)
            return BadRequest("Indique uma data válida no formato AAAA-MM-DD.");

        Response.Headers.CacheControl = "no-store";
        return Json(await simulacao.DoDiaAsync(dia, ct));
    }
}
