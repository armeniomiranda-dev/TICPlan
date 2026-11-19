using System.Text;
using AppTIC.Web.Data;
using AppTIC.Web.Models.Domain;
using AppTIC.Web.Models.Terminal;
using AppTIC.Web.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace AppTIC.Web.Tests;

public class ExportacaoTests
{
    private sealed class Admin : ICurrentUser
    {
        public string? UserId => "admin";
        public string? UserName => "admin";
        public bool IsAdmin => true;
        public int? OperadorId => null;
        public string? IpAddress => null;
    }

    private sealed class SemCais : ICaisService
    {
        public IReadOnlyList<AlocacaoResposta>? AlocarCais(IReadOnlyList<AutocarroDto> autocarros) => [];
        public PlaneamentoDia? PlanearDia(IReadOnlyList<AutocarroDto> autocarros) =>
            new(autocarros.Select((a, i) => new AlocacaoResposta(a.AutocarroId, i + 1)).ToList(), [], true);
        public PlaneamentoDia? PlanearDia(PlaneamentoPedido pedido) => throw new NotSupportedException();
    }

    [Theory]
    [InlineData(PeriodoExportacao.Diario, "2026-11-19", "2026-11-19", "2026-11-19")]
    [InlineData(PeriodoExportacao.Semanal, "2026-11-19", "2026-11-16", "2026-11-22")]
    [InlineData(PeriodoExportacao.Mensal, "2026-02-10", "2026-02-01", "2026-02-28")]
    [InlineData(PeriodoExportacao.Anual, "2026-11-19", "2026-01-01", "2026-12-31")]
    public void CalculaOIntervalo(PeriodoExportacao periodo, string data, string de, string ate) =>
        Assert.Equal((DateOnly.Parse(de), DateOnly.Parse(ate)), ExportacaoPlaneamento.Intervalo(periodo, DateOnly.Parse(data)));

    [Fact]
    public void CalculaAPascoa()
    {
        Assert.Equal(new DateOnly(2026, 4, 5), ExportacaoPlaneamento.Pascoa(2026));
        Assert.Equal(new DateOnly(2027, 3, 28), ExportacaoPlaneamento.Pascoa(2027));
    }

    [Fact]
    public async Task ExportaComADataDeSaidaDoTerminal()
    {
        await using var conexao = new SqliteConnection("Data Source=:memory:");
        await conexao.OpenAsync();
        var opcoes = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(conexao).UseSnakeCaseNamingConvention().Options;
        await using var db = new ApplicationDbContext(opcoes, new Admin());
        await db.Database.EnsureCreatedAsync();
        db.Users.Add(new ApplicationUser { Id = "admin", UserName = "admin" });
        var op = new Operador { Nif = "1", Designacao = "Rede Nacional de Expressos", Abreviatura = "Rede Expressos", OwnerId = "admin", Alvara = "200437",
            AlvaraValidade = new DateOnly(2027, 2, 23) };
        var lisboa = new Paragem { Codigo = "1", Nome = "Lisboa (Sete Rios)", OwnerId = "admin" };
        var moncao = new Paragem { Codigo = "2", Nome = "Monção", OwnerId = "admin" };
        // Entra às 23:46 e sai às 00:00 do dia seguinte; realiza-se de segunda a sexta.
        var dias = Enumerable.Range(0, 14).Select(i => new DateOnly(2026, 11, 16).AddDays(i)).Where(d => d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)).ToList();
        db.Circulacoes.Add(new Circulacao
        {
            OwnerId = "admin", Operador = op, ViagemId = "10", IdTic = "8107", DesignacaoLinha = "Monção - Lisboa (Sete Rios)",
            Origem = moncao, Destino = lisboa, Chegada = new TimeOnly(23, 46), Partida = new TimeOnly(0, 0), IdDcp = "2026.10.00",
            Autorizacao = "AUT 1", Dias = dias.Select(d => new CirculacaoDia { Data = d }).ToList()
        });
        await db.SaveChangesAsync();

        var config = new ConfigurationBuilder().Build();
        var servico = new ExportacaoPlaneamento(db, config, new SemCais());
        // Dia 20 (sexta): sai às 00:00 do circulação de quinta 19.
        var linhas = new List<LinhaPlaneamento>();
        await foreach (var l in servico.LinhasAsync(new DateOnly(2026, 11, 20), new DateOnly(2026, 11, 20), calcularCais: true)) linhas.Add(l);

        var l1 = Assert.Single(linhas);
        Assert.Equal((new DateOnly(2026, 11, 20), "23:46", "00:00", "11111000", "1"),
            (l1.Data, l1.Entrada.ToString("HH:mm"), l1.Saida.ToString("HH:mm"), l1.DSemana, l1.Cais));
        Assert.Equal(new DateOnly(2026, 11, 27), l1.DataFim);

        using var ms = new MemoryStream();
        await ExportacaoPlaneamento.EscreverCsvAsync(ms, ToAsync(linhas));
        var texto = Encoding.Latin1.GetString(ms.ToArray()).Split("\r\n");
        Assert.StartsWith("linhanome;linha;matricula;cais;dsemanat;HoraE;Hora;Mov;Operadora;Destino;Origem;trip;Tipologia;Data;DataFim;", texto[0]);
        Assert.DoesNotContain("DataInicio", texto[0]);
        Assert.DoesNotContain("periodo", texto[0]);
        Assert.Equal("MONÇÃO - LISBOA (SETE RIOS);8107;;1;11111000;23:46:00;00:00:00;;REDE EXPRESSOS;LISBOA (SETE RIOS);MONÇÃO;10;EXPRESSO;20-11-2026;27-11-2026;AUT 1;;200437;23-02-2027;2026.10.00;;;",
            texto[1]);
    }

    private static async IAsyncEnumerable<LinhaPlaneamento> ToAsync(IEnumerable<LinhaPlaneamento> linhas)
    {
        foreach (var l in linhas) yield return l;
        await Task.CompletedTask;
    }
}
