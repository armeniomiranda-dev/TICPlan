using AppTIC.Web.Data;
using AppTIC.Web.Models.Domain;
using AppTIC.Web.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace AppTIC.Web.Tests;

public class SimulacaoQueryTests
{
    private sealed class UtilizadorAtual : ICurrentUser
    {
        public string? UserId => "utilizador";
        public string? UserName => "utilizador";
        public bool IsAdmin { get; set; }
        public int? OperadorId { get; set; }
        public string? IpAddress => null;
    }

    [Fact]
    public async Task ConsultaUsaHorarioDoPeriodoEFiltroDoOperador()
    {
        await using var conexao = new SqliteConnection("Data Source=:memory:");
        await conexao.OpenAsync();
        var opcoes = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(conexao).UseSnakeCaseNamingConvention().Options;
        var atual = new UtilizadorAtual { IsAdmin = true };
        await using var db = new ApplicationDbContext(opcoes, atual);
        await db.Database.EnsureCreatedAsync();
        var dia = new DateOnly(2026, 6, 2);
        db.Users.Add(new ApplicationUser { Id = "utilizador", UserName = "utilizador" });
        var op1 = new Operador { Id = 1, Nif = "1", Designacao = "Operador 1", OwnerId = "utilizador" };
        var op2 = new Operador { Id = 2, Nif = "2", Designacao = "Operador 2", OwnerId = "utilizador" };
        db.Operadores.AddRange(op1, op2);
        db.Circulacoes.AddRange(
            new Circulacao
            {
                Id = 10, ViagemId = "A", OwnerId = "utilizador", Operador = op1,
                Chegada = new TimeOnly(11, 0), Partida = new TimeOnly(11, 20),
                Dias = [new CirculacaoDia { Data = dia }],
                HorariosTic = [new CirculacaoHorarioTic { DataInicio = dia, DataFim = dia, Chegada = new(9, 0), Partida = new(9, 15) }]
            },
            new Circulacao
            {
                Id = 11, ViagemId = "B", OwnerId = "utilizador", Operador = op2,
                Chegada = new TimeOnly(10, 0), Partida = new TimeOnly(10, 15),
                Dias = [new CirculacaoDia { Data = dia }]
            });
        await db.SaveChangesAsync();
        atual.IsAdmin = false;
        atual.OperadorId = 1;
        db.ChangeTracker.Clear();
        var servico = new SimulacaoService(db, new ConfigurationBuilder().Build());

        var resultado = await servico.DoDiaAsync(dia);

        var passagem = Assert.Single(resultado);
        Assert.Equal("A", passagem.ViagemId);
        Assert.Equal("Operador 1", passagem.Operador);
        Assert.Equal(9 * 3600, passagem.InicioSegundos);
        Assert.Equal(9 * 3600 + 15 * 60, passagem.FimSegundos);

        atual.IsAdmin = true;
        var todos = await servico.DoDiaAsync(dia);
        Assert.Equal(2, todos.Count);
        Assert.Equal(10 * 3600, todos.Single(p => p.ViagemId == "B").InicioSegundos);
    }
}
