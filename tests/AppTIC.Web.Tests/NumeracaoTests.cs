using AppTIC.Web.Data;
using AppTIC.Web.Models.Domain;
using AppTIC.Web.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AppTIC.Web.Tests;

public class NumeracaoTests
{
    private sealed class Admin : ICurrentUser
    {
        public string? UserId => "admin";
        public string? UserName => "admin";
        public bool IsAdmin => true;
        public int? OperadorId => null;
        public string? IpAddress => null;
    }

    [Theory]
    [InlineData("2026.62.01", 2026, 62, 1)]
    [InlineData("57.01", 2025, 57, 1)]
    [InlineData("58", 2025, 58, 0)]
    [InlineData(" 2026.63.00 ", 2026, 63, 0)]
    public void LeIdDcp(string texto, int ano, int numero, int versao)
    {
        Assert.Equal((ano, numero, versao), Declaracao.Ler(texto, 2025));
        Assert.Equal($"{ano}.{numero}.{versao:00}", Declaracao.Formatar(ano, numero, versao));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("PEDIDO")]
    [InlineData("1.2.3")]
    public void RejeitaIdDcpInvalido(string? texto) => Assert.Null(Declaracao.Ler(texto, 2026));

    [Fact]
    public async Task NumeraDeclaracoesVersoesEIdTic()
    {
        await using var conexao = new SqliteConnection("Data Source=:memory:");
        await conexao.OpenAsync();
        var opcoes = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(conexao).UseSnakeCaseNamingConvention().Options;
        await using var db = new ApplicationDbContext(opcoes, new Admin());
        await db.Database.EnsureCreatedAsync();
        db.Users.Add(new ApplicationUser { Id = "admin", UserName = "admin" });

        var rne = new Operador { Nif = "503508225", Designacao = "RNE", OwnerId = "admin", EGrupo = true, PrefixoIdTic = 8 };
        var internorte = new Operador { Nif = "500288879", Designacao = "Internorte", OwnerId = "admin", PrefixoIdTic = 4, Grupo = rne };
        var flix = new Operador { Nif = "1", Designacao = "Flixbus", OwnerId = "admin", PrefixoIdTic = 1 };
        var semPrefixo = new Operador { Nif = "2", Designacao = "Outro", OwnerId = "admin" };
        db.Operadores.AddRange(rne, internorte, flix, semPrefixo);
        db.Circulacoes.Add(new Circulacao { OwnerId = "admin", ViagemId = "A", Operador = rne, IdTic = "8971" });
        db.Circulacoes.Add(new Circulacao { OwnerId = "admin", ViagemId = "B", Operador = flix, IdTic = "11005" }); // não é 1XXX
        await db.SaveChangesAsync();

        var n = new NumeracaoService(db, new Admin());
        // Num operador do grupo vale o prefixo do grupo (8XXX), não o próprio (4XXX).
        Assert.Equal("8972", await n.ProximoIdTicAsync(internorte));
        Assert.Equal("1001", await n.ProximoIdTicAsync(flix));
        Assert.Null(await n.ProximoIdTicAsync(semPrefixo));

        // Cada nova circulação: número seguinte no ano, versão 00; o declarante é o grupo.
        var c1 = new Circulacao { OwnerId = "admin", ViagemId = "C1", Operador = internorte };
        var c2 = new Circulacao { OwnerId = "admin", ViagemId = "C2", Operador = flix };
        db.Circulacoes.AddRange(c1, c2);
        var d1 = await n.AtribuirNovaDeclaracaoAsync(c1, internorte, "nova", ano: 2026);
        var d2 = await n.AtribuirNovaDeclaracaoAsync(c2, flix, "nova", ano: 2026);
        await db.SaveChangesAsync();
        Assert.Equal(("2026.1.00", "2026.2.00", rne.Id), (c1.IdDcp, c2.IdDcp, d1.OperadorId));

        // Mudança de horário: nova versão, e o código é copiado para as circulações da declaração.
        var c3 = new Circulacao { OwnerId = "admin", ViagemId = "C3", Operador = internorte, Declaracao = d1, IdDcp = d1.Codigo };
        db.Circulacoes.Add(c3);
        await db.SaveChangesAsync();
        await n.NovaVersaoAsync(c1, "horário");
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        Assert.Equal(["2026.1.01", "2026.1.01"], await db.Circulacoes.Where(c => c.DeclaracaoId == d1.Id).Select(c => c.IdDcp).ToListAsync());
        Assert.Equal([0, 1], await db.DeclaracaoVersoes.Where(v => v.DeclaracaoId == d1.Id).OrderBy(v => v.Versao).Select(v => v.Versao).ToListAsync());
        Assert.Equal("2026.2.00", (await db.Circulacoes.SingleAsync(c => c.ViagemId == "C2")).IdDcp);

        // Renovação para 2027: numeração recomeça; as circulações passam para a renovação e a de 2026 guarda-as no histórico.
        var r = await n.RenovarAnoAsync(2026, manterNumeros: false);
        Assert.Equal((2, 0, 3), (r.Renovadas, r.JaRenovadas, r.Circulacoes));
        db.ChangeTracker.Clear();
        Assert.Equal(["2027.1.00", "2027.2.00", "2027.1.00"],
            await db.Circulacoes.Where(c => c.ViagemId.StartsWith("C")).OrderBy(c => c.ViagemId).Select(c => c.IdDcp!).ToListAsync());
        var nova = await db.Declaracoes.Include(d => d.Anterior).SingleAsync(d => d.Ano == 2027 && d.Numero == 1);
        Assert.Equal(("2026.1.01", 0), (nova.Anterior!.Codigo, nova.Versao));
        Assert.Equal(2, await db.DeclaracaoCirculacoes.CountAsync(h => h.DeclaracaoId == d1.Id));
        // 2027 passou a ser o ano em vigor: 2026 já não se renova; as novas declarações vão para 2027 (número 3).
        Assert.Equal(2027, await n.AnoAtualAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => n.RenovarAnoAsync(2026, false));
        var d3 = await n.NovaDeclaracaoAsync(flix, "nova");
        Assert.Equal((2027, 3), (d3.Ano, d3.Numero));
    }

