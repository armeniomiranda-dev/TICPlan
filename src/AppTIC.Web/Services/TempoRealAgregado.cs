using System.Collections.Concurrent;

namespace AppTIC.Web.Services;

public enum TipoServico { Autocarro, Comboio, Metro }

/// <summary>
/// Uma passagem por Campanhã de qualquer operador (FlixBus, STCP, CP, Metro), na forma comum usada na tabela única da
/// página Tempo real. <see cref="Chave"/> identifica o veículo no mapa e no rasto ("flixbus:{ride}", "stcp:{veículo}", "cp:{nº}").
/// </summary>
public record LinhaTempoReal(
    string Chave,
    string Operador,
    TipoServico Tipo,
    string Linha,
    string? Cor,
    string? CorTexto,
    string? Origem,
    string? Destino,
    string Paragem,             // onde para em Campanhã: TIC, TIC2, CMP1, Estação CP · linha 5, Metro
    string? ParagemDescricao,
    string Movimento,           // Chegada, Partida, Passagem
    DateTimeOffset Prevista,
    DateTimeOffset Estimada,
    int? AtrasoMinutos,
    bool Cancelada,
    SentidoTic? Sentido,
    bool TempoReal,             // false na Metro (só horário)
    bool TemPosicao);

public static class TempoRealAgregado
{
    public const string CorCp = "#1b7a3a";
    public const string CorFlixbus = "#73d700";

    /// <summary>
    /// Junta os quatro operadores numa lista ordenada pela hora estimada em Campanhã. Mostra o que passou há menos de
    /// <paramref name="antes"/> e o que vem nas próximas <paramref name="depois"/>.
    /// </summary>
    public static IReadOnlyList<LinhaTempoReal> Agregar(
        FlixbusSnapshot flix, StcpSnapshot stcp, CpSnapshot cp, MetroSnapshot metro,
        DateTimeOffset agora, TimeSpan antes, TimeSpan depois)
    {
        var linhas = new List<LinhaTempoReal>();
        linhas.AddRange(DaFlixbus(flix, agora));
        linhas.AddRange(DaStcp(stcp));
        linhas.AddRange(DaCp(cp));
        linhas.AddRange(DaMetro(metro));
        return linhas
            .Where(l => l.Estimada >= agora - antes && l.Estimada <= agora + depois)
            .OrderBy(l => l.Estimada).ThenBy(l => l.Tipo).ThenBy(l => l.Linha)
            .ToList();
    }

    private static (string? Origem, string? Destino) Percurso(string? percurso)
    {
        if (string.IsNullOrWhiteSpace(percurso)) return (null, null);
        var partes = percurso.Split('→', 2, StringSplitOptions.TrimEntries);
        return partes.Length == 2 ? (partes[0], partes[1]) : (null, percurso.Trim());
    }

    public static IEnumerable<LinhaTempoReal> DaFlixbus(FlixbusSnapshot s, DateTimeOffset agora)
    {
        var posicoes = (s.Posicoes ?? []).ToDictionary(p => p.RideUuid);
        var chegadas = s.Chegadas.ToDictionary(p => p.RideUuid);
        var partidas = s.Partidas.ToDictionary(p => p.RideUuid);
        foreach (var ride in chegadas.Keys.Union(partidas.Keys))
        {
            var c = chegadas.GetValueOrDefault(ride);
            var p = partidas.GetValueOrDefault(ride);
            var ref_ = c ?? p!;   // a hora de referência é a chegada ao TIC (ou a partida, se a viagem começa aqui)
            var (origem, destino) = Percurso(ref_.Percurso);
            posicoes.TryGetValue(ride, out var pos);
            var sentido = pos?.Sentido
                ?? (c is not null && c.Estimada > agora ? SentidoTic.ACaminho
                    : p is not null && p.Estimada > agora ? SentidoTic.NoTic
                    : SentidoTic.Partiu);
            yield return new LinhaTempoReal(
                $"flixbus:{ride}", "FlixBus", TipoServico.Autocarro,
                $"{ref_.Linha}{(ref_.Viagem is { } v ? $" #{v}" : "")}", CorFlixbus, "#000000",
                origem, destino, "TIC", s.Estacao?.Nome ?? "Porto (TIC - Campanhã)",
                c is not null && p is not null ? "Passagem" : c is not null ? "Chegada" : "Partida",
                ref_.Planeada, ref_.Estimada, ref_.AtrasoMinutos, (c?.Cancelada ?? false) || (p?.Cancelada ?? false),
                sentido, true, pos is not null);
        }
    }

