using System.Globalization;
using System.IO.Compression;
using System.Xml;

namespace AppTIC.Web.Services;

/// <summary>Uma linha de uma folha: número da linha (1 = primeira) e valores por coluna (1 = A). Células vazias ou com erro não constam.</summary>
public sealed record XlsxLinha(int Numero, IReadOnlyDictionary<int, string> Celulas)
{
    public string? this[int coluna] => Celulas.TryGetValue(coluna, out var v) && v.Length > 0 ? v : null;
}

public sealed class XlsxFolha(string nome, List<XlsxLinha> linhas, int celulasComErro)
{
    public string Nome { get; } = nome;
    public IReadOnlyList<XlsxLinha> Linhas { get; } = linhas;
    /// <summary>Células cujo valor é um erro de fórmula (#N/A, #REF!, …); são tratadas como vazias.</summary>
    public int CelulasComErro { get; } = celulasComErro;
}

/// <summary>
/// Leitor mínimo de .xlsx (Office Open XML): devolve o texto de cada célula tal como está guardado
/// (números, datas e horas ficam no valor numérico do Excel; ver <see cref="Data"/> e <see cref="Hora"/>).
/// Só lê valores — fórmulas são lidas pelo último resultado calculado.
/// </summary>
public static class XlsxReader
{
    private const string NsMain = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string NsRel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string NsPkgRel = "http://schemas.openxmlformats.org/package/2006/relationships";

    /// <summary>Limite do conteúdo descomprimido de cada parte, contra ficheiros maliciosos.</summary>
    private const long MaxParteBytes = 300L * 1024 * 1024;

