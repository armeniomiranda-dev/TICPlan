using AppTIC.Web.Models.Terminal;
using Google.OrTools.Sat;

namespace AppTIC.Web.Services;

public interface ICaisService
{
    IReadOnlyList<AlocacaoResposta>? AlocarCais(IReadOnlyList<AutocarroDto> autocarros);

    /// <summary>
    /// Planeamento de um dia inteiro: maximiza os autocarros com cais (preferindo os cais da própria zona)
    /// e devolve os que não cabem, em vez de falhar. Null só quando os cais fixos são incompatíveis.
    /// </summary>
    PlaneamentoDia? PlanearDia(IReadOnlyList<AutocarroDto> autocarros);
}

public class CaisService : ICaisService
{
    /// <summary>Cais comuns (11–17): da categoria Comum e reserva de qualquer zona lotada.</summary>
    private static readonly int[] CaisComuns = [11, 12, 13, 14, 15, 16, 17];

    public IReadOnlyList<AlocacaoResposta>? AlocarCais(IReadOnlyList<AutocarroDto> autocarros)
    {
        var (model, escolhas) = Construir(autocarros, obrigatorio: true);
        // Os cais comuns só se usam quando os da zona estão ocupados.
        model.Minimize(LinearExpr.Sum(escolhas.Where(e => e.Reserva).Select(e => (ILiteral)e.Escolhido)));

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

    public PlaneamentoDia? PlanearDia(IReadOnlyList<AutocarroDto> autocarros)
    {
        var (model, escolhas) = Construir(autocarros, obrigatorio: false);

        // Cada autocarro com cais vale mais do que todas as passagens para a reserva juntas.
        long pesoAtribuido = autocarros.Count + 1;
        model.Maximize(LinearExpr.WeightedSum(
            escolhas.Select(e => (ILiteral)e.Escolhido),
            escolhas.Select(e => e.Reserva ? pesoAtribuido - 1 : pesoAtribuido)));

        var solver = new CpSolver { StringParameters = "max_time_in_seconds:10 num_workers:8" };
        var status = solver.Solve(model);
        if (status == CpSolverStatus.Infeasible)
            return null;
        if (status is not (CpSolverStatus.Optimal or CpSolverStatus.Feasible))
            throw new InvalidOperationException($"O resolvedor não conseguiu concluir o planeamento: {status}.");

        var alocacoes = escolhas.Where(e => solver.BooleanValue(e.Escolhido))
            .Select(e => new AlocacaoResposta(e.Autocarro.AutocarroId, e.Cais))
            .ToList();
        var comCais = alocacoes.Select(a => a.AutocarroId).ToHashSet(StringComparer.Ordinal);
        return new PlaneamentoDia(
            alocacoes,
            autocarros.Where(a => !comCais.Contains(a.AutocarroId)).Select(a => a.AutocarroId).ToList(),
            status == CpSolverStatus.Optimal);
    }

    private static (CpModel Model, List<(AutocarroDto Autocarro, int Cais, bool Reserva, BoolVar Escolhido)> Escolhas)
        Construir(IReadOnlyList<AutocarroDto> autocarros, bool obrigatorio)
    {
        var model = new CpModel();
        var intervalos = Enumerable.Range(1, 17).ToDictionary(c => c, _ => new List<IntervalVar>());
        var escolhas = new List<(AutocarroDto Autocarro, int Cais, bool Reserva, BoolVar Escolhido)>();

        foreach (var autocarro in autocarros)
        {
            var caisPrimarios = autocarro.Zona switch
            {
                ZonaCais.Lisboa => Enumerable.Range(1, 4),
                ZonaCais.Internacional => Enumerable.Range(5, 4),
                ZonaCais.Norte => Enumerable.Range(9, 2),
                ZonaCais.Comum => CaisComuns,
                _ => throw new ArgumentOutOfRangeException(nameof(autocarros), "Zona de cais inválida.")
            };

            // Uma zona lotada pode usar os cais comuns; nunca os cais de outra zona.
            var caisDisponiveis = caisPrimarios.Concat(CaisComuns).Distinct();

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
                escolhas.Add((autocarro, cais, !caisPrimarios.Contains(cais), escolhido));
                if (autocarro.CaisFixo is { } fixo && cais != fixo)
                    model.Add(escolhido == 0);
            }
            // Um autocarro com cais fixo (já atracado) tem sempre de ficar com ele.
            if (obrigatorio || autocarro.CaisFixo is not null)
                model.AddExactlyOne(opcoes);
            else
                model.AddAtMostOne(opcoes);
        }

        foreach (var intervalosCais in intervalos.Values)
            if (intervalosCais.Count > 1)
                model.AddNoOverlap(intervalosCais);

        return (model, escolhas);
    }
}
