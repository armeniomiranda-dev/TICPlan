using System.IO.Compression;
using System.Text;
using System.Text.Json;
using AppTIC.Web.Services;
using Xunit;

namespace AppTIC.Web.Tests;

public class CpRealtimeTests
{
    private static readonly TimeZoneInfo Lisboa = FlixbusMapper.Fuso("Europe/Lisbon");
    private static readonly DateTimeOffset Agora = new(2026, 11, 20, 19, 0, 0, TimeSpan.Zero); // 19:00 em Lisboa

    [Fact]
    public void AEstacaoNaApiUsaHifen() => Assert.Equal("94-2006", new CpOptions().EstacaoApi);

    [Theory]
    [InlineData("19:30", 2026, 11, 20, 19, 30)]
    [InlineData("23:59", 2026, 11, 20, 23, 59)]
    [InlineData("06:10", 2026, 11, 21, 6, 10)]   // muito antes de agora => dia seguinte
    public void HoraDoQuadro(string hhmm, int a, int m, int d, int h, int min) =>
        Assert.Equal(new DateTimeOffset(a, m, d, h, min, 0, TimeSpan.Zero), CpTic.Hora(hhmm, Agora));

    [Fact]
    public void HoraVaziaOuInvalida()
    {
        Assert.Null(CpTic.Hora(null, Agora));
        Assert.Null(CpTic.Hora("", Agora));
        Assert.Null(CpTic.Hora("xx", Agora));
        // Às 00:30, um comboio das 23:50 é do dia anterior.
        var madrugada = new DateTimeOffset(2026, 11, 21, 0, 30, 0, TimeSpan.Zero);
        Assert.Equal(new DateTimeOffset(2026, 11, 20, 23, 50, 0, TimeSpan.Zero), CpTic.Hora("23:50", madrugada));
    }

    [Theory]
    [InlineData("null", false)]
    [InlineData("false", false)]
    [InlineData("\"\"", false)]
    [InlineData("\"SUPRIMIDO\"", true)]
    [InlineData("true", true)]
    [InlineData("{\"motivo\":\"greve\"}", true)]
    public void Supressao(string json, bool esperado) =>
        Assert.Equal(esperado, CpTic.Suprimido(JsonSerializer.Deserialize<object>(json)));

    // Excerto real do quadro de 94-2006 (comboios.live).
    private const string Quadro = """
    {"arrivals":[
     {"trainNumber":523,"trainService":{"code":"IC","designation":"Intercidades"},"trainOrigin":{"code":"94-30007","designation":"Lisboa Santa Apolonia"},
      "trainDestination":{"code":"94-2006","designation":"Porto Campanha"},"arrivalTime":"18:43","departureTime":null,"platform":"5","delay":27,
      "occupancy":null,"supression":null,"ETA":"19:10","ETD":null},
     {"trainNumber":135,"trainService":{"code":"AP","designation":"Alfa Pendular"},"trainOrigin":{"code":"94-30007","designation":"Lisboa Santa Apolonia"},
      "trainDestination":{"code":"94-29157","designation":"Braga"},"arrivalTime":"18:48","departureTime":"18:50","platform":"4","delay":20,
      "occupancy":null,"supression":null,"ETA":"19:08","ETD":"19:10"},
     {"trainNumber":15424,"trainService":{"code":"U","designation":"Urbano"},"trainOrigin":{"code":"94-8318","designation":"Penafiel"},
      "trainDestination":{"code":"94-1008","designation":"Porto Sao Bento"},"arrivalTime":"18:55","departureTime":"18:56","platform":"2","delay":0,
      "occupancy":2,"supression":null,"ETA":"18:55","ETD":null},
     {"trainNumber":15900,"trainService":{"code":"U","designation":"Urbano"},"trainOrigin":{"code":"94-1008","designation":"Porto Sao Bento"},
      "trainDestination":{"code":"94-21071","designation":"Leca do Balio"},"arrivalTime":"18:20","departureTime":"18:21","platform":"7","delay":null,
      "occupancy":null,"supression":null,"ETA":null,"ETD":null},
     {"trainNumber":540,"trainService":{"code":"IC","designation":"Intercidades"},"trainOrigin":{"code":"94-2006","designation":"Porto Campanha"},
      "trainDestination":{"code":"94-30007","designation":"Lisboa Santa Apolonia"},"arrivalTime":null,"departureTime":"19:05","platform":"3","delay":null,
      "occupancy":null,"supression":null,"ETA":null,"ETD":null}
    ]}
    """;

    private static IReadOnlyList<CpVeiculo> Veiculos() =>
    [
        new(135, "2026-11-20", 1200, "94-1009", "41.10", "-8.59", "IN_TRANSIT", 10, 1795200995409),
        new(540, "2026-11-20", 0, "94-2006", "41.1489", "-8.5853", "AT_ORIGIN", null, 1795200995409),
        new(15424, "2026-11-19", 0, "94-2006", "41.0", "-8.0", "COMPLETED", null, null),
        new(15424, "2026-11-20", 0, "94-2010", "41.15", "-8.60", "IN_TRANSIT", 270, null)
    ];

