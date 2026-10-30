using AppTIC.Web.Models.Terminal;

namespace AppTIC.Web.Services;

public interface ITerminalStateStore
{
    IReadOnlyList<RegistoCais> ObterAtivos();
    ResultadoChegada RegistarChegada(NovaChegadaRequest pedido);
    bool MarcarAtracado(string id);
    bool MarcarPartida(string id);
}

public record ResultadoChegada(bool Sucesso, IReadOnlyList<AlocacaoResposta>? Alocacoes, string? Erro);

public class InMemoryTerminalStateStore(ICaisService solver) : ITerminalStateStore
{
    private readonly object _sync = new();
    private readonly Dictionary<string, RegistoCais> _autocarros = new(StringComparer.Ordinal);

    public IReadOnlyList<RegistoCais> ObterAtivos()
    {
        lock (_sync)
            return _autocarros.Values.Where(a => a.Estado != EstadoAutocarro.Concluido).ToList();
    }

    public ResultadoChegada RegistarChegada(NovaChegadaRequest pedido)
    {
        lock (_sync)
        {
            if (_autocarros.ContainsKey(pedido.Id))
                return new(false, null, "Já existe um autocarro com este identificador.");

            var novo = new RegistoCais(pedido.Id, pedido.Zona, pedido.ChegadaMinutos,
                pedido.PartidaMinutos, null, EstadoAutocarro.Agendado);
            var ativos = _autocarros.Values.Where(a => a.Estado != EstadoAutocarro.Concluido)
                .Append(novo)
                .ToList();
            var pedidos = ativos.Select(a => new AutocarroDto(a.AutocarroId, a.Zona,
                a.ChegadaMinutos, a.PartidaMinutos,
                a.Estado == EstadoAutocarro.Atracado ? a.CaisAlocado : null)).ToList();
            var alocacoes = solver.AlocarCais(pedidos);
            if (alocacoes is null)
                return new(false, null, "Inviável alocar cais para a chegada solicitada sem violar janelas temporais.");

            _autocarros.Add(novo.AutocarroId, novo);
            foreach (var alocacao in alocacoes)
                _autocarros[alocacao.AutocarroId] = _autocarros[alocacao.AutocarroId] with { CaisAlocado = alocacao.Cais };
            return new(true, alocacoes, null);
        }
    }

    public bool MarcarAtracado(string id)
    {
        lock (_sync)
        {
            if (!_autocarros.TryGetValue(id, out var autocarro) ||
                autocarro.Estado != EstadoAutocarro.Agendado || !autocarro.CaisAlocado.HasValue)
                return false;
            _autocarros[id] = autocarro with { Estado = EstadoAutocarro.Atracado };
            return true;
        }
    }

    public bool MarcarPartida(string id)
    {
        lock (_sync)
        {
            if (!_autocarros.TryGetValue(id, out var autocarro) || autocarro.Estado == EstadoAutocarro.Concluido)
                return false;
            _autocarros[id] = autocarro with { Estado = EstadoAutocarro.Concluido };
            return true;
        }
    }
}
