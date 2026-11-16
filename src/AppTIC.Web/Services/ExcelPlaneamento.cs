using System.Globalization;
using System.Text;
using AppTIC.Web.Models.Domain;

namespace AppTIC.Web.Services;

public record ExcelOperador(string Nif, string Designacao, string? Alvara, DateOnly? AlvaraValidade, Ambito? Ambito,
    string? Abreviatura, string? Comentarios);

public record ExcelParagem(string Codigo, string Nome, decimal? Latitude, decimal? Longitude, string? Comentarios);

/// <summary>Uma linha da folha "circulações", com o percurso (folha "percursos") e os dias (folha "calendário").</summary>
public class ExcelCirculacao
{
    /// <summary>Linha na folha "circulações".</summary>
    public int Linha { get; init; }
    public string Nif { get; init; } = "";
    public string? OperadorNome { get; init; }
    /// <summary>viagem_id tal como está na folha.</summary>
    public string Codigo { get; init; } = "";
    /// <summary>ID de viagem a gravar: o viagem_id, ou "viagem_id/ID_TIC" quando o viagem_id se repete na folha.</summary>
    public string ViagemId { get; set; } = "";
    public string? IdTic { get; init; }
    public string? LinhaId { get; init; }
    public string? DesignacaoLinha { get; init; }
    public Ambito? Ambito { get; init; }
    public string? Autorizacao { get; init; }
    public DateOnly? AutorizacaoValidade { get; init; }
    public bool DeclaracaoCapacidade { get; init; }
    public string? IdDcp { get; init; }
    public string? OrigemCodigo { get; init; }
    public string? DestinoCodigo { get; init; }
    public TimeOnly? Chegada { get; init; }
    public TimeOnly? Partida { get; init; }
    public int? TempoEmbarqueMinutos { get; init; }
    public string? Comentarios { get; init; }
    public List<string> Percurso { get; set; } = [];
    public SortedSet<DateOnly> Dias { get; } = [];
    /// <summary>Motivo pelo qual a linha não pode ser importada (null = pode).</summary>
    public string? Erro { get; set; }
}

/// <summary>Conteúdo da folha de planeamento (modelo "template_planeamento_simplificado"), pronto a importar.</summary>
public class ExcelDados
{
    public Dictionary<string, ExcelOperador> Operadores { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, ExcelParagem> Paragens { get; } = new(StringComparer.Ordinal);
    public List<ExcelCirculacao> Circulacoes { get; } = [];
    public List<string> Avisos { get; } = [];
    /// <summary>Primeiro e último dia das colunas da folha "calendário".</summary>
    public DateOnly? Inicio { get; set; }
    public DateOnly? Fim { get; set; }
}

/// <summary>
/// Lê a folha Excel de planeamento do TIC: folhas info_operador, paragens, circulações, calendário e percursos.
/// As colunas são encontradas pelo nome técnico do cabeçalho (operador_nif, viagem_id, …), em qualquer ordem.
/// </summary>
public static class ExcelPlaneamento
{
    private const int MaxCodigo = 50;

    public static ExcelDados Ler(string path)
    {
        var folhas = XlsxReader.Ler(path).ToDictionary(f => Norm(f.Key), f => f.Value);
        var dados = new ExcelDados();

        if (!folhas.TryGetValue("circulacoes", out var circulacoes) || Cabecalho(circulacoes, "viagem_id") is not { } cabCirc)
            throw new InvalidDataException("O ficheiro não tem a folha \"circulações\" com a coluna viagem_id (modelo de planeamento do TIC).");
        if (!folhas.TryGetValue("paragens", out var paragens) || Cabecalho(paragens, "paragem_id") is not { } cabPar)
            throw new InvalidDataException("O ficheiro não tem a folha \"paragens\" com a coluna paragem_id (modelo de planeamento do TIC).");

        LerOperadores(dados, folhas.GetValueOrDefault("info_operador"));
        LerParagens(dados, paragens, cabPar);
        LerCirculacoes(dados, circulacoes, cabCirc);
        LerPercursos(dados, folhas.GetValueOrDefault("percursos"));
        LerCalendario(dados, folhas.GetValueOrDefault("calendario"));
        return dados;
    }