    public static IEnumerable<LinhaTempoReal> DaStcp(StcpSnapshot s) => s.Veiculos.Select(v => new LinhaTempoReal(
        $"stcp:{v.Veiculo}", "STCP", TipoServico.Autocarro, v.Linha, v.Cor, v.CorTexto,
        null, v.Destino, v.ParagemTic, v.ParagemTicNome, "Passagem",
        v.HoraTicPrevista, v.HoraTicEstimada, v.AtrasoMinutos, false, v.Sentido, true, true));

    public static IEnumerable<LinhaTempoReal> DaCp(CpSnapshot s) => s.Comboios.Select(c =>
    {
        // Comboios que começam em Campanhã às vezes trazem ETA: a hora de referência é a partida.
        var (prevista, estimada) = c.ChegadaPrevista is { } ch
            ? (ch, c.ChegadaEstimada ?? ch)
            : (c.PartidaPrevista ?? c.PartidaEstimada ?? default, c.PartidaEstimada ?? c.PartidaPrevista ?? default);
        return new LinhaTempoReal(
            $"cp:{c.Numero}", "CP", TipoServico.Comboio, $"{c.Servico} {c.Numero}".Trim(), CorCp, "#ffffff",
            c.Origem, c.Destino, string.IsNullOrEmpty(c.Plataforma) ? "Estação" : $"Estação · linha {c.Plataforma}",
            $"Porto-Campanhã ({s.Estacao}){(c.ServicoNome is null ? "" : " – " + c.ServicoNome)}",
            c.ChegadaPrevista is not null && c.PartidaPrevista is not null ? "Passagem" : c.ChegadaPrevista is not null ? "Chegada" : "Partida",
            prevista, estimada, c.AtrasoMinutos, c.Suprimido, c.Sentido, true, c.Latitude is not null);
    }).Where(l => l.Prevista != default);

    public static IEnumerable<LinhaTempoReal> DaMetro(MetroSnapshot s) => s.Partidas.Select(p => new LinhaTempoReal(
        $"metro:{p.TripId}", "Metro do Porto", TipoServico.Metro, p.Linha, p.Cor, p.CorTexto,
        null, p.Destino, "Metro", $"Estação de metro {s.ParagemNome ?? "Campanhã"} ({s.Paragem})", "Partida",
        p.Hora, p.Hora, null, false, null, false, false));
}

public record PontoRasto(double Latitude, double Longitude, DateTimeOffset Hora);

/// <summary>
/// Últimos minutos (10 por omissão) de posições GPS de cada veículo, para desenhar o rasto no mapa.
/// Em memória e partilhado por todos: alimentado pelos workers FlixBus, STCP e CP a cada leitura.
/// </summary>
public class RastoStore(TimeProvider tempo)
{
    public static readonly TimeSpan Duracao = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<string, List<PontoRasto>> _rastos = new();

    /// <summary>Acrescenta as posições lidas de um operador ("flixbus", "stcp", "cp") e apaga o que tem mais de 10 min.</summary>
    public void Registar(string operador, IEnumerable<(string Id, double Latitude, double Longitude, DateTimeOffset? Hora)> posicoes)
    {
        var agora = tempo.GetUtcNow();
        foreach (var (id, lat, lon, hora) in posicoes)
        {
            var lista = _rastos.GetOrAdd($"{operador}:{id}", _ => []);
            lock (lista)
            {
                // A hora do GPS pode estar parada (posição repetida) ou ser antiga: usa-se a hora atual nesses casos.
                var h = hora is { } x && x <= agora && x > agora - Duracao ? x : agora;
                if (lista.Count > 0)
                {
                    var ultimo = lista[^1];
                    if (Math.Abs(ultimo.Latitude - lat) < 1e-6 && Math.Abs(ultimo.Longitude - lon) < 1e-6) continue;
                    if (h <= ultimo.Hora) h = agora;
                }
                lista.Add(new PontoRasto(lat, lon, h));
            }
        }
        Limpar(agora);
    }

    private void Limpar(DateTimeOffset agora)
    {
        var limite = agora - Duracao;
        foreach (var (chave, lista) in _rastos)
        {
            lock (lista)
            {
                lista.RemoveAll(p => p.Hora < limite);
                if (lista.Count == 0) _rastos.TryRemove(new KeyValuePair<string, List<PontoRasto>>(chave, lista));
            }
        }
    }

    /// <summary>Rasto por veículo (só os que têm pelo menos 2 pontos), do mais antigo para o mais recente.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<PontoRasto>> Obter()
    {
        Limpar(tempo.GetUtcNow());
        var res = new Dictionary<string, IReadOnlyList<PontoRasto>>();
        foreach (var (chave, lista) in _rastos)
            lock (lista)
                if (lista.Count >= 2) res[chave] = lista.ToList();
        return res;
    }
}
