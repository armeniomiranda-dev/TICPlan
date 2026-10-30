using AppTIC.Web.Models.Terminal;
using AppTIC.Web.Services;
using Xunit;

namespace AppTIC.Web.Tests;

public class TerminalStateStoreTests
{
    private static InMemoryTerminalStateStore CriarStore() => new(new CaisService());

    [Theory]
    [InlineData(ZonaCais.Lisboa, 1, 4)]
    [InlineData(ZonaCais.Fora, 5, 8)]
    [InlineData(ZonaCais.Norte, 9, 10)]
    [InlineData(ZonaCais.Outros, 11, 17)]
    public void ChegadaAtribuiCaisDaZona(ZonaCais zona, int primeiro, int ultimo)
    {
        var store = CriarStore();
        var resultado = store.RegistarChegada(new("A", zona, 10, 20));

        Assert.True(resultado.Sucesso);
        Assert.InRange(Assert.Single(resultado.Alocacoes!).Cais, primeiro, ultimo);
    }

    [Fact]
    public void LisboaPodeUsarReservaComumQuandoZonaLotada()
    {
        var store = CriarStore();
        for (var i = 0; i < 6; i++)
            Assert.True(store.RegistarChegada(new($"L{i}", ZonaCais.Lisboa, 10, 20)).Sucesso);

        var alocados = store.ObterAtivos().Select(a => a.CaisAlocado).OfType<int>().ToList();
        Assert.Contains(9, alocados);
        Assert.Contains(10, alocados);
    }

    [Fact]
    public void ChegadaInviavelNaoAlteraEstadoEPartidaLibertaCais()
    {
        var store = CriarStore();
        Assert.True(store.RegistarChegada(new("A", ZonaCais.Norte, 10, 20)).Sucesso);
        Assert.True(store.RegistarChegada(new("B", ZonaCais.Norte, 10, 20)).Sucesso);
        var antes = store.ObterAtivos();

        Assert.False(store.RegistarChegada(new("C", ZonaCais.Norte, 10, 20)).Sucesso);
        Assert.Equal(antes, store.ObterAtivos());
        Assert.True(store.MarcarPartida("A"));
        Assert.True(store.RegistarChegada(new("C", ZonaCais.Norte, 10, 20)).Sucesso);
        Assert.Equal(2, store.ObterAtivos().Count);
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

        Assert.Equal(2, resultados.Count(r => r.Sucesso));
        var ativos = store.ObterAtivos();
        Assert.Equal(2, ativos.Count);
        Assert.Equal(2, ativos.Select(a => a.CaisAlocado).Distinct().Count());
    }
}
