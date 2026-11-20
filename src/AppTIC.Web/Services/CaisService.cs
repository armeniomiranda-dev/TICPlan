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

    /// <summary>
    /// Planeamento do dia com categorias configuráveis (por região ou por tempo de permanência):
    /// primeiro o maior número de autocarros com cais, depois o menor recurso a cais de reserva ou de outra
    /// categoria e, por fim, a posição preferida de cada categoria em relação à saída.
    /// </summary>
    PlaneamentoDia? PlanearDia(PlaneamentoPedido pedido);
}

public class CaisService : ICaisService
{
    public const int TotalCais = 17;

    /// <summary>Cais do mais próximo ao mais distante da saída, segundo a planta da simulação.</summary>
    public static readonly IReadOnlyList<int> OrdemSaidaPorOmissao = [9, 10, 8, 11, 7, 12, 13, 6, 14, 5, 15, 16, 4, 17, 3, 2, 1];

    /// <summary>Zonas por região: os cais comuns (11–17) são da categoria Comum e reserva de qualquer zona lotada.</summary>
    private static readonly IReadOnlyList<CategoriaCaisDto> CategoriasRegionais =
    [
        new(nameof(ZonaCais.Lisboa), [1, 2, 3, 4]),
        new(nameof(ZonaCais.Internacional), [5, 6, 7, 8]),
        new(nameof(ZonaCais.Norte), [9, 10]),
        new(nameof(ZonaCais.Comum), [11, 12, 13, 14, 15, 16, 17], Reserva: true)
    ];

    private sealed record Escolha(int Indice, AutocarroPlanoDto Autocarro, int Cais, long Custo, BoolVar Escolhido)
    {
        public string AutocarroId => Autocarro.AutocarroId;
    }

    public IReadOnlyList<AlocacaoResposta>? AlocarCais(IReadOnlyList<AutocarroDto> autocarros)
    {
        // 1. Criar o modelo / 2. Definir as variáveis / 3. Adicionar restrições (ver Construir)
        var (model, escolhas) = Construir(Regional(autocarros), obrigatorio: true);

        // 4. Definir o objetivo: min Σ custo·x — os cais comuns só se usam quando os da zona estão ocupados.
        model.Minimize(LinearExpr.WeightedSum(escolhas.Select(e => (ILiteral)e.Escolhido), escolhas.Select(e => e.Custo)));

        // 5. Criar o solver e resolver o modelo
        var solver = new CpSolver();
        var status = solver.Solve(model);
        if (status == CpSolverStatus.Infeasible)
            return null;
        if (status is not (CpSolverStatus.Optimal or CpSolverStatus.Feasible))
            throw new InvalidOperationException($"O resolvedor não conseguiu concluir a alocação: {status}.");

        return escolhas.Where(e => solver.BooleanValue(e.Escolhido))
            .Select(e => new AlocacaoResposta(e.AutocarroId, e.Cais))
            .ToList();
    }

    public PlaneamentoDia? PlanearDia(IReadOnlyList<AutocarroDto> autocarros) => PlanearDia(Regional(autocarros));

    public PlaneamentoDia? PlanearDia(PlaneamentoPedido pedido)
    {
        // 1. Criar o modelo / 2. Definir as variáveis / 3. Adicionar restrições (ver Construir)
        var (model, escolhas) = Construir(pedido, obrigatorio: false);
        var literais = escolhas.Select(e => (ILiteral)e.Escolhido).ToList();

        // 4. Definir o objetivo (1.ª fase): max Σ x — o maior número possível de autocarros com cais.
        model.Maximize(LinearExpr.Sum(literais));

        // 5. Criar o solver e resolver o modelo (1.ª fase)
        var solver = new CpSolver { StringParameters = "max_time_in_seconds:10 num_workers:8" };
        var status = solver.Solve(model);
        if (status == CpSolverStatus.Infeasible)
            return null;
        if (status is not (CpSolverStatus.Optimal or CpSolverStatus.Feasible))
            throw new InvalidOperationException($"O resolvedor não conseguiu concluir o planeamento: {status}.");
        var escolhidos = escolhas.Select(e => solver.BooleanValue(e.Escolhido)).ToList();

        // 2.ª fase: sem perder nenhum, o menor custo (reserva, outra categoria e posição face à saída).
        // Parte de uma atribuição gulosa (por ordem de chegada, o cais livre mais barato), que o resolvedor melhora.
        if (escolhas.Any(e => e.Custo > 0))
        {
            long Custo(IReadOnlyList<bool> solucao) => escolhas.Where((_, i) => solucao[i]).Sum(e => e.Custo);
            var comCaisNaFase1 = escolhidos.Count(e => e);
            var gulosa = PartidaGulosa(escolhas, escolhidos);
            if (gulosa.Count(e => e) == comCaisNaFase1 && Custo(gulosa) <= Custo(escolhidos))
                escolhidos = gulosa;

            // 3. Adicionar restrições (2.ª fase): Σ x ≥ N* — não perder nenhum autocarro com cais da 1.ª fase.
            model.Add(LinearExpr.Sum(literais) >= comCaisNaFase1);
            // Solução inicial (hint) para acelerar o resolvedor.
            for (var i = 0; i < escolhas.Count; i++)
                model.AddHint(escolhas[i].Escolhido, escolhidos[i] ? 1 : 0);

            // 4. Definir o objetivo (2.ª fase): min Σ custo·x — substitui o objetivo da 1.ª fase.
            model.Minimize(LinearExpr.WeightedSum(literais, escolhas.Select(e => e.Custo)));

            // 5. Criar o solver e resolver o modelo (2.ª fase)
            var segunda = new CpSolver { StringParameters = "max_time_in_seconds:6 num_workers:8" };
            var statusCusto = segunda.Solve(model);
            if (statusCusto is CpSolverStatus.Optimal or CpSolverStatus.Feasible)
            {
                var melhorada = escolhas.Select(e => segunda.BooleanValue(e.Escolhido)).ToList();
                if (Custo(melhorada) <= Custo(escolhidos)) escolhidos = melhorada;
            }
            if (statusCusto != CpSolverStatus.Optimal) status = CpSolverStatus.Feasible;
        }

        var alocacoes = escolhas.Where((_, i) => escolhidos[i])
            .Select(e => new AlocacaoResposta(e.AutocarroId, e.Cais))
            .ToList();
        var comCais = alocacoes.Select(a => a.AutocarroId).ToHashSet(StringComparer.Ordinal);
        return new PlaneamentoDia(
            alocacoes,
            pedido.Autocarros.Where(a => !comCais.Contains(a.AutocarroId)).Select(a => a.AutocarroId).ToList(),
            status == CpSolverStatus.Optimal);
    }

