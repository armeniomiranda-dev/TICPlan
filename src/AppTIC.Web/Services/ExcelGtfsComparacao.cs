namespace AppTIC.Web.Services;

/// <summary>Uma circulação do Excel e/ou uma viagem do GTFS, lado a lado.</summary>
public record ComparacaoLinha(string Estado, string? ViagemId, string? Operador, string? Linha, string? TripId,
    string? ChegadaExcel, string? PartidaExcel, string? ChegadaGtfs, string? PartidaGtfs,
    int DiasExcel, int DiasGtfs, int DiasSoExcel, int DiasSoGtfs, string? Diferencas);

/// <summary>Resultado da comparação da folha Excel com o GTFS filtrado TIC de um feed.</summary>
public record ComparacaoGtfs(DateOnly? Inicio, DateOnly? Fim, IReadOnlyList<ComparacaoLinha> Linhas, IReadOnlyList<string> TripIdsCorrespondidos)
{
    public const string Igual = "Igual";
    public const string Diferente = "Diferente";
    public const string SoExcel = "Só no Excel";
    public const string SoGtfs = "Só no GTFS";

    public int Conta(string estado) => Linhas.Count(l => l.Estado == estado);
    /// <summary>Viagens que existem dos dois lados (iguais ou diferentes).</summary>
    public int Correspondidas => Conta(Igual) + Conta(Diferente);
}

/// <summary>
/// Compara as circulações da folha Excel com as viagens de um GTFS filtrado TIC: horas de chegada/partida no TIC,
/// dias em que se realizam (no período comum aos dois) e sequência de paragens.
/// A correspondência é pelo ID de viagem: igual ao trip_id, ou igual a uma das partes do trip_id depois de retirada a
/// pontuação (ex.: viagem_id "008-01" ↔ trip_id "101750:00801:63:0").
/// </summary>
public static class ExcelGtfsComparacao
{
    private const int MinChaveParcial = 4;
    private const int MaxDatasListadas = 6;

    public static ComparacaoGtfs Comparar(ExcelDados excel, GtfsDadosImportacao gtfs, Func<string, string?>? nomeOperador = null)
    {
        // Período comum: só aí faz sentido comparar os dias.
        (DateOnly Inicio, DateOnly Fim)? periodo = null;
        if (excel.Inicio is { } ei && excel.Fim is { } ef && gtfs.Intervalo() is { } gi)
        {
            var (i, f) = (ei > gi.Inicio ? ei : gi.Inicio, ef < gi.Fim ? ef : gi.Fim);
            if (i <= f) periodo = (i, f);
        }

        var exatas = gtfs.Viagens.GroupBy(v => v.TripId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var parciais = new Dictionary<string, List<GtfsViagem>>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in gtfs.Viagens)
            foreach (var chave in Partes(v.TripId).Append(Chave(v.TripId)).Where(c => c.Length >= MinChaveParcial).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!parciais.TryGetValue(chave, out var lista)) parciais[chave] = lista = [];
                lista.Add(v);
            }