    private static readonly XmlReaderSettings Xml = new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, IgnoreWhitespace = false };

    /// <summary>Lê todas as folhas (chave = nome da folha, sem distinguir maiúsculas).</summary>
    public static Dictionary<string, XlsxFolha> Ler(string path)
    {
        ZipArchive zip;
        try { zip = ZipFile.OpenRead(path); }
        catch (InvalidDataException) { throw new InvalidDataException("O ficheiro não é um .xlsx válido."); }
        using (zip)
        {
            try
            {
                var partilhadas = LerPartilhadas(zip);
                var folhas = new Dictionary<string, XlsxFolha>(StringComparer.OrdinalIgnoreCase);
                foreach (var (nome, parte) in Folhas(zip))
                    folhas[nome] = LerFolha(nome, Abrir(zip, parte), partilhadas);
                return folhas;
            }
            catch (XmlException ex)
            {
                throw new InvalidDataException($"O ficheiro .xlsx está corrompido: {ex.Message}");
            }
        }
    }

    private static Stream Abrir(ZipArchive zip, string parte)
    {
        var entry = zip.GetEntry(parte) ?? throw new InvalidDataException($"O ficheiro não é um .xlsx válido (falta {parte}).");
        if (entry.Length > MaxParteBytes) throw new InvalidDataException("O ficheiro .xlsx é demasiado grande.");
        return entry.Open();
    }

    private static List<(string Nome, string Parte)> Folhas(ZipArchive zip)
    {
        var alvos = new Dictionary<string, string>(StringComparer.Ordinal);
        using (var s = Abrir(zip, "xl/_rels/workbook.xml.rels"))
        using (var r = XmlReader.Create(s, Xml))
            while (r.Read())
                if (r.NodeType == XmlNodeType.Element && r.LocalName == "Relationship" && r.NamespaceURI == NsPkgRel
                    && r.GetAttribute("Id") is { } id && r.GetAttribute("Target") is { } target)
                    alvos[id] = target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target;

        var folhas = new List<(string, string)>();
        using (var s = Abrir(zip, "xl/workbook.xml"))
        using (var r = XmlReader.Create(s, Xml))
            while (r.Read())
                if (r.NodeType == XmlNodeType.Element && r.LocalName == "sheet" && r.NamespaceURI == NsMain
                    && r.GetAttribute("name") is { } nome && r.GetAttribute("id", NsRel) is { } rid
                    && alvos.TryGetValue(rid, out var parte) && parte.Contains("worksheets/", StringComparison.OrdinalIgnoreCase))
                    folhas.Add((nome, parte));
        return folhas;
    }

    private static List<string> LerPartilhadas(ZipArchive zip)
    {
        var lista = new List<string>();
        if (zip.GetEntry("xl/sharedStrings.xml") is null) return lista;
        using var s = Abrir(zip, "xl/sharedStrings.xml");
        using var r = XmlReader.Create(s, Xml);
        while (r.Read())
            if (r.NodeType == XmlNodeType.Element && r.LocalName == "si" && r.NamespaceURI == NsMain)
                lista.Add(LerTexto(r));
        return lista;
    }

    /// <summary>Texto de um elemento si/is: junta os &lt;t&gt; (ignora a fonética &lt;rPh&gt;). Deixa o leitor no fim do elemento.</summary>
    private static string LerTexto(XmlReader r)
    {
        if (r.IsEmptyElement) return "";
        var sb = new System.Text.StringBuilder();
        var fundo = r.Depth;
        var fonetica = 0;
        // ReadElementContentAsString já avança para o nó seguinte: nesse caso não se volta a ler.
        var avancar = true;
        while (!avancar || r.Read())
        {
            avancar = true;
            if (r.NodeType == XmlNodeType.EndElement && r.Depth == fundo) break;
            if (r.NodeType == XmlNodeType.Element && r.LocalName == "rPh" && !r.IsEmptyElement) fonetica = r.Depth;
            else if (r.NodeType == XmlNodeType.EndElement && r.LocalName == "rPh") fonetica = 0;
            else if (fonetica == 0 && r.NodeType == XmlNodeType.Element && r.LocalName == "t" && !r.IsEmptyElement)
            {
                sb.Append(r.ReadElementContentAsString());
                avancar = false;
            }
        }
        return sb.ToString();
    }

    private static XlsxFolha LerFolha(string nome, Stream stream, List<string> partilhadas)
    {
        var linhas = new List<XlsxLinha>();
        var erros = 0;
        using var _ = stream;
        using var r = XmlReader.Create(stream, Xml);
        Dictionary<int, string>? atual = null;
        var numero = 0;
        var proximaColuna = 1;
        while (r.Read())
        {
            if (r.NodeType != XmlNodeType.Element || r.NamespaceURI != NsMain) continue;
            if (r.LocalName == "row")
            {
                numero = int.TryParse(r.GetAttribute("r"), out var n) ? n : numero + 1;
                atual = [];
                linhas.Add(new XlsxLinha(numero, atual));
                proximaColuna = 1;
            }
            else if (r.LocalName == "c" && atual is not null)
            {
                var coluna = Coluna(r.GetAttribute("r")) ?? proximaColuna;
                proximaColuna = coluna + 1;
                var tipo = r.GetAttribute("t");
                if (r.IsEmptyElement) continue;
                string? valor = null;
                var fundo = r.Depth;
                var avancar = true;
                while (!avancar || r.Read())
                {
                    avancar = true;
                    if (r.NodeType == XmlNodeType.EndElement && r.Depth == fundo) break;
                    if (r.NodeType != XmlNodeType.Element) continue;
                    if (r.LocalName == "v" && !r.IsEmptyElement)
                    {
                        valor = r.ReadElementContentAsString();
                        avancar = false;
                    }
                    else if (r.LocalName == "is") valor = LerTexto(r);
                }
                if (valor is null) continue;
                switch (tipo)
                {
                    case "e": erros++; continue;
                    case "s":
                        if (!int.TryParse(valor, out var i) || i < 0 || i >= partilhadas.Count) continue;
                        valor = partilhadas[i];
                        break;
                    case "b": valor = valor == "1" ? "1" : "0"; break;
                    case "str" or "inlineStr": break;
                    default: valor = Numero(valor); break;
                }
                valor = valor.Trim();
                if (valor.Length > 0) atual[coluna] = valor;
            }
        }
        return new XlsxFolha(nome, linhas, erros);
    }

    /// <summary>"C12" → 3.</summary>
    private static int? Coluna(string? referencia)
    {
        if (string.IsNullOrEmpty(referencia)) return null;
        var n = 0;
        foreach (var ch in referencia)
        {
            if (ch is >= 'A' and <= 'Z') n = n * 26 + (ch - 'A' + 1);
            else if (ch is >= 'a' and <= 'z') n = n * 26 + (ch - 'a' + 1);
            else break;
        }
        return n > 0 ? n : null;
    }

    /// <summary>Valor numérico tal como o Excel o mostra em formato "Geral": 3489 e não 3489.0 nem 3.489E3.</summary>
    private static string Numero(string bruto)
    {
        if (!double.TryParse(bruto, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return bruto;
        return Math.Abs(d) < 1e15 && d == Math.Floor(d)
            ? ((long)d).ToString(CultureInfo.InvariantCulture)
            : d.ToString("R", CultureInfo.InvariantCulture);
    }

    // ---- Conversões ----

    private static readonly DateOnly Epoca = new(1899, 12, 30);
    private static readonly string[] FormatosData = ["dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd", "dd-MM-yyyy", "yyyyMMdd"];

    /// <summary>Data a partir do número de série do Excel (46023 = 01/01/2026) ou de texto (dd/MM/yyyy, yyyy-MM-dd).</summary>
    public static DateOnly? Data(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) return null;
        valor = valor.Trim();
        if (DateOnly.TryParseExact(valor, FormatosData, CultureInfo.InvariantCulture, DateTimeStyles.None, out var data)
            && (valor.Length != 8 || !valor.All(char.IsDigit) || valor.StartsWith("20") || valor.StartsWith("19")))
            return data;
        return double.TryParse(valor, NumberStyles.Float, CultureInfo.InvariantCulture, out var serie) && serie is >= 1 and < 2_958_466
            ? Epoca.AddDays((int)Math.Floor(serie))
            : null;
    }

    /// <summary>Hora (ao minuto) a partir da fração de dia do Excel (0,5 = 12:00) ou de texto "HH:mm[:ss]".</summary>
    public static TimeOnly? Hora(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) return null;
        valor = valor.Trim();
        if (valor.Contains(':'))
            return TimeOnly.TryParse(valor, CultureInfo.InvariantCulture, out var t) ? new TimeOnly(t.Hour, t.Minute) : null;
        if (Minutos(valor) is not { } min) return null;
        min %= 1440;
        return new TimeOnly(min / 60, min % 60);
    }

    /// <summary>Duração em minutos a partir da fração de dia do Excel (00:14 = 0,00972…) ou de texto "HH:mm".</summary>
    public static int? Minutos(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) return null;
        valor = valor.Trim();
        if (valor.Contains(':'))
            return TimeSpan.TryParse(valor, CultureInfo.InvariantCulture, out var ts) ? (int)Math.Round(ts.TotalMinutes) : null;
        return double.TryParse(valor, NumberStyles.Float, CultureInfo.InvariantCulture, out var fracao) && fracao >= 0
            ? (int)Math.Round(fracao * 1440, MidpointRounding.AwayFromZero)
            : null;
    }

    public static decimal? Decimal(string? valor) =>
        decimal.TryParse(valor?.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
}
