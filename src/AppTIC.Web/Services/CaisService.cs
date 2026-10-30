using AppTIC.Web.Models.Terminal;
using Google.OrTools.Sat;

namespace AppTIC.Web.Services;

public interface ICaisService
{
    IReadOnlyList<AlocacaoResposta>? AlocarCais(IReadOnlyList<AutocarroDto> autocarros);
}

public class CaisService : ICaisService
{
    private static readonly int[] ReservaComum = [9, 10];

    public IReadOnlyList<AlocacaoResposta>? AlocarCais(IReadOnlyList<AutocarroDto> autocarros)
    {
        var model = new CpModel();
        var intervalos = Enumerable.Range(1, 17).ToDictionary(c => c, _ => new List<IntervalVar>());
        var escolhas = new List<(AutocarroDto Autocarro, int Cais, BoolVar Escolhido)>();

        foreach (var autocarro in autocarros)
        {
            var caisPrimarios = autocarro.Zona switch
            {
                ZonaCais.Lisboa => Enumerable.Range(1, 4),
                ZonaCais.Fora => Enumerable.Range(5, 4),
                ZonaCais.Norte => Enumerable.Range(9, 2),
                ZonaCais.Outros => Enumerable.Range(11, 7),
                _ => throw new ArgumentOutOfRangeException(nameof(autocarros), "Zona de cais inválida.")
            };

            var caisDisponiveis = autocarro.Zona == ZonaCais.Norte
                ? caisPrimarios
                : caisPrimarios.Concat(ReservaComum).Distinct();

            var opcoes = new List<BoolVar>();
            foreach (var cais in caisDisponiveis)
            {
                var escolhido = model.NewBoolVar($"autocarro_{escolhas.Count}_cais_{cais}");
                var intervalo = model.NewOptionalFixedSizeIntervalVar(
                    autocarro.ChegadaMinutos,
                    (long)autocarro.PartidaMinutos - autocarro.ChegadaMinutos,
                    escolhido,
                    $"intervalo_{escolhas.Count}");
                opcoes.Add(escolhido);
                intervalos[cais].Add(intervalo);
                escolhas.Add((autocarro, cais, escolhido));
                if (autocarro.CaisFixo is { } fixo && cais != fixo)
                    model.Add(escolhido == 0);
            }
            model.AddExactlyOne(opcoes);
        }

        foreach (var intervalosCais in intervalos.Values)
            if (intervalosCais.Count > 1)
                model.AddNoOverlap(intervalosCais);

        var solver = new CpSolver();
        var status = solver.Solve(model);
        if (status == CpSolverStatus.Infeasible)
            return null;
        if (status is not (CpSolverStatus.Optimal or CpSolverStatus.Feasible))
            throw new InvalidOperationException($"O resolvedor não conseguiu concluir a alocação: {status}.");

        return escolhas.Where(e => solver.BooleanValue(e.Escolhido))
            .Select(e => new AlocacaoResposta(e.Autocarro.AutocarroId, e.Cais))
            .ToList();
    }
}
