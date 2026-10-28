using AppTIC.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace AppTIC.Web.Services;

/// <summary>Intervalo de 15 minutos: veículos no terminal (chegaram e ainda não partiram), chegadas e partidas.</summary>
public record OcupacaoIntervalo(string Hora, int Veiculos, int Chegadas, int Partidas);

/// <summary>Veículo no TIC entre Inicio e Fim.</summary>
public record PermanenciaTic(DateTime Inicio, DateTime Fim, bool Chega, bool Parte);

/// <summary>Ocupação do Terminal de Campanhã ao longo de um dia, em intervalos de 15 minutos.</summary>
public class OcupacaoTerminal(ApplicationDbContext db, IConfiguration config)
{
    public const int MinutosIntervalo = 15;

    /// <summary>Permanência quando o TIC é início ou fim de viagem e a circulação não indica o tempo de embarque/desembarque.</summary>
    private int PermanenciaPorOmissao => config.GetValue("Terminal:PermanenciaPorOmissaoMinutos", 10);

    public async Task<List<OcupacaoIntervalo>> DoDiaAsync(DateOnly dia, CancellationToken ct = default)
    {
        // O dia anterior conta para as circulações que chegam antes e partem depois da meia-noite.
        var passagens = await db.CirculacaoDias
            .Where(d => d.Data >= dia.AddDays(-1) && d.Data <= dia)
            .Select(d => new
            {
                d.Data,
                Periodo = d.Circulacao!.HorariosTic.Where(h => h.DataInicio <= d.Data && d.Data <= h.DataFim)
                    .Select(h => new { h.Chegada, h.Partida }).FirstOrDefault(),
                d.Circulacao.Chegada,
                d.Circulacao.Partida,
                d.Circulacao.TempoEmbarqueMinutos
            }).ToListAsync(ct);

        var permanencias = passagens.Select(p => Permanencia(p.Data,
                p.Periodo is { } h ? h.Chegada : p.Chegada, p.Periodo is { } h2 ? h2.Partida : p.Partida,
                p.TempoEmbarqueMinutos ?? PermanenciaPorOmissao))
            .OfType<PermanenciaTic>().ToList();
        return Intervalos(dia, permanencias);
    }

    /// <summary>Partida antes da chegada = partida no dia seguinte. Só chegada ou só partida: fica o tempo de embarque/desembarque.</summary>
    public static PermanenciaTic? Permanencia(DateOnly data, TimeOnly? chegada, TimeOnly? partida, int minutosEmbarque)
    {
        if (chegada is null && partida is null) return null;
        var inicio = chegada is { } c ? data.ToDateTime(c) : data.ToDateTime(partida!.Value).AddMinutes(-minutosEmbarque);
        var fim = partida is { } p ? data.ToDateTime(p) : inicio.AddMinutes(minutosEmbarque);
        if (fim < inicio) fim = fim.AddDays(1);
        return new PermanenciaTic(inicio, fim, chegada is not null, partida is not null);
    }

    /// <summary>
    /// Para cada intervalo [t, t+15) do dia: veículos presentes em algum momento do intervalo
    /// (chegaram antes do fim do intervalo e ainda não tinham partido no início), chegadas e partidas.
    /// </summary>
    public static List<OcupacaoIntervalo> Intervalos(DateOnly dia, IReadOnlyCollection<PermanenciaTic> permanencias)
    {
        var lista = new List<OcupacaoIntervalo>();
        for (var t = dia.ToDateTime(TimeOnly.MinValue); t < dia.AddDays(1).ToDateTime(TimeOnly.MinValue); t = t.AddMinutes(MinutosIntervalo))
        {
            var fim = t.AddMinutes(MinutosIntervalo);
            lista.Add(new OcupacaoIntervalo(t.ToString("HH:mm"),
                permanencias.Count(p => p.Inicio < fim && (p.Fim > t || p.Fim == p.Inicio && p.Inicio >= t)),
                permanencias.Count(p => p.Chega && p.Inicio >= t && p.Inicio < fim),
                permanencias.Count(p => p.Parte && p.Fim >= t && p.Fim < fim)));
        }
        return lista;
    }
}
