using AppTIC.Web.Services;
using Xunit;

namespace AppTIC.Web.Tests;

public class TempoRealAgregadoTests
{
    private static readonly DateTimeOffset Agora = new(2026, 11, 20, 19, 0, 0, TimeSpan.Zero);
    private static DateTimeOffset T(int minutos) => Agora.AddMinutes(minutos);

    private static PassagemFlixbus Flix(string ride, TipoPassagem tipo, int prevista, int? atraso, bool cancelada = false) =>
        new(tipo, ride, "1000", 49, "FlixBus", "Lisbon (Oriente) → Braga", null, null,
            T(prevista), T(prevista + (atraso ?? 0)), atraso, null, null, cancelada, null);

    private static IReadOnlyList<LinhaTempoReal> Agregar(
        FlixbusSnapshot? f = null, StcpSnapshot? s = null, CpSnapshot? c = null, MetroSnapshot? m = null, int horas = 2) =>
        TempoRealAgregado.Agregar(f ?? FlixbusSnapshot.Vazio, s ?? StcpSnapshot.Vazio, c ?? CpSnapshot.Vazio, m ?? MetroSnapshot.Vazio,
            Agora, TimeSpan.FromMinutes(15), TimeSpan.FromHours(horas));

    [Fact]
    public void JuntaOsQuatroOperadoresOrdenadosPelaHoraEstimada()
    {
        var flix = new FlixbusSnapshot([Flix("r1", TipoPassagem.Chegada, 10, 20)], [], Agora, null);
        var stcp = StcpSnapshot.Vazio with
        {
            Veiculos = [new PosicaoStcp("1832", "205", "205", "#00f", "#fff", "Campanhã", SentidoTic.ACaminho, "CMP1", "CAMPANHÃ",
                T(5), T(8), 3, "R. X", 700, 41.1, -8.5, 90, 20, Agora, "t1")]
        };
        var cp = CpSnapshot.Vazio with
        {
            Comboios = [new ComboioCp(523, "IC", "Intercidades", "Lisboa", "Porto Campanha", T(15), null, T(12), null, -3, "5", false,
                SentidoTic.ACaminho, null, null, null, null, null)]
        };
        var metro = MetroSnapshot.Vazio with
        {
            Partidas = [new PartidaMetro("B", "#E31937", "#fff", "Póvoa de Varzim", T(2), "BU1")], ParagemNome = "Campanhã"
        };

        var l = Agregar(flix, stcp, cp, metro);

        Assert.Equal(["metro:BU1", "stcp:1832", "cp:523", "flixbus:r1"], l.Select(x => x.Chave));
        Assert.Equal([TipoServico.Metro, TipoServico.Autocarro, TipoServico.Comboio, TipoServico.Autocarro], l.Select(x => x.Tipo));
        Assert.Equal(["Metro", "CMP1", "Estação · linha 5", "TIC"], l.Select(x => x.Paragem));

        var m = l[0];
        Assert.False(m.TempoReal);
        Assert.Equal(("Metro do Porto", "B", "Partida", (int?)null), (m.Operador, m.Linha, m.Movimento, m.AtrasoMinutos));

        var c = l[2];
        Assert.Equal(("IC 523", "Lisboa", "Porto Campanha", "Chegada"), (c.Linha, c.Origem, c.Destino, c.Movimento));
        Assert.Equal((T(15), T(12)), (c.Prevista, c.Estimada));

        var f = l[3];
        Assert.Equal(("1000 #49", "Lisbon (Oriente)", "Braga", "Chegada", 20), (f.Linha, f.Origem, f.Destino, f.Movimento, f.AtrasoMinutos!.Value));
        Assert.False(f.TemPosicao);
    }

    [Fact]
    public void FlixbusDePassagemDaUmaSoLinhaComAHoraDeChegada()
    {
        var flix = new FlixbusSnapshot(
            [Flix("r1", TipoPassagem.Chegada, 30, 5)],
            [Flix("r1", TipoPassagem.Partida, 35, 5), Flix("r2", TipoPassagem.Partida, 40, null)], Agora, null);

        var l = Agregar(flix);

        Assert.Equal(["flixbus:r1", "flixbus:r2"], l.Select(x => x.Chave));
        Assert.Equal(("Passagem", T(35)), (l[0].Movimento, l[0].Estimada));
        Assert.Equal(("Partida", SentidoTic.NoTic), (l[1].Movimento, l[1].Sentido!.Value)); // começa no TIC
    }

