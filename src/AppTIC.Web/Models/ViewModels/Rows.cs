namespace AppTIC.Web.Models.ViewModels;

// Linhas planas para as grelhas Syncfusion (sem navegações, para serializar sem ciclos).

public record OperadorRow(int Id, string Nif, string Designacao, string? Abreviatura, string? Alvara,
    DateTime? AlvaraValidade, string? Ambito, int NumCirculacoes, string? Dono);

public record ParagemRow(int Id, string Codigo, string Nome, decimal? Latitude, decimal? Longitude, string? Comentarios, string? Dono);

public record CirculacaoRow(int Id, string ViagemId, string Operador, string? LinhaId, string? DesignacaoLinha,
    string? Ambito, string? Origem, string? Destino, string? Chegada, string? Partida, int NumDias,
    DateTime? Primeiro, DateTime? Ultimo, DateTime? AutorizacaoValidade, string? Dono);

public record GtfsFeedRow(int Id, string Nome, string Ficheiro, double TamanhoMb, string Estado,
    DateTime? DataInicio, DateTime? DataFim, int NumRotas, int NumViagens, DateTime CriadoEm, string? Dono);

public record RegistoRow(long Id, DateTime Data, string Nivel, string Categoria, string Acao, string Mensagem,
    int? GtfsFeedId, string? UserName, string? Dono);

public record UtilizadorRow(string Id, string Email, string? NomeCompleto, string? Entidade, string Perfil,
    bool Ativo, bool Bloqueado, DateTime CriadoEm);

public static class DateOnlyExtensions
{
    public static DateTime? ToDateTime(this DateOnly? d) => d?.ToDateTime(TimeOnly.MinValue);
}