        var usadas = new HashSet<GtfsViagem>();
        var linhas = new List<ComparacaoLinha>();
        foreach (var x in excel.Circulacoes)
        {
            var operador = nomeOperador?.Invoke(x.Nif) ?? x.OperadorNome ?? x.Nif;
            var diasExcel = periodo is { } p ? x.Dias.GetViewBetween(p.Inicio, p.Fim) : [];
            var viagens = exatas.GetValueOrDefault(x.Codigo)
                ?? (Chave(x.Codigo) is { Length: >= MinChaveParcial } chave ? parciais.GetValueOrDefault(chave) : null);
            if (viagens is null)
            {
                linhas.Add(new ComparacaoLinha(ComparacaoGtfs.SoExcel, x.ViagemId, operador, x.LinhaId, null, H(x.Chegada), H(x.Partida), null, null,
                    diasExcel.Count, 0, diasExcel.Count, 0,
                    periodo is not null && diasExcel.Count == 0 ? "Sem dias no período do feed." : null));
                continue;
            }
            foreach (var v in viagens) usadas.Add(v);

            var diasPorViagem = viagens.Select(v => (Viagem: v, Dias: periodo is { } q ? gtfs.Dias(v, q.Inicio, q.Fim).ToHashSet() : [])).ToList();
            var diasGtfs = diasPorViagem.SelectMany(d => d.Dias).ToHashSet();
            // Com várias viagens para o mesmo código, as horas e o percurso são os da que tem mais dias no período.
            var principal = diasPorViagem.MaxBy(d => d.Dias.Count).Viagem;

            var soExcel = diasExcel.Where(d => !diasGtfs.Contains(d)).ToList();
            var soGtfs = diasGtfs.Where(d => !x.Dias.Contains(d)).Order().ToList();
            var diferencas = new List<string>();
            if (H(x.Chegada) != H(principal.Chegada)) diferencas.Add($"Chegada: Excel {H(x.Chegada) ?? "–"}, GTFS {H(principal.Chegada) ?? "–"}.");
            if (H(x.Partida) != H(principal.Partida)) diferencas.Add($"Partida: Excel {H(x.Partida) ?? "–"}, GTFS {H(principal.Partida) ?? "–"}.");
            if (soExcel.Count > 0) diferencas.Add($"{soExcel.Count} dia(s) só no Excel: {Datas(soExcel)}.");
            if (soGtfs.Count > 0) diferencas.Add($"{soGtfs.Count} dia(s) só no GTFS: {Datas(soGtfs)}.");
            if (x.Percurso.Count > 0 && !x.Percurso.SequenceEqual(principal.Paragens, StringComparer.Ordinal))
                diferencas.Add($"Percurso: Excel {string.Join(" › ", x.Percurso)}; GTFS {string.Join(" › ", principal.Paragens)}.");
            if (viagens.Count > 1) diferencas.Add($"{viagens.Count} viagens no GTFS com este código: {string.Join(", ", viagens.Take(5).Select(v => v.TripId))}{(viagens.Count > 5 ? ", …" : "")}.");

            var soVarias = diferencas.Count == 1 && viagens.Count > 1;
            linhas.Add(new ComparacaoLinha(diferencas.Count == 0 || soVarias ? ComparacaoGtfs.Igual : ComparacaoGtfs.Diferente,
                x.ViagemId, operador, x.LinhaId, principal.TripId + (viagens.Count > 1 ? $" (+{viagens.Count - 1})" : ""),
                H(x.Chegada), H(x.Partida), H(principal.Chegada), H(principal.Partida),
                diasExcel.Count, diasGtfs.Count, soExcel.Count, soGtfs.Count, diferencas.Count == 0 ? null : string.Join(" ", diferencas)));
        }

        foreach (var v in gtfs.Viagens.Where(v => !usadas.Contains(v)))
        {
            var dias = periodo is { } p ? gtfs.Dias(v, p.Inicio, p.Fim).Count() : 0;
            var rota = gtfs.Rotas.GetValueOrDefault(v.RouteId);
            linhas.Add(new ComparacaoLinha(ComparacaoGtfs.SoGtfs, null, null, rota?.Linha, v.TripId, null, null, H(v.Chegada), H(v.Partida),
                0, dias, 0, dias, periodo is not null && dias == 0 ? "Sem dias no período do Excel." : null));
        }

        return new ComparacaoGtfs(periodo?.Inicio, periodo?.Fim, linhas, usadas.Select(v => v.TripId).ToList());
    }

    /// <summary>Só letras e algarismos, em maiúsculas: "008-01" → "00801".</summary>
    private static string Chave(string id) => new(id.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    /// <summary>Partes do trip_id separadas por pontuação: "101750:00801:63:0" → 101750, 00801, 63, 0.</summary>
    private static IEnumerable<string> Partes(string tripId)
    {
        var inicio = -1;
        for (var i = 0; i <= tripId.Length; i++)
        {
            var alfa = i < tripId.Length && char.IsLetterOrDigit(tripId[i]);
            if (alfa && inicio < 0) inicio = i;
            else if (!alfa && inicio >= 0)
            {
                yield return tripId[inicio..i].ToUpperInvariant();
                inicio = -1;
            }
        }
    }

    private static string? H(TimeOnly? t) => t?.ToString("HH:mm");

    private static string Datas(IReadOnlyList<DateOnly> datas) =>
        string.Join(", ", datas.Take(MaxDatasListadas).Select(d => d.ToString("dd/MM"))) + (datas.Count > MaxDatasListadas ? ", …" : "");
}
