using AppTIC.Web.Models.Domain;
using AppTIC.Web.Data;
using AppTIC.Web.Models.ViewModels;
using AppTIC.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AppTIC.Web.Controllers;

/// <summary>
/// Exportação do planeamento (diário, semanal, mensal, anual ou intervalo) no formato "Horarios.csv" do terminal.
/// Os utilizadores do operador exportam só as suas circulações (filtros globais).
/// </summary>
public class ExportacaoController(ApplicationDbContext db, ExportacaoPlaneamento exportacao, IActivityLog log) : Controller
{
    /// <summary>Máximo de dias com cálculo de cais (o planeamento de cada dia pode levar alguns segundos).</summary>
    public const int MaxDiasCais = 7;
    private const int LinhasPreVisualizacao = 100;

    public async Task<IActionResult> Index(PeriodoExportacao periodo = PeriodoExportacao.Diario, DateOnly? data = null, DateOnly? ate = null,
        int? operadorId = null, bool cais = false, bool ver = false, CancellationToken ct = default)
    {
        var d = data ?? DateOnly.FromDateTime(DateTime.Today);
        var (de, fim) = ExportacaoPlaneamento.Intervalo(periodo, d, ate);
        ViewBag.Periodo = periodo;
        ViewBag.Data = d;
        ViewBag.Ate = ate ?? fim;
        ViewBag.De = de;
        ViewBag.Fim = fim;
        ViewBag.OperadorId = operadorId;
        ViewBag.Cais = cais;
        ViewBag.Operadores = await OperadoresAsync();
        var linhas = new List<LinhaPlaneamento>();
        if (ver)
            await foreach (var l in exportacao.LinhasAsync(de, fim, operadorId, cais && DiasDe(de, fim) <= MaxDiasCais, ct))
                if (linhas.Count < LinhasPreVisualizacao) linhas.Add(l); else break;
        ViewBag.Ver = ver;
        return View(linhas);
    }

    /// <summary>Descarrega o CSV (separador ";", ISO-8859-1), em streaming.</summary>
    public async Task Csv(PeriodoExportacao periodo = PeriodoExportacao.Diario, DateOnly? data = null, DateOnly? ate = null,
        int? operadorId = null, bool cais = false, CancellationToken ct = default)
    {
        var (de, fim) = ExportacaoPlaneamento.Intervalo(periodo, data ?? DateOnly.FromDateTime(DateTime.Today), ate);
        var nome = de == fim ? $"Horarios_{de:yyyyMMdd}.csv" : $"Horarios_{de:yyyyMMdd}_{fim:yyyyMMdd}.csv";
        Response.ContentType = "text/csv; charset=iso-8859-1";
        Response.Headers.ContentDisposition = $"attachment; filename=\"{nome}\"";
        await log.LogAsync(CategoriaRegisto.Dados, "Exportação",
            $"Planeamento {de:dd/MM/yyyy}–{fim:dd/MM/yyyy}{(operadorId is { } o ? $" (operador {o})" : "")}.");
        await ExportacaoPlaneamento.EscreverCsvAsync(Response.Body,
            exportacao.LinhasAsync(de, fim, operadorId, cais && DiasDe(de, fim) <= MaxDiasCais, ct), ct);
    }

    private static int DiasDe(DateOnly de, DateOnly ate) => ate.DayNumber - de.DayNumber + 1;

    private Task<List<OperadorOuGrupoItem>> OperadoresAsync() =>
        db.Operadores.OrderByDescending(o => o.EGrupo).ThenBy(o => o.Designacao)
            .Select(o => new OperadorOuGrupoItem(o.Id.ToString(), (o.Abreviatura ?? o.Designacao) + (o.EGrupo ? " (grupo)" : ""),
                o.EGrupo ? "Grupos" : "Operadores")).ToListAsync();
}
