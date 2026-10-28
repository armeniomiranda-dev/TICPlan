using AppTIC.Web.Models.Domain;

namespace AppTIC.Web.Services;

/// <summary>Histórico das horas de uma circulação no TIC (períodos de Circulacao.HorariosTic, sem sobreposição).</summary>
public static class HorariosTic
{
    /// <summary>
    /// Define as horas no TIC entre inicio e fim: os períodos que se sobrepõem são encurtados, divididos ou removidos.
    /// Com manterManuais, os dias cobertos por períodos manuais ficam como estão (usado na importação do GTFS).
    /// Atualiza Chegada/Partida da circulação com o período em vigor.
    /// </summary>
    public static void Definir(Circulacao c, DateOnly inicio, DateOnly fim, TimeOnly? chegada, TimeOnly? partida,
        OrigemHorario origem, DateOnly hoje, bool manterManuais = false)
    {
        var intervalos = manterManuais
            ? Lacunas(inicio, fim, c.HorariosTic.Where(p => p.Origem == OrigemHorario.Manual).ToList())
            : [(inicio, fim)];
        foreach (var (de, ate) in intervalos)
        {
            Recortar(c.HorariosTic, de, ate);
            c.HorariosTic.Add(new CirculacaoHorarioTic
            {
                DataInicio = de, DataFim = ate, Chegada = chegada, Partida = partida, Origem = origem
            });
        }
        Juntar(c.HorariosTic);
        AtualizarVigente(c, hoje);
    }

    /// <summary>Remove um período do histórico e atualiza as horas em vigor.</summary>
    public static void Remover(Circulacao c, CirculacaoHorarioTic periodo, DateOnly hoje)
    {
        c.HorariosTic.Remove(periodo);
        AtualizarVigente(c, hoje);
    }

    /// <summary>Período que se aplica a uma data (ou null).</summary>
    public static CirculacaoHorarioTic? Em(IEnumerable<CirculacaoHorarioTic> periodos, DateOnly data) =>
        periodos.FirstOrDefault(p => p.DataInicio <= data && data <= p.DataFim);

    /// <summary>Chegada/Partida da circulação = período de hoje; senão o próximo; senão o último.</summary>
    public static void AtualizarVigente(Circulacao c, DateOnly hoje)
    {
        var p = Em(c.HorariosTic, hoje)
            ?? c.HorariosTic.Where(x => x.DataInicio > hoje).MinBy(x => x.DataInicio)
            ?? c.HorariosTic.MaxBy(x => x.DataFim);
        if (p is null) return;
        c.Chegada = p.Chegada;
        c.Partida = p.Partida;
    }

    private static void Recortar(List<CirculacaoHorarioTic> lista, DateOnly de, DateOnly ate)
    {
        foreach (var p in lista.Where(p => p.DataInicio <= ate && p.DataFim >= de).ToList())
        {
            if (p.DataInicio < de && p.DataFim > ate)
            {
                lista.Add(new CirculacaoHorarioTic
                {
                    DataInicio = ate.AddDays(1), DataFim = p.DataFim, Chegada = p.Chegada, Partida = p.Partida,
                    Origem = p.Origem, RegistadoEm = p.RegistadoEm
                });
                p.DataFim = de.AddDays(-1);
            }
            else if (p.DataInicio < de) p.DataFim = de.AddDays(-1);
            else if (p.DataFim > ate) p.DataInicio = ate.AddDays(1);
            else lista.Remove(p);
        }
    }

    /// <summary>Junta períodos contíguos com as mesmas horas e a mesma origem.</summary>
    private static void Juntar(List<CirculacaoHorarioTic> lista)
    {
        var ordenados = lista.OrderBy(p => p.DataInicio).ToList();
        for (var i = 1; i < ordenados.Count; i++)
        {
            var (a, b) = (ordenados[i - 1], ordenados[i]);
            if (a.DataFim.AddDays(1) != b.DataInicio || a.Chegada != b.Chegada || a.Partida != b.Partida || a.Origem != b.Origem) continue;
            a.DataFim = b.DataFim;
            lista.Remove(b);
            ordenados.RemoveAt(i--);
        }
    }

    /// <summary>Partes de [de, ate] não cobertas pelos períodos indicados.</summary>
    private static List<(DateOnly, DateOnly)> Lacunas(DateOnly de, DateOnly ate, List<CirculacaoHorarioTic> ocupados)
    {
        var lacunas = new List<(DateOnly, DateOnly)>();
        var cursor = de;
        foreach (var p in ocupados.Where(p => p.DataFim >= de && p.DataInicio <= ate).OrderBy(p => p.DataInicio))
        {
            if (p.DataInicio > cursor) lacunas.Add((cursor, p.DataInicio.AddDays(-1)));
            if (p.DataFim >= cursor) cursor = p.DataFim.AddDays(1);
        }
        if (cursor <= ate) lacunas.Add((cursor, ate));
        return lacunas;
    }
}
