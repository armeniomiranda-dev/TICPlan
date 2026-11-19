namespace AppTIC.Web.Models.ViewModels;

// Linhas planas para as grelhas Syncfusion (sem navegações, para serializar sem ciclos).

public record OperadorRow(int Id, string Nif, string Designacao, string? Abreviatura, string? Alvara,
    DateTime? AlvaraValidade, string? Ambito, int NumCirculacoes, string? Dono, string? Grupo = null, string? NumeracaoIdTic = null, string? Cor = null);

public record ParagemRow(int Id, string Codigo, string Nome, decimal? Latitude, decimal? Longitude, string? Comentarios, string? Dono);

public record CirculacaoRow(int Id, string ViagemId, string Operador, string? LinhaId, string? DesignacaoLinha,
    string? Ambito, string? Origem, string? Destino, string? Chegada, string? Partida, int NumDias,
    DateTime? Primeiro, DateTime? Ultimo, DateTime? AutorizacaoValidade, string? Dono, int? Importacao = null);

public record GtfsFeedRow(int Id, string Nome, string? Operador, string Ficheiro, double TamanhoMb, string Estado,
    DateTime? DataInicio, DateTime? DataFim, int NumRotas, int NumViagens, int NumViagensTic, DateTime CriadoEm, string? Dono);

public record ImportacaoRow(int Id, string Origem, string Nome, string? Fonte, string Estado, DateTime? DataInicio, DateTime? DataFim,
    int Circulacoes, int Aplicacoes, DateTime? AplicadaEm, DateTime CriadoEm, string? Dono);

/// <summary>Lote de importação de um feed GTFS (lista na página de importação do feed).</summary>
public record ImportacaoResumo(int Id, DateTime? AplicadaEm, DateOnly? DataInicio, DateOnly? DataFim, int Circulacoes);

public record RegistoRow(long Id, DateTime Data, string Nivel, string Categoria, string Acao, string Mensagem,
    int? GtfsFeedId, string? UserName, string? Dono);

public record UtilizadorRow(string Id, string Email, string? NomeCompleto, string? Entidade, string? Operador, string Perfil,
    bool Ativo, bool Bloqueado, DateTime CriadoEm);

public static class DateOnlyExtensions
{
    public static DateTime? ToDateTime(this DateOnly? d) => d?.ToDateTime(TimeOnly.MinValue);
}

public record PedidoRow(int Id, string Tipo, int? CirculacaoId, string ViagemId, string Operador, string Descricao, string Estado,
    string? PedidoPor, DateTime CriadoEm, DateTime? RespondidoEm);

public record DeclaracaoRow(int Id, string Codigo, int Ano, int Numero, int Versao, string? Declarante, string Operadores,
    int NumCirculacoes, DateTime CriadoEm, DateTime AtualizadoEm, string Estado = "Em vigor", string? Anterior = null);

public record DeclaracaoCirculacaoRow(int Id, string ViagemId, string? IdTic, string Operador, string? LinhaId, string? DesignacaoLinha,
    TimeOnly? Chegada, TimeOnly? Partida, DateOnly? Primeiro, DateOnly? Ultimo);

/// <summary>Numeração do ID TIC de um operador ou grupo com prefixo.</summary>
public record NumeracaoIdTicRow(int OperadorId, string Operador, bool EGrupo, string? Membros, int Prefixo, string Intervalo,
    int Utilizados, string? Ultimo, string? Proximo, int Livres, int SemIdTic);

/// <summary>Circulação com ID TIC fora da numeração do seu operador, ou repetido.</summary>
public record IdTicProblemaRow(int Id, string ViagemId, string? IdTic, string Operador, string Problema);

/// <summary>Circulação de uma declaração num ano (para consulta e exportação para Excel).</summary>
public record DeclaracaoAnoCirculacaoRow(int DeclaracaoId, string Codigo, int Ano, string? Declarante, string Estado, int CirculacaoId,
    string ViagemId, string? IdTic, string Operador, string? LinhaId, string? DesignacaoLinha, string? Origem, string? Destino,
    string? Chegada, string? Partida, DateTime? Primeiro, DateTime? Ultimo);

/// <summary>Opção da lista de circulações a associar a uma declaração (agrupadas pela declaração atual).</summary>
public record CirculacaoOpcao(int Value, string Text, string Grupo);
