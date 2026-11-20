using System.Net;
using System.Text;
using AppTIC.Web.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace AppTIC.Web.Tests;

public class FlixbusRealtimeTests
{
    private const string Tic = "cd022a5c-a591-46fe-86a1-f7ca0ebbaf37";

    // Excerto real da resposta de /timetable/arrivals (TIC Campanhã), com uma viagem sem desvio e outra cancelada.
    private const string Json = """
    {"rides":[
      {"through_the_stations":"Lisbon (Oriente) → Porto (TIC - Campanhã)",
       "planned":{"timestamp":1795198500,"tz":"Europe/Lisbon"},
       "deviation":{"deviation_seconds":1210,"deviation_class":"SMALL_DELAY","deviation_type":"ESTIMATED","status":"DELAYED","updated":1795197891},
       "from_stop_uuid":"9a30aab4-4caa-4ee6-9e33-739752be7cd0","to_stop_uuid":"cd022a5c-a591-46fe-86a1-f7ca0ebbaf37",
       "ride_uuid":"e52d8c00-3587-4116-a706-a6b35376bb79","is_cancelled":false,"line_code":"1000","direction":"Porto (TIC - Campanhã)",
       "stops":[{"uuid":"9a30aab4-4caa-4ee6-9e33-739752be7cd0","name":"Lisbon (Oriente)"},{"uuid":"cd022a5c-a591-46fe-86a1-f7ca0ebbaf37","name":"Porto (TIC - Campanhã)"}],
       "trip_number":49,"brand_name":"FlixBus"},
      {"through_the_stations":"Braga → Leiria","planned":{"timestamp":1795190000,"tz":"Europe/Lisbon"},
       "ride_uuid":"r2","is_cancelled":true,"line_code":"1022",
       "stops":[{"uuid":"braga","name":"Braga"},{"uuid":"cd022a5c-a591-46fe-86a1-f7ca0ebbaf37","name":"Porto (TIC - Campanhã)"},{"uuid":"leiria","name":"Leiria"}],
       "trip_number":12}
    ],"station":{"uuid":"cd022a5c-a591-46fe-86a1-f7ca0ebbaf37","name":"Porto (TIC - Campanhã)","latitude":41.152888811,"longitude":-8.583158708}}
    """;

    private sealed class FakeHandler(string body) : HttpMessageHandler
    {
        public Uri? Pedido;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Pedido = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    private static (FlixbusClient, FakeHandler) Cliente()
    {
        var handler = new FakeHandler(Json);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://global.api.flixbus.com/gis/") };
        return (new FlixbusClient(http, Options.Create(new FlixbusOptions { StationUuid = Tic })), handler);
    }

    [Fact]
    public async Task ConstroiOUrlDaEstacaoComTimestampsUnix()
    {
        var (client, handler) = Cliente();
        var de = DateTimeOffset.FromUnixTimeSeconds(1795190000);
        await client.ObterAsync(TipoPassagem.Chegada, de, de.AddHours(6));

        Assert.Equal(
            $"https://global.api.flixbus.com/gis/v1/station/{Tic}/timetable/arrivals/from-timestamp/1795190000/to-timestamp/1795211600",
            handler.Pedido!.ToString());
    }

    [Fact]
    public async Task MapeiaAtrasoHoraEstimadaEOrigemOrdenandoPorHora()
    {
        var (client, _) = Cliente();
        var lista = (await client.ObterAsync(TipoPassagem.Chegada, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch)).Passagens;

        Assert.Equal(["r2", "e52d8c00-3587-4116-a706-a6b35376bb79"], lista.Select(p => p.RideUuid));

        var lisboa = lista[1];
        Assert.Equal("1000", lisboa.Linha);
        Assert.Equal(49, lisboa.Viagem);
        Assert.Equal("Lisbon (Oriente)", lisboa.Extremo);
        Assert.Equal(20, lisboa.AtrasoMinutos); // 1210 s
        Assert.Equal(TimeSpan.FromSeconds(1210), lisboa.Estimada - lisboa.Planeada);
        Assert.Equal(TimeSpan.Zero, lisboa.Planeada.Offset); // 20/11 em Lisboa = UTC+0 (horário de inverno)
        Assert.Equal("DELAYED", lisboa.Estado);
        Assert.False(lisboa.Cancelada);

        var cancelada = lista[0];
        Assert.True(cancelada.Cancelada);
        Assert.Null(cancelada.AtrasoMinutos);
        Assert.Equal("Braga", cancelada.Extremo);
    }

    [Fact]
    public void NasPartidasOExtremoEOUltimoDestino()
    {
        var ride = new FlixRide("r", null, new FlixPlanned(1795190000, null), null, null, null, false, "1022", null,
            [new("braga", "Braga"), new(Tic, "Porto (TIC - Campanhã)"), new("leiria", "Leiria")], 12, null);

        var p = FlixbusMapper.Mapear(ride, TipoPassagem.Partida, Tic, FlixbusMapper.Fuso("Europe/Lisbon"));

        Assert.Equal("Leiria", p.Extremo);
    }

    [Fact]
    public void UmErroMantemOsDadosAnteriores()
    {
        var store = new FlixbusRealtimeStore();
        var p = FlixbusMapper.Mapear(new FlixRide("r", null, new FlixPlanned(0, null), null, null, null, false, null, null, null, null, null),
            TipoPassagem.Chegada, Tic, TimeZoneInfo.Utc);
        store.Definir(new FlixbusSnapshot([p], [], DateTimeOffset.UnixEpoch, null));

        store.RegistarErro("timeout");

        Assert.Single(store.Atual.Chegadas);
        Assert.Equal("timeout", store.Atual.Erro);
    }