    [Fact]
    public async Task RenovaMantendoOsNumeros()
    {
        await using var conexao = new SqliteConnection("Data Source=:memory:");
        await conexao.OpenAsync();
        var opcoes = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(conexao).UseSnakeCaseNamingConvention().Options;
        await using var db = new ApplicationDbContext(opcoes, new Admin());
        await db.Database.EnsureCreatedAsync();
        db.Users.Add(new ApplicationUser { Id = "admin", UserName = "admin" });
        var op = new Operador { Nif = "1", Designacao = "Op", OwnerId = "admin" };
        foreach (var (numero, viagem) in new[] { (62, "A"), (70, "B") })
            db.Circulacoes.Add(new Circulacao { OwnerId = "admin", ViagemId = viagem, Operador = op,
                Declaracao = new Declaracao { Ano = 2026, Numero = numero, Versao = 3 } });
        await db.SaveChangesAsync();

        var n = new NumeracaoService(db, new Admin());
        // Só o ano mais recente se renova, e para o seguinte.
        await Assert.ThrowsAsync<InvalidOperationException>(() => n.RenovarAnoAsync(2025, manterNumeros: true));
        var r = await n.RenovarAnoAsync(2026, manterNumeros: true);
        Assert.Equal((2, 0), (r.Renovadas, r.NumerosOcupados.Count));
        Assert.Equal(["2027.62.00", "2027.70.00"], await db.Circulacoes.OrderBy(c => c.ViagemId).Select(c => c.IdDcp!).ToListAsync());
        // A seguir, 2027 → 2028.
        Assert.Equal(2, (await n.RenovarAnoAsync(2027, manterNumeros: false)).Renovadas);
        Assert.Equal(2028, await n.AnoAtualAsync());
    }
}
