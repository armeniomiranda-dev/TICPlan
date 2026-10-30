using AppTIC.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace AppTIC.Web.Services;

/// <summary>
/// Intervalo de 15 minutos: veículos no terminal (chegaram e ainda não partiram), chegadas e partidas.
/// Os veículos no terminal dividem-se por tipo de viagem: Origem (o TIC é o início), Destino (o TIC é o fim) e Passagem.
/// </summary>
public record OcupacaoIntervalo(string Hora, int Veiculos, int Chegadas, int Partidas, int Origem, int Destino, int Passagem);

/// <summary>Ocupação de um dia e circulações no TIC nesse dia por tipo de viagem.</summary>
public record OcupacaoDia(List<OcupacaoIntervalo> Intervalos, int Origem, int Destino, int Passagem);

/// <summary>
/// Destino das viagens que partem do TIC (início ou passagem) e número de viagens no período.
/// OutrosNomes: nomes das outras paragens juntadas neste destino.
/// </summary>
public record DestinoFrequente(string Nome, int Viagens, List<string> OutrosNomes);

/// <summary>Paragem de destino com as viagens do período que lá terminam.</summary>
public record ParagemDestino(string Nome, decimal? Latitude, decimal? Longitude, int Viagens);

/// <summary>
/// Viagens (circulação × dia em que se realiza) no período [De, Ate] por tipo de viagem no TIC
/// e destinos mais frequentes das que partem do TIC.
/// </summary>
public record ResumoPeriodo(DateOnly De, DateOnly Ate, int Origem, int Destino, int Passagem, int PartemDoTic, List<DestinoFrequente> Destinos);

/// <summary>Veículo no TIC entre Inicio e Fim.</summary>
public record PermanenciaTic(DateTime Inicio, DateTime Fim, bool Chega, bool Parte)
{
    public bool Origem => Parte && !Chega;
    public bool Destino => Chega && !Parte;
    public bool Passagem => Chega && Parte;
}

/// <summary>Ocupação do Terminal de Campanhã ao longo de um dia, em intervalos de 15 minutos.</summary>
public class OcupacaoTerminal(ApplicationDbContext db, IConfiguration config)
{
    public const int MinutosIntervalo = 15;

    /// <summary>Permanência quando o TIC é início ou fim de viagem e a circulação não indica o tempo de embarque/desembarque.</summary>
    private int PermanenciaPorOmissao => config.GetValue("Terminal:PermanenciaPorOmissaoMinutos", 10);

    /// <summary>Paragens de destino a esta distância (ou com o mesmo nome) contam como o mesmo destino.</summary>
    private double RaioDestinoMetros => config.GetValue("Terminal:RaioDestinoMetros", 500.0);

    public async Task<OcupacaoDia> DoDiaAsync(DateOnly dia, CancellationToken ct = default)
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