    [Fact]
    public void ComboioQueComecaEmCampanhaUsaAHoraDePartida()
    {
        // Às vezes a API traz ETA mesmo sem hora de chegada (comboio com origem em Campanhã).
        var cp = CpSnapshot.Vazio with
        {
            Comboios = [new ComboioCp(15750, "U", "Urbano", "Porto Campanha", "Aveiro", null, T(10), T(0), T(10), 0, "8", false,
                SentidoTic.NoTic, 41.1, -8.5, null, "AT_ORIGIN", null)]
        };

        var c = Assert.Single(Agregar(c: cp));
        Assert.Equal(("Partida", T(10), T(10), true), (c.Movimento, c.Prevista, c.Estimada, c.TemPosicao));
    }

    [Fact]
    public void LimitaAJanelaDeTempo()
    {
        var metro = MetroSnapshot.Vazio with
        {
            Partidas =
            [
                new PartidaMetro("A", null, null, "X", T(-20), "antes"),
                new PartidaMetro("A", null, null, "X", T(-10), "ha10"),
                new PartidaMetro("A", null, null, "X", T(55), "dentro"),
                new PartidaMetro("A", null, null, "X", T(70), "fora")
            ]
        };

        Assert.Equal(["metro:ha10", "metro:dentro"], Agregar(m: metro, horas: 1).Select(x => x.Chave));
        Assert.Equal(4 - 1, Agregar(m: metro, horas: 2).Count);
    }
}

public class RastoStoreTests
{
    private sealed class Relogio(DateTimeOffset inicio) : TimeProvider
    {
        public DateTimeOffset Agora { get; set; } = inicio;
        public override DateTimeOffset GetUtcNow() => Agora;
    }

    private static readonly DateTimeOffset Inicio = new(2026, 11, 20, 19, 0, 0, TimeSpan.Zero);

    [Fact]
    public void GuardaOsUltimos10MinutosSemRepetirPosicoes()
    {
        var relogio = new Relogio(Inicio);
        var rastos = new RastoStore(relogio);

        for (var i = 0; i < 15; i++)   // uma leitura por minuto durante 15 min
        {
            relogio.Agora = Inicio.AddMinutes(i);
            rastos.Registar("stcp", [("1832", 41.10 + i * 0.001, -8.50, relogio.Agora)]);
            rastos.Registar("stcp", [("1832", 41.10 + i * 0.001, -8.50, relogio.Agora)]);  // posição repetida: ignorada
        }

        var r = rastos.Obter()["stcp:1832"];
        Assert.Equal(11, r.Count); // minutos 4..14 (o limite de 10 min inclui o minuto 4)
        Assert.Equal(Inicio.AddMinutes(4), r[0].Hora);
        Assert.Equal(41.114, r[^1].Latitude, 6);
    }

    [Fact]
    public void SoDevolveRastosComPeloMenosDoisPontosEApagaVeiculosAntigos()
    {
        var relogio = new Relogio(Inicio);
        var rastos = new RastoStore(relogio);

        rastos.Registar("cp", [("523", 41.0, -8.0, null), ("135", 40.0, -8.0, null)]);
        relogio.Agora = Inicio.AddMinutes(1);
        rastos.Registar("cp", [("523", 41.01, -8.0, null)]);

        Assert.Equal(["cp:523"], rastos.Obter().Keys);

        relogio.Agora = Inicio.AddMinutes(12);
        Assert.Empty(rastos.Obter());
    }

    [Fact]
    public void HoraDoGpsAntigaOuRepetidaUsaAHoraAtual()
    {
        var relogio = new Relogio(Inicio);
        var rastos = new RastoStore(relogio);

        rastos.Registar("flixbus", [("r", 41.0, -8.0, Inicio.AddHours(-2))]);   // GPS de há 2 h
        relogio.Agora = Inicio.AddSeconds(10);
        rastos.Registar("flixbus", [("r", 41.1, -8.0, Inicio)]);                // hora não avança

        var r = rastos.Obter()["flixbus:r"];
        Assert.Equal([Inicio, Inicio.AddSeconds(10)], r.Select(p => p.Hora));
    }
}
