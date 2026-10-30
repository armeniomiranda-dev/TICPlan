using AppTIC.Web.Models.Terminal;
using AppTIC.Web.Services;
using Xunit;

namespace AppTIC.Web.Tests;

public class TerminalStateStoreTests
{
    private static InMemoryTerminalStateStore CriarStore() => new(new CaisService());

    [Theory]
    [InlineData(ZonaCais.Lisboa, 1, 4)]
    [InlineData(ZonaCais.Internacional, 5, 8)]
    [InlineData(ZonaCais.Norte, 9, 10)]
    [InlineData(ZonaCais.Comum, 11, 17)]
    public void ChegadaAtribuiCaisDaZona(ZonaCais zona, int primeiro, int ultimo)
    {
        var store = CriarStore();
        var resultado = store.RegistarChegada(new("A", zona, 10, 20));

        Assert.True(resultado.Sucesso);
        Assert.InRange(Assert.Single(resultado.Alocacoes!).Cais, primeiro, ultimo);
    }

    [Theory]
    [InlineData(ZonaCais.Lisboa, 4)]
    [InlineData(ZonaCais.Internacional, 4)]
    [InlineData(ZonaCais.Norte, 2)]
    public void ZonaLotadaUsaCaisComunsENuncaOutraZona(ZonaCais zona, int caisDaZona)
    {
        var store = CriarStore();
        for (var i = 0; i < caisDaZona + 2; i++)
            Assert.True(store.RegistarChegada(new($"A{i}", zona, 10, 20)).Sucesso);

        var alocados = store.ObterAtivos().Select(a => a.CaisAlocado).OfType<int>().ToList();
        Assert.Equal(2, alocados.Count(c => c >= 11));
        Assert.Equal(caisDaZona, alocados.Count(c => c < 11));
    }

    [Fact]
    public void ChegadaInviavelNaoAlteraEstadoEPartidaLibertaCais()
    {
        var store = CriarStore();
        for (var i = 0; i < 9; i++) // 2 cais do Norte + 7 comuns
            Assert.True(store.RegistarChegada(new($"A{i}", ZonaCais.Norte, 10, 20)).Sucesso);
        var antes = store.ObterAtivos();

        Assert.False(store.RegistarChegada(new("C", ZonaCais.Norte, 10, 20)).Sucesso);
        Assert.Equal(antes, store.ObterAtivos());
        Assert.True(store.MarcarPartida("A0"));
        Assert.True(store.RegistarChegada(new("C", ZonaCais.Norte, 10, 20)).Sucesso);
        Assert.Equal(9, store.ObterAtivos().Count);
    }

    [Fact]
    public void AtracadoMantemCaisAoRecalcular()
    {
        var store = CriarStore();
        Assert.True(store.RegistarChegada(new("A", ZonaCais.Norte, 10, 20)).Sucesso);
        Assert.True(store.MarcarAtracado("A"));
        var cais = Assert.Single(store.ObterAtivos()).CaisAlocado;

        Assert.True(store.RegistarChegada(new("B", ZonaCais.Norte, 10, 20)).Sucesso);
        Assert.Equal(cais, store.ObterAtivos().Single(a => a.AutocarroId == "A").CaisAlocado);
        Assert.NotEqual(cais, store.ObterAtivos().Single(a => a.AutocarroId == "B").CaisAlocado);
    }

    [Fact]
    public void IntervalosContiguosPodemPartilharCaisFixo()
    {
        var solver = new CaisService();
        var alocacoes = solver.AlocarCais([
            new("A", ZonaCais.Norte, 10, 20, 9),
            new("B", ZonaCais.Norte, 20, 30, 9)
        ]);

        Assert.NotNull(alocacoes);
        Assert.All(alocacoes, a => Assert.Equal(9, a.Cais));
    }

    [Fact]
    public void PlanearDiaDevolveExcessoEmVezDeFalhar()
    {
        var solver = new CaisService();
        // Cais comuns todos ocupados: o Norte só tem os cais 9–10.
        var plano = solver.PlanearDia([
            .. Enumerable.Range(0, 7).Select(i => new AutocarroDto($"C{i}", ZonaCais.Comum, 0, 60, null)),
            new("N1", ZonaCais.Norte, 10, 20, null),
            new("N2", ZonaCais.Norte, 10, 20, null),
            new("N3", ZonaCais.Norte, 15, 25, null),
            new("N4", ZonaCais.Norte, 20, 30, null)
        ]);

        Assert.NotNull(plano);
        Assert.Contains(Assert.Single(plano.SemCais), new[] { "N1", "N2", "N3" });
        Assert.Equal(10, plano.Alocacoes.Count);
        Assert.All(plano.Alocacoes.Where(a => a.AutocarroId.StartsWith('N')), a => Assert.InRange(a.Cais, 9, 10));
    }

    [Fact]
    public void PlanearDiaPrefereCaisDaZonaAntesDaReserva()
    {
        var solver = new CaisService();
        var plano = solver.PlanearDia(
            Enumerable.Range(0, 5).Select(i => new AutocarroDto($"L{i}", ZonaCais.Lisboa, 10, 20, null)).ToList());

        Assert.NotNull(plano);
        Assert.Empty(plano.SemCais);
        Assert.Equal(4, plano.Alocacoes.Count(a => a.Cais is >= 1 and <= 4));
        Assert.Single(plano.Alocacoes, a => a.Cais >= 11);
    }

    [Fact]
    public void PlanearDiaMantemCaisFixoDeCarroAtracado()
    {
        var solver = new CaisService();
        var plano = solver.PlanearDia([
            new("A", ZonaCais.Comum, 10, 30, 15),
            new("Extra", ZonaCais.Comum, 20, 40, null)
        ]);

        Assert.NotNull(plano);
        Assert.Equal(15, plano.Alocacoes.Single(a => a.AutocarroId == "A").Cais);
        Assert.NotEqual(15, plano.Alocacoes.Single(a => a.AutocarroId == "Extra").Cais);
    }

    [Fact]
    public void IdDuplicadoNaoSubstituiAutocarroAtracado()
    {
        var store = CriarStore();
        Assert.True(store.RegistarChegada(new("A", ZonaCais.Norte, 10, 20)).Sucesso);
        Assert.True(store.MarcarAtracado("A"));
        var antes = Assert.Single(store.ObterAtivos());

        Assert.False(store.RegistarChegada(new("A", ZonaCais.Lisboa, 30, 40)).Sucesso);
        Assert.Equal(antes, Assert.Single(store.ObterAtivos()));
    }

    [Fact]
    public async Task ChegadasSimultaneasNaoExcedemCapacidade()
    {
        var store = CriarStore();
        var tarefas = Enumerable.Range(0, 20).Select(i => Task.Run(() =>
            store.RegistarChegada(new($"A{i}", ZonaCais.Norte, 10, 20))));
        var resultados = await Task.WhenAll(tarefas);

        Assert.Equal(9, resultados.Count(r => r.Sucesso));
        var ativos = store.ObterAtivos();
        Assert.Equal(9, ativos.Count);
        Assert.Equal(9, ativos.Select(a => a.CaisAlocado).Distinct().Count());
    }
}