    [Fact]
    public async Task LeAEstacaoDaRespostaDoHorario()
    {
        var (client, _) = Cliente();
        var h = await client.ObterAsync(TipoPassagem.Chegada, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

        Assert.Equal(new EstacaoFlixbus("Porto (TIC - Campanhã)", 41.152888811, -8.583158708), h.Estacao);
    }

    // ---- Posições dos veículos ----

    private static readonly DateTimeOffset Agora = new(2026, 11, 20, 18, 0, 0, TimeSpan.Zero);

    private static PassagemFlixbus Passagem(string ride, TipoPassagem tipo, int minutos, bool cancelada = false, int? atraso = 0) =>
        new(tipo, ride, "1000", 1, "FlixBus", "A → B", null, null, Agora.AddMinutes(minutos), Agora.AddMinutes(minutos),
            atraso, null, null, cancelada, null);

    [Fact]
    public void SelecionaViagensPorSentidoJuntandoChegadaEPartida()
    {
        var chegadas = new[]
        {
            Passagem("vem", TipoPassagem.Chegada, 30),
            Passagem("passagem", TipoPassagem.Chegada, -5),
            Passagem("cancelada", TipoPassagem.Chegada, 10, cancelada: true),
            Passagem("longe", TipoPassagem.Chegada, 600),
        };
        var partidas = new[]
        {
            Passagem("passagem", TipoPassagem.Partida, 5),
            Passagem("foi", TipoPassagem.Partida, -40),
            Passagem("antiga", TipoPassagem.Partida, -200),
        };

        var v = FlixbusPosicoes.Selecionar(chegadas, partidas, Agora, TimeSpan.FromHours(3), TimeSpan.FromMinutes(90), 80);

        Assert.Equal(["passagem", "vem", "foi"], v.Select(x => x.RideUuid)); // mais próximas do momento atual primeiro
        Assert.Equal(SentidoTic.NoTic, v[0].Sentido);
        Assert.NotNull(v[0].Chegada);
        Assert.NotNull(v[0].Partida);
        Assert.Equal(SentidoTic.ACaminho, v[1].Sentido);
        Assert.Equal(SentidoTic.Partiu, v[2].Sentido);
    }

    [Fact]
    public void LimitaONumeroDeViagensSeguidas()
    {
        var chegadas = Enumerable.Range(1, 10).Select(i => Passagem($"r{i}", TipoPassagem.Chegada, i)).ToList();

        var v = FlixbusPosicoes.Selecionar(chegadas, [], Agora, TimeSpan.FromHours(3), TimeSpan.FromMinutes(90), 3);

        Assert.Equal(["r1", "r2", "r3"], v.Select(x => x.RideUuid));
    }

    [Fact]
    public void UsaOAtrasoDaChegadaQuandoVemACaminhoEDaPartidaDepois()
    {
        var c = Passagem("r", TipoPassagem.Chegada, 10, atraso: 7);
        var p = Passagem("r", TipoPassagem.Partida, 20, atraso: 2);
        var t = new FlixTracking("r", new FlixCoordinates(41.1, -8.5), "FAST", 46, 1795198618, false, null);

        var aCaminho = FlixbusPosicoes.Mapear(new ViagemSeguida("r", "1000", 1, null, SentidoTic.ACaminho, c, p), t, TimeZoneInfo.Utc)!;
        var noTic = FlixbusPosicoes.Mapear(new ViagemSeguida("r", "1000", 1, null, SentidoTic.NoTic, c, p), t, TimeZoneInfo.Utc)!;

        Assert.Equal(7, aCaminho.AtrasoMinutos);
        Assert.Equal(2, noTic.AtrasoMinutos);
        Assert.Equal((41.1, -8.5, 46d), (aCaminho.Latitude, aCaminho.Longitude, aCaminho.Rumo));
        Assert.Null(FlixbusPosicoes.Mapear(new ViagemSeguida("r", null, null, null, SentidoTic.ACaminho, c, null), null, TimeZoneInfo.Utc));
    }

    private sealed class RotasHandler(Func<string, (HttpStatusCode, string)> resposta) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var (status, body) = resposta(request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    [Fact]
    public async Task LePosicaoEDevolveNullQuandoNaoHaLocalizacao()
    {
        var http = new HttpClient(new RotasHandler(path => path.Contains("/sem-gps/")
            ? (HttpStatusCode.NotFound, """{"code":404,"message":"No locations found for the given ride uuid"}""")
            : (HttpStatusCode.OK, """{"ride_uuid":"r1","coordinates":{"longitude":-8.569984,"latitude":41.100544},"speed_category":"SLOW","course":46,"updated":1795198618,"above_quality_threshold":false,"stale":true}""")))
        { BaseAddress = new Uri("https://global.api.flixbus.com/gis/") };
        var client = new FlixbusClient(http, Options.Create(new FlixbusOptions()));

        var t = await client.ObterPosicaoAsync("r1");
        Assert.NotNull(t);
        Assert.Equal(41.100544, t.Coordinates!.Latitude);
        Assert.True(t.Stale);
        Assert.Null(await client.ObterPosicaoAsync("sem-gps"));
    }

    [Fact]
    public async Task DevolveOsSegmentosDaRotaPorOrdem()
    {
        var http = new HttpClient(new RotasHandler(_ => (HttpStatusCode.OK,
            """{"segments":[{"polyline":"bbb","segment_sequence":2},{"polyline":"aaa","segment_sequence":1}]}""")))
        { BaseAddress = new Uri("https://global.api.flixbus.com/gis/") };
        var client = new FlixbusClient(http, Options.Create(new FlixbusOptions()));

        Assert.Equal(["aaa", "bbb"], await client.ObterRotaAsync("r1"));
    }
}
