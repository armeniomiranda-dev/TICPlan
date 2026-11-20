using System.IO.Compression;
using System.Text;
using System.Text.Json;
using AppTIC.Web.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace AppTIC.Web.Tests;

public class StcpRealtimeTests
{
    private static readonly TimeZoneInfo Lisboa = FlixbusMapper.Fuso("Europe/Lisbon");
    private static readonly IReadOnlyList<string> Tic = StcpOptions.ParagensTicPadrao;

    // Sexta-feira 20/11/2026, 10:00 em Lisboa (UTC+0 no inverno).
    private static readonly DateTimeOffset Agora = new(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AsParagensDaConfiguracaoSubstituemAsPorOmissao()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Stcp:ParagensTic:0"] = "TIC1", ["Stcp:ParagensTic:1"] = "CMP1", ["Stcp:ParagensTic:2"] = "tic1"
        }).Build();
        var opt = config.GetSection(StcpOptions.Section).Get<StcpOptions>()!;

        Assert.Equal(["TIC1", "CMP1"], opt.ParagensTicEfetivas);
        Assert.Equal(StcpOptions.ParagensTicPadrao, new StcpOptions().ParagensTicEfetivas);
    }

    [Fact]
    public void EscolheOGtfsPublicadoMaisRecente()
    {
        CkanRecurso R(string url, string formato, int dia) => new(url, url, formato, new DateTime(2026, 11, dia), null);

        Assert.Equal("c.zip", StcpClient.MaisRecente([R("a.zip", "ZIP", 1), R("c.zip", "ZIP", 16), R("b.zip", "GTFS", 10), R("d.pdf", "PDF", 20)]));
        Assert.Null(StcpClient.MaisRecente(null));
    }

    [Fact]
    public void LeAsAnotacoesStcpDaFiware()
    {
        const string json = """
        [{"id":"urn:ngsi-ld:Vehicle:porto:stcp:bus:1832","type":"Vehicle",
          "annotations":{"type":"Array","value":["stcp:route:107","stcp:nr_turno:na","stcp:nr_viagem:107_0_1|337|D1|T1|N26","stcp:sentido:0"]},
          "bearing":{"type":"Number","value":107},"fleetVehicleId":{"type":"Text","value":"1832"},
          "location":{"type":"geo:json","value":{"type":"Point","coordinates":[-8.578216553,41.146247864]}},
          "observationDateTime":{"type":"DateTime","value":"2026-11-20T18:43:47.00Z"},"speed":{"type":"Number","value":23}},
         {"id":"sem-linha","annotations":{"value":[]},"location":{"value":{"coordinates":[-8.5,41.1]}}}]
        """;
        var lista = JsonSerializer.Deserialize<List<FiwareVehicle>>(json)!.Select(VeiculoStcp.De).ToList();

        var v = Assert.Single(lista.OfType<VeiculoStcp>());
        Assert.Equal(("1832", "107", "0", "107_0_1|337|D1|T1|N26"), (v.Veiculo, v.Linha, v.Sentido, v.Viagem));
        Assert.Equal((41.146247864, -8.578216553, 107d, 23d), (v.Latitude, v.Longitude, v.Rumo!.Value, v.Velocidade!.Value));
        Assert.Equal(new DateTimeOffset(2026, 11, 20, 18, 43, 47, TimeSpan.Zero), v.AtualizadoEm);
    }

    /// <summary>
    /// GTFS mínimo. Linha 400 sentido 0: N5 (dias úteis) passa no TIC1 às 10:05; N7 (dias úteis) é encurtada e não vai ao
    /// TIC; N5X (sábado) e a D3 (domingo) também passam no TIC. Linha 200 não passa no TIC.
    /// </summary>
    private static string CriarGtfs()
    {
        var path = Path.Combine(Path.GetTempPath(), $"stcp-teste-{Guid.NewGuid():N}.zip");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        void F(string nome, string conteudo)
        {
            using var w = new StreamWriter(zip.CreateEntry(nome).Open(), new UTF8Encoding(true));
            w.Write(conteudo.Replace("\r\n", "\n"));
        }
        F("stops.txt", """
            stop_lat,stop_lon,stop_id,stop_code,stop_name,zone_id,stop_url
            41.1600,-8.6000,AAA1,AAA1,ORIGEM,PRT1,
            41.1550,-8.5900,BBB1,BBB1,"MEIO, RUA",PRT1,
            41.1519195782079,-8.58223517500258,TIC1,TIC1,TERMINAL INTERMODAL CAMPANHÃ,PRT1,
            41.1400,-8.5700,CCC1,CCC1,DESTINO,PRT1,
            41.1496617166864,-8.58589257517866,CMP1,CMP1,CAMPANHÃ,PRT1,
            41.1200,-8.5400,DDD1,DDD1,FIM DA LINHA,PRT1,
            41.1580,-8.5800,EEE1,EEE1,DESVIO,PRT1,
            """);
        F("routes.txt", """
            agency_id,route_id,route_short_name,route_type,route_long_name,route_url,route_color,route_text_color,route_sort_order
            STCP,400,400,3,ALIADOS - CAMPANHÃ,,187EC2,FFFFFF,1
            STCP,200,200,3,BOLHÃO - CASTELO,,000000,FFFFFF,2
            """);
        F("trips.txt", """
            route_id,service_id,trip_id,trip_headsign,wheelchair_accessible,block_id,direction_id,shape_id
            400,UTIL:20261027,400_0_1|327|D1|T1|N5,Campanhã,0,,0,s
            400,UTIL:20261027,400_0_1|327|D1|T2|N7,Desvio,0,,0,s
            400,SABADO:20261027,400_0_1|327|D1|T1|N5X,Campanhã,0,,0,s
            400,DOM:20261027,400_0_1|326|D3|T1|N5,Campanhã,0,,0,s
            200,UTIL:20261027,200_0_1|327|D1|T1|N1,Castelo,0,,0,s
            """);
        F("stop_times.txt", """
            shape_dist_traveled,trip_id,arrival_time,departure_time,stop_id,stop_sequence,timepoint
            0,400_0_1|327|D1|T1|N5,09:50:00,09:50:00,AAA1,1,1
            0,400_0_1|327|D1|T1|N5,09:55:00,09:55:00,BBB1,2,1
            0,400_0_1|327|D1|T1|N5,10:05:00,10:05:00,TIC1,3,1
            0,400_0_1|327|D1|T1|N5,10:15:00,10:15:00,CCC1,4,1
            0,400_0_1|327|D1|T1|N5,10:30:00,10:30:00,DDD1,5,1
            0,400_0_1|327|D1|T2|N7,09:30:00,09:30:00,AAA1,1,1
            0,400_0_1|327|D1|T2|N7,09:40:00,09:40:00,BBB1,2,1
            0,400_0_1|327|D1|T2|N7,09:45:00,09:45:00,EEE1,3,1
            0,400_0_1|327|D1|T1|N5X,11:00:00,11:00:00,TIC1,1,1
            0,400_0_1|326|D3|T1|N5,12:00:00,12:00:00,TIC1,1,1
            0,200_0_1|327|D1|T1|N1,10:00:00,10:00:00,AAA1,1,1
            0,200_0_1|327|D1|T1|N1,10:10:00,10:10:00,CCC1,2,1
            """);
        F("calendar.txt", "service_id,monday,tuesday,wednesday,thursday,friday,saturday,sunday,start_date,end_date\n");
        F("calendar_dates.txt", """
            service_id,date,exception_type
            UTIL:20261027,20261120,1
            SABADO:20261027,20261121,1
            DOM:20261027,20261122,1
            """);
        F("feed_info.txt", """
            feed_publisher_name,feed_publisher_url,feed_lang,feed_start_date,feed_end_date
            STCP,http://www.stcp.pt,pt,20261026,20261126
            """);
        return path;
    }

    private static StcpGtfsIndice Indice()
    {
        var zip = CriarGtfs();
        try { return StcpGtfsIndice.Carregar(zip, Tic, Agora); }
        finally { File.Delete(zip); }
    }

    private static VeiculoStcp Bus(double lat, double lon, string viagem = "400_0_1|337|D1|T9|N99", string linha = "400") =>
        new("1001", linha, "0", viagem, lat, lon, 90, 20, Agora.AddSeconds(-30));

    [Fact]
    public void CarregaTodasAsViagensDasLinhasQueServemOTic()
    {
        var g = Indice();

        Assert.Equal(["400|0"], g.PorLinha.Keys);
        Assert.Equal(4, g.PorLinha["400|0"].Count); // inclui a N7, que não passa no TIC
        Assert.False(g.PorTripId.ContainsKey("200_0_1|327|D1|T1|N1"));
        var v = g.PorTripId["400_0_1|327|D1|T1|N5"];
        Assert.Equal(["AAA1", "BBB1", "TIC1", "CCC1", "DDD1"], v.Paragens);
        Assert.Equal(2, v.IndiceTic);
        Assert.Equal(10 * 3600 + 5 * 60, v.Horas[2]);
        Assert.False(g.PorTripId["400_0_1|327|D1|T2|N7"].PassaTic);
        Assert.Equal("MEIO, RUA", g.Paragens["BBB1"].Nome); // campo entre aspas com vírgula
        Assert.Equal(new RotaStcp("400", "400", "#187EC2", "#FFFFFF"), g.Rotas["400"]);
        Assert.Equal(["TIC1", "CMP1"], g.ParagensTic.Select(p => p.Id)); // só as que existem no GTFS
        Assert.Equal("20261026–20261126", g.Versao);
        Assert.True(g.ServicoAtivo("UTIL:20261027", new DateOnly(2026, 11, 20)));
        Assert.False(g.ServicoAtivo("SABADO:20261027", new DateOnly(2026, 11, 20)));
    }

    [Fact]
    public void AssociaPelaViagemQueMelhorEncaixaNaPosicaoEHora()
    {
        var g = Indice();

        // Nº de viagem de outra versão do horário: escolhe pela posição/hora. Junto a BBB1 às 10:00:
        // N5 (BBB1 09:55) => +5 min; N7 (BBB1 09:40) => +20 min. Fica a N5.
        var a = g.Associar(Bus(41.1550, -8.5900), Agora)!;
        Assert.Equal("400_0_1|327|D1|T1|N5", a.Viagem.TripId);
        Assert.Equal((1, 5), (a.IndiceProximo, a.AtrasoMinutos));

        // Às 09:42 no mesmo sítio encaixa melhor a N7 (+2) do que a N5 (-13).
        Assert.Equal("400_0_1|327|D1|T2|N7", g.Associar(Bus(41.1550, -8.5900), Agora.AddMinutes(-18))!.Viagem.TripId);

        // trip_id exato (mesma versão do GTFS) é usado diretamente.
        Assert.Equal("400_0_1|327|D1|T2|N7",
            g.Associar(Bus(41.1550, -8.5900, "400_0_1|327|D1|T2|N7"), Agora)!.Viagem.TripId);

        // Longe de qualquer paragem da linha, ou linha que não serve o TIC: sem associação.
        Assert.Null(g.Associar(Bus(41.30, -8.70), Agora));
        Assert.Null(g.Associar(Bus(41.1600, -8.6000, linha: "200"), Agora));
    }

    [Fact]
    public void ClassificaOSentidoEEstimaOAtraso()
    {
        var g = Indice();

        // Junto a BBB1 (prevista 09:55) às 10:00 => 5 min de atraso; TIC1 previsto 10:05 => estimado 10:10.
        var (lista, sem) = StcpTic.Filtrar([Bus(41.1550, -8.5900)], g, Agora, Lisboa, 150, 15);

        var p = Assert.Single(lista);
        Assert.Equal(0, sem);
        Assert.Equal(SentidoTic.ACaminho, p.Sentido);
        Assert.Equal(5, p.AtrasoMinutos);
        Assert.Equal("TIC1", p.ParagemTic);
        Assert.Equal(new DateTimeOffset(2026, 11, 20, 10, 5, 0, TimeSpan.Zero), p.HoraTicPrevista);
        Assert.Equal(new DateTimeOffset(2026, 11, 20, 10, 10, 0, TimeSpan.Zero), p.HoraTicEstimada);
        Assert.Equal("MEIO, RUA", p.ParagemProxima);
        Assert.Equal(("400", "#187EC2", "Campanhã"), (p.Linha, p.Cor, p.Destino));
        Assert.InRange(p.DistanciaTicMetros, 650, 850);
    }

    [Fact]
    public void JuntoAoTicContaComoNoTicDepoisPartiuEDepoisSai()
    {
        var g = Indice();

        var noTic = StcpTic.Filtrar([Bus(41.15195, -8.58230)], g, Agora, Lisboa, 150, 15).Posicoes;
        Assert.Equal(SentidoTic.NoTic, Assert.Single(noTic).Sentido);

        // Junto a CCC1, 10 min de viagem depois do TIC: partiu, ainda aparece.
        var partiu = StcpTic.Filtrar([Bus(41.1400, -8.5700)], g, Agora.AddMinutes(15), Lisboa, 150, 15).Posicoes;
        Assert.Equal(SentidoTic.Partiu, Assert.Single(partiu).Sentido);

        // Junto a DDD1, 25 min de viagem depois do TIC (> 15): já não aparece.
        Assert.Empty(StcpTic.Filtrar([Bus(41.1200, -8.5400)], g, Agora.AddMinutes(30), Lisboa, 150, 15).Posicoes);
    }

    [Fact]
    public void ExcluiVariantesQueNaoVaoAoTicEContaAsSemCorrespondencia()
    {
        var g = Indice();
        var (lista, sem) = StcpTic.Filtrar(
            [
                Bus(41.1580, -8.5800),                 // em EEE1 às 10:00: só a N7 (encurtada) lá passa => excluído
                Bus(41.30, -8.70),                     // linha do TIC, mas longe do percurso => sem correspondência
                Bus(41.1600, -8.6000, linha: "200"),   // linha que não serve o TIC => ignorado
                Bus(41.1600, -8.6000, linha: "999")
            ], g, Agora.AddMinutes(-15), Lisboa, 150, 15);

        Assert.Empty(lista);
        Assert.Equal(1, sem);
    }
}