    private static void LerOperadores(ExcelDados dados, XlsxFolha? folha)
    {
        if (folha is null || Cabecalho(folha, "operador_nif") is not { } cab)
        {
            dados.Avisos.Add("Folha \"info_operador\" em falta: os operadores novos são criados só com o NIF e o nome da folha \"circulações\".");
            return;
        }
        foreach (var l in Dados(folha, cab))
        {
            if (Texto(l, cab, "operador_nif") is not { } nif) continue;
            var designacao = Texto(l, cab, "operador_designacao") ?? Texto(l, cab, "operador_abreviatura") ?? nif;
            dados.Operadores[nif] = new ExcelOperador(nif, Truncar(designacao, 200)!,
                Truncar(TextoP(l, cab, "operador_alvara"), 100), XlsxReader.Data(Texto(l, cab, "alvara_validade")),
                AmbitoDe(Texto(l, cab, "ambito")), Truncar(Texto(l, cab, "operador_abreviatura"), 30), Texto(l, cab, "comentarios"));
        }
    }

    private static void LerParagens(ExcelDados dados, XlsxFolha folha, Cab cab)
    {
        foreach (var l in Dados(folha, cab))
        {
            if (Texto(l, cab, "paragem_id") is not { } codigo) continue;
            if (codigo.Length > MaxCodigo)
            {
                dados.Avisos.Add($"paragens, linha {l.Numero}: paragem_id \"{codigo}\" tem mais de {MaxCodigo} caracteres; ignorada.");
                continue;
            }
            decimal? Coord(string coluna, decimal limite)
            {
                if (Texto(l, cab, coluna) is not { } s) return null;
                if (XlsxReader.Decimal(s) is { } d && Math.Abs(d) <= limite) return Math.Round(d, 6);
                dados.Avisos.Add($"paragens, linha {l.Numero}: {coluna} \"{s}\" inválida; a paragem {codigo} fica sem essa coordenada.");
                return null;
            }
            dados.Paragens[codigo] = new ExcelParagem(codigo, Truncar(Texto(l, cab, "paragem_nome"), 200) ?? codigo,
                Coord("latitude", 90), Coord("longitude", 180), Texto(l, cab, "comentarios"));
        }
    }