    /// <summary>
    /// Solução inicial da 2.ª fase: os autocarros com cais na 1.ª fase, por ordem de chegada, ficam com o cais livre
    /// de menor custo; os de cais fixo mantêm-no. Pode deixar algum por atribuir quando as categorias são restritas.
    /// </summary>
    private static List<bool> PartidaGulosa(List<Escolha> escolhas, IReadOnlyList<bool> escolhidos)
    {
        var ocupacao = Enumerable.Range(0, TotalCais + 1).Select(_ => new List<AutocarroPlanoDto>()).ToArray();
        var resultado = new bool[escolhas.Count];
        bool Livre(Escolha e) => ocupacao[e.Cais].All(o =>
            o.PartidaMinutos <= e.Autocarro.ChegadaMinutos || e.Autocarro.PartidaMinutos <= o.ChegadaMinutos);
        void Ocupar(int i)
        {
            resultado[i] = true;
            ocupacao[escolhas[i].Cais].Add(escolhas[i].Autocarro);
        }

        var porAutocarro = Enumerable.Range(0, escolhas.Count).GroupBy(i => escolhas[i].Indice)
            .Where(g => g.Any(i => escolhidos[i]))
            .OrderBy(g => escolhas[g.First()].Autocarro.ChegadaMinutos).ThenBy(g => escolhas[g.First()].Autocarro.PartidaMinutos)
            .ToList();
        foreach (var grupo in porAutocarro.Where(g => escolhas[g.First()].Autocarro.CaisFixo is not null))
            Ocupar(grupo.First(i => escolhidos[i]));
        foreach (var grupo in porAutocarro.Where(g => escolhas[g.First()].Autocarro.CaisFixo is null))
        {
            var livres = grupo.Where(i => Livre(escolhas[i])).ToList();
            if (livres.Count > 0) Ocupar(livres.MinBy(i => escolhas[i].Custo));
        }
        return [.. resultado];
    }

    /// <summary>Erro de validação do pedido, ou null se for válido.</summary>
    public static string? Validar(PlaneamentoPedido pedido)
    {
        if (pedido.Categorias is null || pedido.Autocarros is null)
            return "Indique as categorias e os autocarros a planear.";
        if (pedido.Categorias.Any(c => string.IsNullOrWhiteSpace(c.Id) || c.Cais is null || !Enum.IsDefined(c.Preferencia) ||
                c.Cais.Any(n => n < 1 || n > TotalCais)) ||
            pedido.Categorias.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count() != pedido.Categorias.Count)
            return $"Existem categorias inválidas ou repetidas (os cais vão de 1 a {TotalCais}).";
        if (pedido.OrdemSaida is { } ordem && !ordem.Order().SequenceEqual(Enumerable.Range(1, TotalCais)))
            return $"A ordem de saída tem de conter cada cais de 1 a {TotalCais} uma única vez.";

        var categorias = pedido.Categorias.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        if (pedido.Autocarros.Any(a => string.IsNullOrWhiteSpace(a.AutocarroId) || a.Categoria is null ||
                !categorias.Contains(a.Categoria) || a.ChegadaMinutos >= a.PartidaMinutos) ||
            pedido.Autocarros.Select(a => a.AutocarroId).Distinct(StringComparer.Ordinal).Count() != pedido.Autocarros.Count)
            return "Existem pedidos inválidos ou repetidos no planeamento do dia.";
        return null;
    }