        // Circulações com presença ou partida no dia (as do dia anterior só se ainda estiverem no TIC).
        var inicioDia = dia.ToDateTime(TimeOnly.MinValue);
        var noDia = permanencias.Where(p => p.Inicio < inicioDia.AddDays(1) && p.Fim >= inicioDia).ToList();
        return new OcupacaoDia(Intervalos(dia, permanencias),
            noDia.Count(p => p.Origem), noDia.Count(p => p.Destino), noDia.Count(p => p.Passagem));
    }

    /// <summary>
    /// Conta cada dia em que a circulação se realiza, com as horas no TIC do período de HorariosTic desse dia
    /// (ou as da circulação, se nenhum se aplicar). As viagens que terminam no TIC não entram nos destinos.
    /// </summary>
    public async Task<ResumoPeriodo> DoPeriodoAsync(DateOnly de, DateOnly ate, int maxDestinos = 10, CancellationToken ct = default)
    {
        var grupos = await db.CirculacaoDias
            .Where(d => d.Data >= de && d.Data <= ate)
            .Select(d => new
            {
                Chega = d.Circulacao!.HorariosTic.Where(h => h.DataInicio <= d.Data && d.Data <= h.DataFim)
                    .Select(h => (bool?)(h.Chegada != null)).FirstOrDefault() ?? d.Circulacao.Chegada != null,
                Parte = d.Circulacao.HorariosTic.Where(h => h.DataInicio <= d.Data && d.Data <= h.DataFim)
                    .Select(h => (bool?)(h.Partida != null)).FirstOrDefault() ?? d.Circulacao.Partida != null,
                d.Circulacao.DestinoId
            })
            .GroupBy(x => new { x.Chega, x.Parte, x.DestinoId })
            .Select(g => new { g.Key.Chega, g.Key.Parte, g.Key.DestinoId, N = g.Count() })
            .ToListAsync(ct);

        var partem = grupos.Where(g => g.Parte).ToList();
        var viagensPorParagem = partem.Where(g => g.DestinoId != null)
            .GroupBy(g => g.DestinoId!.Value).ToDictionary(g => g.Key, g => g.Sum(x => x.N));
        var ids = viagensPorParagem.Keys.ToList();
        var paragens = await db.Paragens.Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, p.Nome, p.Latitude, p.Longitude }).ToListAsync(ct);
        var destinos = AgruparDestinos(
                paragens.Select(p => new ParagemDestino(p.Nome.Trim(), p.Latitude, p.Longitude, viagensPorParagem[p.Id])).ToList(),
                RaioDestinoMetros)
            .OrderByDescending(d => d.Viagens).ThenBy(d => d.Nome).Take(maxDestinos).ToList();
        return new ResumoPeriodo(de, ate,
            grupos.Where(g => g.Parte && !g.Chega).Sum(g => g.N),
            grupos.Where(g => g.Chega && !g.Parte).Sum(g => g.N),
            grupos.Where(g => g.Chega && g.Parte).Sum(g => g.N),
            partem.Sum(g => g.N), destinos);
    }

    /// <summary>
    /// Junta as paragens de destino que são o mesmo sítio com nomes diferentes (operadores distintos):
    /// mesmo nome, ou até <paramref name="raioMetros"/> uma da outra. O grupo fica com o nome com mais viagens.
    /// </summary>
    public static List<DestinoFrequente> AgruparDestinos(IReadOnlyList<ParagemDestino> paragens, double raioMetros)
    {
        var pai = Enumerable.Range(0, paragens.Count).ToArray();
        int Raiz(int i) => pai[i] == i ? i : pai[i] = Raiz(pai[i]);
        for (var i = 0; i < paragens.Count; i++)
            for (var j = i + 1; j < paragens.Count; j++)
                if (string.Equals(paragens[i].Nome, paragens[j].Nome, StringComparison.CurrentCultureIgnoreCase)
                    || DistanciaMetros(paragens[i], paragens[j]) <= raioMetros)
                    pai[Raiz(j)] = Raiz(i);

        return paragens.Select((p, i) => (p, Grupo: Raiz(i))).GroupBy(x => x.Grupo)
            .Select(g =>
            {
                var nomes = g.GroupBy(x => x.p.Nome, StringComparer.CurrentCultureIgnoreCase)
                    .Select(n => (Nome: n.Key, Viagens: n.Sum(x => x.p.Viagens)))
                    .OrderByDescending(n => n.Viagens).ThenBy(n => n.Nome).Select(n => n.Nome).ToList();
                return new DestinoFrequente(nomes[0], g.Sum(x => x.p.Viagens), nomes.Skip(1).ToList());
            }).ToList();
    }

    /// <summary>Distância na superfície da Terra (haversine); infinita se faltar alguma coordenada.</summary>
    private static double DistanciaMetros(ParagemDestino a, ParagemDestino b)
    {
        if (a.Latitude is null || a.Longitude is null || b.Latitude is null || b.Longitude is null) return double.PositiveInfinity;
        static double Rad(decimal g) => (double)g * Math.PI / 180;
        var dLat = Rad(b.Latitude.Value - a.Latitude.Value);
        var dLon = Rad(b.Longitude.Value - a.Longitude.Value);
        var h = Math.Pow(Math.Sin(dLat / 2), 2) + Math.Cos(Rad(a.Latitude.Value)) * Math.Cos(Rad(b.Latitude.Value)) * Math.Pow(Math.Sin(dLon / 2), 2);
        return 2 * 6_371_000 * Math.Asin(Math.Sqrt(h));
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
            var presentes = permanencias.Where(p => p.Inicio < fim && (p.Fim > t || p.Fim == p.Inicio && p.Inicio >= t)).ToList();
            lista.Add(new OcupacaoIntervalo(t.ToString("HH:mm"),
                presentes.Count,
                permanencias.Count(p => p.Chega && p.Inicio >= t && p.Inicio < fim),
                permanencias.Count(p => p.Parte && p.Fim >= t && p.Fim < fim),
                presentes.Count(p => p.Origem), presentes.Count(p => p.Destino), presentes.Count(p => p.Passagem)));
        }
        return lista;
    }
}
