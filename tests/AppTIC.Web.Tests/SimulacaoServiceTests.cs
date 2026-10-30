using AppTIC.Web.Services;
using Xunit;

namespace AppTIC.Web.Tests;

public class SimulacaoServiceTests
{
    private static readonly DateOnly Dia = new(2026, 6, 2);

    private static SimulacaoOrigem Origem(DateOnly data, TimeOnly? chegada, TimeOnly? partida, int permanencia = 10) =>
        new(42, data, "VIAGEM", "Operador", "Linha", "Origem", "Destino", chegada, partida, permanencia);

    [Fact]
    public void IncluiPermanenciaDoDiaAnteriorQueAtravessaMeiaNoite()
    {
        var passagem = SimulacaoService.CriarPassagem(Dia, Origem(Dia.AddDays(-1), new(23, 50), new(0, 20)));
        Assert.NotNull(passagem);
        Assert.Equal(-600, passagem.InicioSegundos);
        Assert.Equal(1200, passagem.FimSegundos);
        Assert.False(passagem.PermanenciaEstimada);
    }

    [Fact]
    public void IncluiPartidaDoDiaSeguinteCujaPermanenciaComecaNoDia()
    {
        var passagem = SimulacaoService.CriarPassagem(Dia, Origem(Dia.AddDays(1), null, new(0, 5)));
        Assert.NotNull(passagem);
        Assert.Equal(86100, passagem.InicioSegundos);
        Assert.Equal(86700, passagem.FimSegundos);
        Assert.True(passagem.PermanenciaEstimada);
    }

    [Fact]
    public void ExcluiPassagensForaDoDiaOuSemHorario()
    {
        Assert.Null(SimulacaoService.CriarPassagem(Dia, Origem(Dia.AddDays(-1), new(20, 0), new(21, 0))));
        Assert.Null(SimulacaoService.CriarPassagem(Dia, Origem(Dia.AddDays(-1), new(23, 50), new(0, 0))));
        Assert.Null(SimulacaoService.CriarPassagem(Dia, Origem(Dia.AddDays(-1), new(20, 0), new(20, 0))));
        Assert.Null(SimulacaoService.CriarPassagem(Dia, Origem(Dia, null, null)));
    }

    [Fact]
    public void UsaPermanenciaIndicadaQuandoExisteApenasChegada()
    {
        var passagem = SimulacaoService.CriarPassagem(Dia, Origem(Dia, new(14, 0), null, 25));
        Assert.NotNull(passagem);
        Assert.Equal(14 * 3600, passagem.InicioSegundos);
        Assert.Equal(14 * 3600 + 25 * 60, passagem.FimSegundos);
        Assert.True(passagem.PermanenciaEstimada);
    }
}
