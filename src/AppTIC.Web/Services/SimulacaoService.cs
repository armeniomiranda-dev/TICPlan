using AppTIC.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace AppTIC.Web.Services;

public record SimulacaoOrigem(
    int CirculacaoId, DateOnly Data, string ViagemId, string Operador,
    string? Linha, string? Origem, string? Destino,
    TimeOnly? Chegada, TimeOnly? Partida, int PermanenciaMinutos);

public record SimulacaoPassagem(
    string Id, string ViagemId, string Operador, string? Linha, string? Origem, string? Destino,
    DateOnly DataCirculacao, int InicioSegundos, int FimSegundos, bool PermanenciaEstimada);

public class SimulacaoService(ApplicationDbContext db, IConfiguration config)
{
    public async Task<IReadOnlyList<SimulacaoPassagem>> DoDiaAsync(DateOnly dia, CancellationToken ct = default)
    {
        var anterior = dia.AddDays(-1);
        var seguinte = dia.AddDays(1);
        var permanenciaPorOmissao = config.GetValue("Terminal:PermanenciaPorOmissaoMinutos", 10);
        var dias = await db.CirculacaoDias.AsNoTracking()
            .Where(d => d.Data >= anterior && d.Data <= seguinte)
            .Select(d => new
            {
                d.CirculacaoId,
                d.Data,
                d.Circulacao!.ViagemId,
                Operador = d.Circulacao.Operador!.Abreviatura ?? d.Circulacao.Operador.Designacao,
                Linha = d.Circulacao.LinhaId,
                Origem = d.Circulacao.Origem == null ? null : d.Circulacao.Origem.Nome,
                Destino = d.Circulacao.Destino == null ? null : d.Circulacao.Destino.Nome,
                d.Circulacao.Chegada,
                d.Circulacao.Partida,
                d.Circulacao.TempoEmbarqueMinutos
            }).ToListAsync(ct);

        var ids = dias.Select(d => d.CirculacaoId).Distinct().ToArray();
        var periodos = await db.CirculacaoHorariosTic.AsNoTracking()
            .Where(h => ids.Contains(h.CirculacaoId) && h.DataInicio <= seguinte && h.DataFim >= anterior)
            .Select(h => new { h.CirculacaoId, h.DataInicio, h.DataFim, h.Chegada, h.Partida })
            .ToListAsync(ct);
        var porCirculacao = periodos.ToLookup(h => h.CirculacaoId);

        return dias.Select(d =>
            {
                var periodo = porCirculacao[d.CirculacaoId].FirstOrDefault(h => h.DataInicio <= d.Data && d.Data <= h.DataFim);
                return CriarPassagem(dia, new SimulacaoOrigem(
                    d.CirculacaoId, d.Data, d.ViagemId, d.Operador, d.Linha, d.Origem, d.Destino,
                    periodo is not null ? periodo.Chegada : d.Chegada,
                    periodo is not null ? periodo.Partida : d.Partida,
                    d.TempoEmbarqueMinutos ?? permanenciaPorOmissao));
            })
            .OfType<SimulacaoPassagem>()
            .OrderBy(p => p.InicioSegundos).ThenBy(p => p.Id)
            .ToList();
    }

    public static SimulacaoPassagem? CriarPassagem(DateOnly dia, SimulacaoOrigem origem)
    {
        var permanencia = OcupacaoTerminal.Permanencia(origem.Data, origem.Chegada, origem.Partida, origem.PermanenciaMinutos);
        if (permanencia is null) return null;
        var inicioDia = dia.ToDateTime(TimeOnly.MinValue);
        if (permanencia.Inicio >= inicioDia.AddDays(1) || permanencia.Fim < inicioDia ||
            (permanencia.Fim == inicioDia && permanencia.Inicio != permanencia.Fim)) return null;
        return new SimulacaoPassagem(
            $"{origem.CirculacaoId}-{origem.Data:yyyyMMdd}", origem.ViagemId, origem.Operador,
            origem.Linha, origem.Origem, origem.Destino, origem.Data,
            (int)(permanencia.Inicio - inicioDia).TotalSeconds,
            (int)(permanencia.Fim - inicioDia).TotalSeconds,
            origem.Chegada is null || origem.Partida is null);
    }
}