    private static void LerCirculacoes(ExcelDados dados, XlsxFolha folha, Cab cab)
    {
        foreach (var l in Dados(folha, cab))
        {
            if (Texto(l, cab, "viagem_id") is not { } codigo) continue;
            var nif = Texto(l, cab, "operador_nif");
            var c = new ExcelCirculacao
            {
                Linha = l.Numero, Nif = nif ?? "", OperadorNome = Texto(l, cab, "operador"), Codigo = codigo, ViagemId = codigo,
                IdTic = Truncar(Texto(l, cab, "id_tic"), 50),
                LinhaId = Truncar(Texto(l, cab, "linha_id"), 50), DesignacaoLinha = Truncar(Texto(l, cab, "designacao_linha"), 200),
                Ambito = AmbitoDe(Texto(l, cab, "ambito")),
                Autorizacao = Truncar(Texto(l, cab, "autorizacao"), 100), AutorizacaoValidade = XlsxReader.Data(Texto(l, cab, "aut_validade")),
                DeclaracaoCapacidade = Sim(Texto(l, cab, "declaracao_capacidade")), IdDcp = Truncar(Texto(l, cab, "id_dcp"), 50),
                OrigemCodigo = Texto(l, cab, "origem_id"), DestinoCodigo = Texto(l, cab, "destino_id"),
                Chegada = XlsxReader.Hora(Texto(l, cab, "chegada")), Partida = XlsxReader.Hora(Texto(l, cab, "partida")),
                TempoEmbarqueMinutos = XlsxReader.Minutos(TextoP(l, cab, "tempo embarque")) is { } m and <= 1440 ? m : null,
                Comentarios = Texto(l, cab, "comentarios")
            };
            if (nif is null) c.Erro = "sem operador_nif";
            else if (c.Chegada is null && c.Partida is null) c.Erro = "sem chegada nem partida do TIC";
            dados.Circulacoes.Add(c);
        }

        // O viagem_id tem de ser único: quando se repete, distingue-se pelo ID_TIC.
        foreach (var grupo in dados.Circulacoes.GroupBy(c => c.Codigo, StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            var lista = grupo.ToList();
            var distinguem = lista.All(c => c.IdTic is not null) && lista.Select(c => c.IdTic).Distinct().Count() == lista.Count;
            if (distinguem)
            {
                foreach (var c in lista) c.ViagemId = $"{c.Codigo}/{c.IdTic}";
                dados.Avisos.Add($"viagem_id \"{grupo.Key}\" repete-se {lista.Count} vezes (linhas {string.Join(", ", lista.Select(c => c.Linha))}); "
                    + $"fica com o ID_TIC no ID de viagem: {string.Join(", ", lista.Select(c => c.ViagemId))}.");
            }
            else
                foreach (var c in lista.Skip(1)) c.Erro ??= $"viagem_id repetido (já na linha {lista[0].Linha}) e sem ID_TIC que o distinga";
        }
        foreach (var c in dados.Circulacoes.Where(c => c.ViagemId.Length > MaxCodigo))
            c.Erro ??= $"ID de viagem com mais de {MaxCodigo} caracteres";
    }

    private static void LerPercursos(ExcelDados dados, XlsxFolha? folha)
    {
        if (folha is null || Cabecalho(folha, "id_viagem/sequencia") is not { } cab)
        {
            dados.Avisos.Add("Folha \"percursos\" em falta: as circulações ficam sem percurso.");
            return;
        }
        // Colunas da sequência: cabeçalho numérico (1, 2, 3, …), pela ordem do número.
        var colunas = cab.Linha.Celulas.Where(c => int.TryParse(c.Value, out _))
            .OrderBy(c => int.Parse(c.Value)).ThenBy(c => c.Key).Select(c => c.Key).ToList();
        var encontradas = new HashSet<ExcelCirculacao>();
        foreach (var l in Dados(folha, cab))
        {
            if (Circulacao(dados, l, cab, "id_viagem/sequencia") is not { } c || !encontradas.Add(c)) continue;
            c.Percurso = colunas.Select(col => l[col]).Where(v => v is not null).Select(v => v!).ToList();
        }
        if (dados.Circulacoes.Count(c => !encontradas.Contains(c)) is > 0 and var semPercurso)
            dados.Avisos.Add($"{semPercurso} circulação(ões) sem linha na folha \"percursos\".");
    }

    private static void LerCalendario(ExcelDados dados, XlsxFolha? folha)
    {
        if (folha is null || Cabecalho(folha, "id_viagem/data") is not { } cab)
        {
            dados.Avisos.Add("Folha \"calendário\" em falta: as circulações ficam sem dias marcados.");
            return;
        }
        var chave = cab.Colunas["id_viagem/data"];
        // Colunas dos dias: cabeçalho com uma data (número de série do Excel ou dd/MM/yyyy).
        static bool PareceData(string v) =>
            v.Contains('/') || double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && n is > 20000 and < 100000;
        var datas = cab.Linha.Celulas.Where(c => c.Key != chave && PareceData(c.Value))
            .Select(c => (Coluna: c.Key, Data: XlsxReader.Data(c.Value)))
            .Where(c => c.Data is not null).Select(c => (c.Coluna, Data: c.Data!.Value)).ToList();
        if (datas.Count == 0)
        {
            dados.Avisos.Add("A folha \"calendário\" não tem datas no cabeçalho: as circulações ficam sem dias marcados.");
            return;
        }
        dados.Inicio = datas.Min(d => d.Data);
        dados.Fim = datas.Max(d => d.Data);

        var encontradas = new HashSet<ExcelCirculacao>();
        foreach (var l in Dados(folha, cab))
        {
            if (Circulacao(dados, l, cab, "id_viagem/data") is not { } c || !encontradas.Add(c)) continue;
            foreach (var (coluna, data) in datas)
                if (l[coluna] == "1") c.Dias.Add(data);
        }
        if (folha.CelulasComErro > 0)
            dados.Avisos.Add($"calendário: {folha.CelulasComErro:N0} células com erro de fórmula (#N/A) foram lidas como \"não se realiza\".");
        if (dados.Circulacoes.Count(c => !encontradas.Contains(c)) is > 0 and var semLinha)
            dados.Avisos.Add($"{semLinha} circulação(ões) sem linha na folha \"calendário\".");
        if (dados.Circulacoes.Count(c => encontradas.Contains(c) && c.Dias.Count == 0) is > 0 and var semDias)
            dados.Avisos.Add($"{semDias} circulação(ões) sem nenhum dia marcado no calendário.");
    }

    /// <summary>Circulação a que pertence uma linha de "calendário"/"percursos": pelo ID_TIC + viagem_id, ou só pelo viagem_id.</summary>
    private static ExcelCirculacao? Circulacao(ExcelDados dados, XlsxLinha l, Cab cab, string colunaViagem)
    {
        if (Texto(l, cab, colunaViagem) is not { } codigo) return null;
        var candidatas = dados.Circulacoes.Where(c => c.Codigo == codigo).ToList();
        if (candidatas.Count <= 1) return candidatas.FirstOrDefault();
        var idTic = Texto(l, cab, "id_tic");
        return candidatas.FirstOrDefault(c => c.IdTic == idTic);
    }

    // ---- Cabeçalhos e células ----

    private sealed record Cab(XlsxLinha Linha, Dictionary<string, int> Colunas);

    /// <summary>Linha de cabeçalho: a primeira (das 10 primeiras) que tem a coluna indicada.</summary>
    private static Cab? Cabecalho(XlsxFolha folha, string coluna)
    {
        foreach (var l in folha.Linhas.Take(10))
        {
            var colunas = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var (c, v) in l.Celulas.OrderBy(c => c.Key)) colunas.TryAdd(Norm(v), c);
            if (colunas.ContainsKey(coluna)) return new Cab(l, colunas);
        }
        return null;
    }