    private static IReadOnlyList<ComboioCp> Combinar() =>
        CpTic.Combinar(JsonSerializer.Deserialize<CpQuadro>(Quadro)!.Arrivals!, Veiculos(), "94-2006", Agora, Lisboa, 15);

    [Fact]
    public void ClassificaOsComboiosEmRelacaoACampanha()
    {
        var c = Combinar().ToDictionary(x => x.Numero);

        Assert.DoesNotContain(15900, c.Keys); // partiu às 18:21, há mais de 15 min
        Assert.Equal(SentidoTic.ACaminho, c[523].Sentido); // ETA 19:10
        Assert.Equal(SentidoTic.ACaminho, c[135].Sentido); // ETA 19:08
        Assert.Equal(SentidoTic.NoTic, c[540].Sentido);    // começa aqui, GPS na estação
        Assert.Equal(SentidoTic.Partiu, c[15424].Sentido); // chegou 18:55, partiu 18:56

        Assert.Equal((27, "5", "Lisboa Santa Apolonia", "Porto Campanha"), (c[523].AtrasoMinutos, c[523].Plataforma, c[523].Origem, c[523].Destino));
        Assert.Equal(new DateTimeOffset(2026, 11, 20, 18, 43, 0, TimeSpan.Zero), c[523].ChegadaPrevista);
        Assert.Equal(new DateTimeOffset(2026, 11, 20, 19, 10, 0, TimeSpan.Zero), c[523].ChegadaEstimada);
        Assert.Null(c[523].PartidaPrevista);
        Assert.Equal(("AP", "Alfa Pendular"), (c[135].Servico, c[135].ServicoNome));
    }

    [Fact]
    public void JuntaAPosicaoGpsDoComboioDoDia()
    {
        var c = Combinar().ToDictionary(x => x.Numero);

        Assert.Equal((41.10, -8.59, 10d), (c[135].Latitude!.Value, c[135].Longitude!.Value, c[135].Rumo!.Value));
        Assert.Equal("IN_TRANSIT", c[15424].EstadoGps); // o de 2026-11-20, não o de ontem
        Assert.Null(c[523].Latitude); // sem GPS
        Assert.Equal(new DateTimeOffset(2026, 11, 20, 18, 56, 35, TimeSpan.Zero).AddMilliseconds(409), c[135].AtualizadoEm);
    }

    [Fact]
    public void ComboioCanceladoNoGpsContaComoSuprimido()
    {
        var quadro = JsonSerializer.Deserialize<CpQuadro>(Quadro)!.Arrivals!;
        var lista = CpTic.Combinar(quadro, [new(523, "2026-11-20", null, null, null, null, "CANCELLED", null, null)], "94-2006", Agora, Lisboa, 15);

        Assert.True(lista.Single(x => x.Numero == 523).Suprimido);
        Assert.False(lista.Single(x => x.Numero == 135).Suprimido);
    }
}

public class MetroRealtimeTests
{
    private static readonly TimeZoneInfo Lisboa = FlixbusMapper.Fuso("Europe/Lisbon");

    // Estrutura real do bloco "Estado das Linhas" da página inicial da Metro do Porto (resumida).
    private const string Html = """
        <h4>Estado das Linhas</h4><div class="writer_text_clear"></div></div></div>
        <div id="links_list_20" class="widget links_list"><ul>
        <li><div class="title widget_field "><div class="widget_value"><h4>Estádio do Dragão - Sr. de Matosinhos</h4></div></div>
            <div class="text widget_field "><div class="widget_value"><div class="writer_text"><p>Ok</p></div></div></div></li>
        <li><div class="title widget_field "><div class="widget_value"><h4>Campanhã - ISMAI</h4></div></div>
            <div class="text widget_field "><div class="widget_value"><div class="writer_text"><p>OK</p></div></div></div></li>
        <li><div class="title widget_field "><div class="widget_value"><h4>Hospital S. João - Vila d&#39;Este</h4></div></div>
            <div class="text widget_field "><div class="widget_value"><div class="writer_text"><a href="/news/1"><p>Condicionada</p></a></div></div></div></li>
        <li><div class="title widget_field "><div class="widget_value"><h4>Trindade - Aeroporto</h4></div></div>
            <div class="text widget_field "><div class="widget_value"><div class="writer_text"><p>Interrompida</p></div></div></div></li>
        </ul></div>
        <h4>Outra secção</h4><p>Ok</p>
        """;

    [Fact]
    public void LeOEstadoDasLinhas()
    {
        var estado = MetroEstado.Ler(Html, new Dictionary<string, string?> { ["A"] = "#199FDA" });

        Assert.Equal(["A", "C", "D", "E"], estado.Select(e => e.Linha));
        Assert.Equal(["Ok", "Ok", "Condicionada", "Interrompida"], estado.Select(e => e.Estado));
        Assert.Equal([true, true, false, false], estado.Select(e => e.Normal));
        Assert.Equal("Hospital S. João - Vila d'Este", estado[2].Percurso);
        Assert.Equal("#199FDA", estado[0].Cor);
        Assert.Empty(MetroEstado.Ler("<html>sem bloco</html>", new Dictionary<string, string?>()));
    }