    private static PlaneamentoPedido Regional(IReadOnlyList<AutocarroDto> autocarros)
    {
        if (autocarros.Any(a => !Enum.IsDefined(a.Zona)))
            throw new ArgumentOutOfRangeException(nameof(autocarros), "Zona de cais inválida.");
        return new PlaneamentoPedido(
            CategoriasRegionais,
            autocarros.Select(a => new AutocarroPlanoDto(a.AutocarroId, a.Zona.ToString(), a.ChegadaMinutos, a.PartidaMinutos, a.CaisFixo)).ToList());
    }

    /// <summary>
    /// Custo de cada opção: o nível (0 cais da categoria, 1 reserva, 2 outra categoria em último recurso) e, dentro
    /// do nível, a distância à posição preferida (junto à saída ou no fundo).
    /// </summary>
    private static (CpModel Model, List<Escolha> Escolhas) Construir(PlaneamentoPedido pedido, bool obrigatorio)
    {
        // 1. Criar o modelo
        var model = new CpModel();
        var intervalos = Enumerable.Range(1, TotalCais).ToDictionary(c => c, _ => new List<IntervalVar>());
        var escolhas = new List<Escolha>();

        var categorias = pedido.Categorias.ToDictionary(c => c.Id, StringComparer.Ordinal);
        var caisReserva = pedido.Categorias.Where(c => c.Reserva).SelectMany(c => c.Cais).ToHashSet();
        var posicaoSaida = (pedido.OrdemSaida ?? OrdemSaidaPorOmissao)
            .Select((cais, posicao) => (cais, posicao)).ToDictionary(p => p.cais, p => (long)p.posicao);

        // Subir um nível pesa sempre mais do que qualquer diferença de posição dentro do nível.
        long pesoNivel = pedido.Categorias.Any(c => c.Preferencia != PreferenciaCais.Nenhuma) ? TotalCais + 3 : 1;

        for (var indice = 0; indice < pedido.Autocarros.Count; indice++)
        {
            var autocarro = pedido.Autocarros[indice];
            if (!categorias.TryGetValue(autocarro.Categoria, out var categoria))
                throw new ArgumentException($"Categoria de cais desconhecida: {autocarro.Categoria}.", nameof(pedido));
            var caisPrimarios = categoria.Cais.ToHashSet();

            var opcoes = new List<BoolVar>();
            for (var cais = 1; cais <= TotalCais; cais++)
            {
                // Uma categoria lotada pode usar os cais de reserva; os de outra categoria só em último recurso.
                var nivel = caisPrimarios.Contains(cais) ? 0 : caisReserva.Contains(cais) ? 1 : pedido.UltimoRecurso ? 2 : -1;
                if (nivel < 0) continue;
                var preferencia = categoria.Preferencia switch
                {
                    PreferenciaCais.Saida => posicaoSaida[cais],
                    PreferenciaCais.Fundo => TotalCais - 1 - posicaoSaida[cais],
                    _ => 0
                };

                // 2. Definir as variáveis
                // x[a,c] ∈ {0,1}: 1 se o autocarro a fica no cais c.
                var escolhido = model.NewBoolVar($"autocarro_{escolhas.Count}_cais_{cais}");
                // Intervalo [chegada, partida) no cais c, que só existe quando x[a,c] = 1.
                var intervalo = model.NewOptionalFixedSizeIntervalVar(
                    autocarro.ChegadaMinutos,
                    (long)autocarro.PartidaMinutos - autocarro.ChegadaMinutos,
                    escolhido,
                    $"intervalo_{escolhas.Count}");
                opcoes.Add(escolhido);
                intervalos[cais].Add(intervalo);
                escolhas.Add(new Escolha(indice, autocarro, cais, nivel * pesoNivel + preferencia, escolhido));

                // 3. Adicionar restrições — cais fixo: x[a,c] = 0 para qualquer c diferente do fixo.
                if (autocarro.CaisFixo is { } fixo && cais != fixo)
                    model.Add(escolhido == 0);
            }

            // 3. Adicionar restrições — um cais por autocarro:
            //    obrigatório ou cais fixo (já atracado): Σ_c x[a,c] = 1; caso contrário: Σ_c x[a,c] ≤ 1.
            if (obrigatorio || autocarro.CaisFixo is not null)
            {
                if (opcoes.Count == 0)
                    model.Add(model.NewConstant(0) == 1); // sem cais possíveis: inviável
                else
                    model.AddExactlyOne(opcoes);
            }
            else if (opcoes.Count > 1)
                model.AddAtMostOne(opcoes);
        }

        // 3. Adicionar restrições — sem sobreposição: dois autocarros não ocupam o mesmo cais ao mesmo tempo.
        foreach (var intervalosCais in intervalos.Values)
            if (intervalosCais.Count > 1)
                model.AddNoOverlap(intervalosCais);

        return (model, escolhas);
    }
}