    private static IEnumerable<XlsxLinha> Dados(XlsxFolha folha, Cab cab) =>
        folha.Linhas.Where(l => l.Numero > cab.Linha.Numero && l.Celulas.Count > 0);

    private static string? Texto(XlsxLinha l, Cab cab, string coluna) =>
        cab.Colunas.TryGetValue(coluna, out var c) ? l[c] : null;

    /// <summary>Como <see cref="Texto"/>, mas a coluna só tem de começar pelo nome (ex.: "tempo embarque/desembarque (em caso de término)").</summary>
    private static string? TextoP(XlsxLinha l, Cab cab, string prefixo) =>
        cab.Colunas.FirstOrDefault(c => c.Key.StartsWith(prefixo, StringComparison.Ordinal)) is { Key: not null } par ? l[par.Value] : null;

    private static Ambito? AmbitoDe(string? s) => s is null ? null : Norm(s) switch
    {
        "nacional" => Ambito.Nacional,
        "internacional" => Ambito.Internacional,
        "nac/int" or "nacional/internacional" or "nac/internacional" => Ambito.NacionalInternacional,
        _ => null
    };

    private static bool Sim(string? s) => s is not null && Norm(s) is "sim" or "s" or "1" or "x" or "yes" or "true";

    private static string? Truncar(string? s, int max) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Length <= max ? s : s[..max];

    /// <summary>Minúsculas, sem acentos e sem espaços nas pontas: "Circulações" → "circulacoes".</summary>
    internal static string Norm(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(ch);
        return sb.ToString().Normalize(NormalizationForm.FormC).Trim().ToLowerInvariant();
    }
}
