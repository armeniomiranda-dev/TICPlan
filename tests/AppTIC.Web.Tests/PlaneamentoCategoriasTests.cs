using AppTIC.Web.Models.Terminal;
using AppTIC.Web.Services;
using Xunit;

namespace AppTIC.Web.Tests;

/// <summary>Planeamento com categorias configuráveis: zonamento por tempo de permanência no OR-Tools.</summary>
public class PlaneamentoCategoriasTests
{
    // Ordem de saída por omissão: 9, 10, 8, 11, 7, 12 | 13, 6, 14, 5, 15 | 16, 4, 17, 3, 2, 1.
    private static readonly CategoriaCaisDto Rapida = new("Rapida", [9, 10, 8, 11, 7, 12], Preferencia: PreferenciaCais.Saida);
    private static readonly CategoriaCaisDto Intermedia = new("Intermedia", [13, 6, 14, 5, 15], Reserva: true);
    private static readonly CategoriaCaisDto Espera = new("Espera", [16, 4, 17, 3, 2, 1], Preferencia: PreferenciaCais.Fundo);

    private static PlaneamentoPedido Pedido(IReadOnlyList<AutocarroPlanoDto> autocarros, bool ultimoRecurso = false) =>
        new([Rapida, Intermedia, Espera], autocarros, ultimoRecurso);

    [Fact]
    public void ParagensRapidasFicamJuntoASaidaEEsperasNoFundo()
    {
        var plano = new CaisService().PlanearDia(Pedido([
            new("R1", "Rapida", 600, 602),
            new("R2", "Rapida", 600, 603),
            new("E1", "Espera", 600, 640),
            new("E2", "Espera", 600, 650)
        ]));

        Assert.NotNull(plano);
        Assert.True(plano.Otimo);
        Assert.Empty(plano.SemCais);
        int[] Cais(char prefixo) => plano.Alocacoes.Where(a => a.AutocarroId[0] == prefixo).Select(a => a.Cais).Order().ToArray();
        Assert.Equal([9, 10], Cais('R'));
        Assert.Equal([1, 2], Cais('E'));
    }

    [Fact]
    public void CategoriaLotadaUsaReservaAntesDeOutraCategoria()
    {
        var plano = new CaisService().PlanearDia(Pedido(
            Enumerable.Range(0, 7).Select(i => new AutocarroPlanoDto($"R{i}", "Rapida", 600, 605)).ToList(),
            ultimoRecurso: true));

        Assert.NotNull(plano);
        Assert.Empty(plano.SemCais);
        Assert.Equal(6, plano.Alocacoes.Count(a => Rapida.Cais.Contains(a.Cais)));
        Assert.Single(plano.Alocacoes, a => Intermedia.Cais.Contains(a.Cais));
    }

    [Theory]
    [InlineData(false, 11)]
    [InlineData(true, 13)]
    public void UltimoRecursoPermiteCaisDeOutraCategoria(bool ultimoRecurso, int comCais)
    {
        // 13 paragens rápidas em simultâneo: 6 cais próprios + 5 de reserva; as restantes só em último recurso.
        var plano = new CaisService().PlanearDia(Pedido(
            Enumerable.Range(0, 13).Select(i => new AutocarroPlanoDto($"R{i}", "Rapida", 600, 605)).ToList(),
            ultimoRecurso));

        Assert.NotNull(plano);
        Assert.Equal(comCais, plano.Alocacoes.Count);
        Assert.Equal(13 - comCais, plano.SemCais.Count);
        Assert.Equal(ultimoRecurso ? 2 : 0, plano.Alocacoes.Count(a => Espera.Cais.Contains(a.Cais)));
    }

    [Fact]
    public void CaisFixoMantemSeComCategoriasConfiguraveis()
    {
        var plano = new CaisService().PlanearDia(Pedido([
            new("A", "Rapida", 600, 610, 12),
            new("B", "Rapida", 600, 610)
        ]));

        Assert.NotNull(plano);
        Assert.Equal(12, plano.Alocacoes.Single(a => a.AutocarroId == "A").Cais);
        Assert.Equal(9, plano.Alocacoes.Single(a => a.AutocarroId == "B").Cais);
    }

    [Fact]
    public void ValidarRejeitaCategoriasECaisInvalidos()
    {
        AutocarroPlanoDto[] autocarros = [new("A", "Rapida", 0, 10)];
        Assert.Null(CaisService.Validar(Pedido(autocarros)));
        Assert.NotNull(CaisService.Validar(Pedido([new("A", "Desconhecida", 0, 10)])));
        Assert.NotNull(CaisService.Validar(Pedido([new("A", "Rapida", 10, 10)])));
        Assert.NotNull(CaisService.Validar(Pedido([.. autocarros, .. autocarros])));
        Assert.NotNull(CaisService.Validar(new([new("X", [0])], [])));
        Assert.NotNull(CaisService.Validar(new([Rapida, Rapida], [])));
        Assert.NotNull(CaisService.Validar(new([Rapida], autocarros, OrdemSaida: [1, 2, 3])));
    }
}