    private static string CriarGtfs()
    {
        var path = Path.Combine(Path.GetTempPath(), $"metro-teste-{Guid.NewGuid():N}.zip");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        void F(string nome, string conteudo)
        {
            using var w = new StreamWriter(zip.CreateEntry(nome).Open(), new UTF8Encoding(false));
            w.Write(conteudo.Replace("\r\n", "\n"));
        }
        F("stops.txt", """
            stop_id,stop_code,stop_name,stop_desc,stop_lat,stop_lon,zone_id,stop_url
            5697,,24 de Agosto,,41.1488,-8.598349,PRT1,
            5703,,Campanhã,,41.15054,-8.586245,PRT1,
            5710,,Estádio do Dragão,,41.1607,-8.5837,PRT1,
            """);
        F("routes.txt", """
            route_id,agency_id,route_short_name,route_long_name,route_desc,route_type,route_url,route_color,route_text_color
            A,,A, - ,,1,,199FDA,FFFFFF
            F,,F, - ,,1,,F5A623,000000
            """);
        F("trips.txt", """
            route_id,service_id,trip_id,trip_headsign,direction_id,block_id,wheelchair_accessible,shape_id
            A,AU,A1,Senhor de Matosinhos,0,,1,s
            A,AU,A2,Estádio do Dragão,1,,1,s
            F,AU,F1,Fânzeres,1,,1,s
            F,AS,F2,Fânzeres,1,,1,s
            A,AU,A3,Senhor de Matosinhos,0,,1,s
            """);
        F("stop_times.txt", """
            trip_id,arrival_time,departure_time,stop_id,stop_sequence,stop_headsign,pickup_type,drop_off_type,shape_dist_traveled
            A1,9:58:00,9:58:00,5710,0,,,,
            A1,10:01:00,10:01:00,5703,1,,,,
            A1,10:04:00,10:04:00,5697,2,,,,
            A2,10:05:00,10:05:00,5697,0,,,,
            A2,10:08:00,10:08:00,5703,1,,,,
            A2,10:11:00,10:11:00,5710,2,,,,
            F1,10:20:00,10:20:00,5703,0,,,,
            F1,10:25:00,10:25:00,5710,1,,,,
            F2,10:30:00,10:30:00,5703,0,,,,
            F2,10:35:00,10:35:00,5710,1,,,,
            A3,24:10:00,24:10:00,5703,0,,,,
            A3,24:13:00,24:13:00,5697,1,,,,
            """);
        F("calendar.txt", """
            service_id,monday,tuesday,wednesday,thursday,friday,saturday,sunday,start_date,end_date
            AU,1,1,1,1,1,0,0,20260907,20261231
            AS,0,0,0,0,0,1,0,20260907,20261231
            """);
        F("calendar_dates.txt", "service_id,date,exception_type\n");
        return path;
    }

    private static MetroHorario Horario()
    {
        var zip = CriarGtfs();
        try { return MetroHorario.Carregar(zip, "5703"); }
        finally { File.Delete(zip); }
    }

    [Fact]
    public void CarregaAEstacaoEAValidade()
    {
        var h = Horario();
        Assert.Equal(("Campanhã", 41.15054, -8.586245), (h.ParagemNome, h.Latitude!.Value, h.Longitude!.Value));
        Assert.Equal(new DateOnly(2026, 12, 31), h.ValidoAte);
    }

    [Fact]
    public void PartidasNaJanelaDoDiaDeServico()
    {
        var h = Horario();
        var sexta10h = new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);

        var p = h.Proximas(sexta10h, TimeSpan.FromMinutes(60), Lisboa);

        // A2 termina noutra estação mas passa aqui; F2 é de sábado; A3 é depois da meia-noite.
        Assert.Equal(["A1", "A2", "F1"], p.Select(x => x.TripId));
        Assert.Equal(("A", "#199FDA", "#FFFFFF", "Senhor de Matosinhos"), (p[0].Linha, p[0].Cor, p[0].CorTexto, p[0].Destino));
        Assert.Equal(new DateTimeOffset(2026, 11, 20, 10, 1, 0, TimeSpan.Zero), p[0].Hora);
    }

    [Fact]
    public void ExcluiViagensQueTerminamNaEstacaoEIncluiAsDepoisDaMeiaNoite()
    {
        var h = Horario();

        // Às 00:05 de sábado, a A3 (24:10 do serviço de sexta) ainda aparece.
        var madrugada = new DateTimeOffset(2026, 11, 21, 0, 5, 0, TimeSpan.Zero);
        var p = h.Proximas(madrugada, TimeSpan.FromMinutes(30), Lisboa);
        Assert.Equal(["A3"], p.Select(x => x.TripId));
        Assert.Equal(new DateTimeOffset(2026, 11, 21, 0, 10, 0, TimeSpan.Zero), p[0].Hora);
    }
}
