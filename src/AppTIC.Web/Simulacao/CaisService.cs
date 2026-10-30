using Google.OrTools.Sat;

namespace TerminalIntermodal.Api;

public enum ZonaCais { Lisboa, Fora, Norte, Superior }

public record AutocarroDto(string Id, ZonaCais Zona, int ChegadaMinutos, int PartidaMinutos, int? CaisFixo = null);
public record AlocacaoResposta(string AutocarroId, int Cais, ZonaCais Zona, int ChegadaMinutos, int PartidaMinutos);

public interface ICaisService
{
    List<AlocacaoResposta>? AlocarCais(IReadOnlyList<AutocarroDto> autocarros);
}

public class CaisService : ICaisService
{
    private const int BufferMinutos = 5;

    public static readonly Dictionary<ZonaCais, int[]> CaisPorZona = new()
    {
        { ZonaCais.Lisboa, [1, 2, 3, 4] },
        { ZonaCais.Fora,   [5, 6, 7, 8] },
        { ZonaCais.Norte,  [9, 10] },
        { ZonaCais.Superior, [11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28] }
    };

    private static readonly int[] TodosCais = Enumerable.Range(1, 28).ToArray();

    public List<AlocacaoResposta>? AlocarCais(IReadOnlyList<AutocarroDto> autocarros)
    {
        var model = new CpModel();
        var x = new Dictionary<(string BusId, int Cais), BoolVar>();
        var intervalosPorCais = TodosCais.ToDictionary(c => c, _ => new List<IntervalVar>());

        foreach (var b in autocarros)
        {
            var caisPermitidos = CaisPorZona[b.Zona];
            var varsDoAutocarro = new List<BoolVar>();
            int duracao = (b.PartidaMinutos - b.ChegadaMinutos) + BufferMinutos;

            foreach (var cais in caisPermitidos)
            {
                var presenca = model.NewBoolVar($"x_{b.Id}_cais_{cais}");
                x[(b.Id, cais)] = presenca;
                varsDoAutocarro.Add(presenca);

                var intervalo = model.NewOptionalIntervalVar(
                    start: b.ChegadaMinutos,
                    size: duracao,
                    end: b.ChegadaMinutos + duracao,
                    is_present: presenca,
                    name: $"int_{b.Id}_{cais}"
                );

                intervalosPorCais[cais].Add(intervalo);

                if (b.CaisFixo.HasValue && b.CaisFixo.Value == cais)
                    model.Add(presenca == 1);
            }

            model.AddExactlyOne(varsDoAutocarro);
        }

        foreach (var cais in TodosCais)
        {
            model.AddNoOverlap(intervalosPorCais[cais]);
        }

        var termosObjetivo = autocarros.SelectMany(b => CaisPorZona[b.Zona].Select(c => x[(b.Id, c)] * c));
        model.Minimize(LinearExpr.Sum(termosObjetivo));

        var solver = new CpSolver { StringParameters = "max_time_in_seconds:2.0" };
        var status = solver.Solve(model);

        if (status is not (CpSolverStatus.Optimal or CpSolverStatus.Feasible))
            return null;

        var resultados = new List<AlocacaoResposta>();
        foreach (var b in autocarros)
        {
            foreach (var cais in CaisPorZona[b.Zona])
            {
                if (solver.Value(x[(b.Id, cais)]) == 1)
                {
                    resultados.Add(new AlocacaoResposta(b.Id, cais, b.Zona, b.ChegadaMinutos, b.PartidaMinutos));
                    break;
                }
            }
        }

        return resultados;
    }
}