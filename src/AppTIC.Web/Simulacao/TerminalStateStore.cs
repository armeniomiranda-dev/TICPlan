namespace TerminalIntermodal.Api;

public enum EstadoAutocarro { Agendado, Atracado, Concluido }

public record RegistoCais(
    string AutocarroId,
    ZonaCais Zona,
    int ChegadaMinutos,
    int PartidaMinutos,
    int? CaisAlocado,
    EstadoAutocarro Estado
);

public interface ITerminalStateStore
{
    IReadOnlyList<RegistoCais> ObterAtivos();
    void AtualizarAlocacoes(IEnumerable<AlocacaoResposta> alocacoes);
    void RegistarChegada(string id, ZonaCais zona, int chegadaMinutos, int partidaMinutos);
    bool MarcarAtracado(string id);
    bool MarcarPartida(string id);
}

public class InMemoryTerminalStateStore : ITerminalStateStore
{
    private readonly ReaderWriterLockSlim _lock = new();
    private readonly Dictionary<string, RegistoCais> _autocarros = new();

    public IReadOnlyList<RegistoCais> ObterAtivos()
    {
        _lock.EnterReadLock();
        try
        {
            return _autocarros.Values.Where(a => a.Estado != EstadoAutocarro.Concluido).ToList();
        }
        finally { _lock.ExitReadLock(); }
    }

    public void RegistarChegada(string id, ZonaCais zona, int chegadaMinutos, int partidaMinutos)
    {
        _lock.EnterWriteLock();
        try
        {
            _autocarros[id] = new RegistoCais(id, zona, chegadaMinutos, partidaMinutos, null, EstadoAutocarro.Agendado);
        }
        finally { _lock.ExitWriteLock(); }
    }

    public void AtualizarAlocacoes(IEnumerable<AlocacaoResposta> alocacoes)
    {
        _lock.EnterWriteLock();
        try
        {
            foreach (var aloc in alocacoes)
            {
                if (_autocarros.TryGetValue(aloc.AutocarroId, out var r))
                {
                    _autocarros[aloc.AutocarroId] = r with { CaisAlocado = aloc.Cais };
                }
            }
        }
        finally { _lock.ExitWriteLock(); }
    }

    public bool MarcarAtracado(string id)
    {
        _lock.EnterWriteLock();
        try
        {
            if (_autocarros.TryGetValue(id, out var r) && r.CaisAlocado.HasValue)
            {
                _autocarros[id] = r with { Estado = EstadoAutocarro.Atracado };
                return true;
            }
            return false;
        }
        finally { _lock.ExitWriteLock(); }
    }

    public bool MarcarPartida(string id)
    {
        _lock.EnterWriteLock();
        try
        {
            if (_autocarros.TryGetValue(id, out var r))
            {
                _autocarros[id] = r with { Estado = EstadoAutocarro.Concluido };
                return true;
            }
            return false;
        }
        finally { _lock.ExitWriteLock(); }
    }
}