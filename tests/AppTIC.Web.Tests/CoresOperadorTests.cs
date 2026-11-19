using AppTIC.Web.Services;
using Xunit;

namespace AppTIC.Web.Tests;

public class CoresOperadorTests
{
    [Theory]
    [InlineData("#FF8800", "#ff8800")]
    [InlineData("#ff8800ff", "#ff8800")] // ColorPicker devolve com transparência
    [InlineData(" #00aa11 ", "#00aa11")]
    [InlineData("vermelho", null)]
    [InlineData("#fff", null)]
    [InlineData(null, null)]
    public void NormalizaACor(string? cor, string? esperado) => Assert.Equal(esperado, CoresOperador.Normalizar(cor));

    [Fact]
    public void UsaACorDoOperadorDepoisADoGrupoDepoisAPaleta()
    {
        Assert.Equal("#111111", CoresOperador.De(3, "#111111", "#222222"));
        Assert.Equal("#222222", CoresOperador.De(3, null, "#222222ff"));
        Assert.Equal(CoresOperador.Paleta[3], CoresOperador.De(3, null, null));
        Assert.Equal(CoresOperador.De(13, null), CoresOperador.De(3, null)); // paleta de 10 cores, estável pelo Id
    }
}
