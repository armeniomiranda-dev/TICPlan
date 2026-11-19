using System.Globalization;
using System.Text;
using AppTIC.Web.Data;
using AppTIC.Web.Models.Domain;
using AppTIC.Web.Models.Terminal;
using Microsoft.EntityFrameworkCore;

namespace AppTIC.Web.Services;

public enum PeriodoExportacao { Diario, Semanal, Mensal, Anual, Personalizado }

/// <summary>Uma linha do planeamento (formato "Horarios.csv"): uma circulação num dia de saída do terminal.</summary>
public record LinhaPlaneamento(
    string LinhaNome, string Linha, string Matricula, string Cais, string DSemana, DateTime Entrada, DateTime Saida, string Mov,
    string Operadora, string Destino, string Origem, string Trip, string Tipologia, DateOnly DataFim,
    string Autoriza, DateOnly? ValidadeAutoriza, string AutorizaComunitaria, DateOnly? ValidadeAc, string Obs1, string Obs2, string Obs3,
    int CirculacaoId)
{
    /// <summary>Data de saída do terminal.</summary>
    public DateOnly Data => DateOnly.FromDateTime(Saida);
}

/// <summary>
/// Exportação do planeamento (diário a anual) no formato do ficheiro "Horarios.csv" do terminal
/// (sem as colunas periodo e DataInicio). Uma linha por circulação e dia; a Data é a de saída do terminal
/// (dia seguinte quando a partida é depois da meia-noite), HoraE a entrada e Hora a saída.
/// </summary>
public class ExportacaoPlaneamento(ApplicationDbContext db, IConfiguration config, ICaisService cais)
{
    public static readonly string[] Colunas =
    [
        "linhanome", "linha", "matricula", "cais", "dsemanat", "HoraE", "Hora", "Mov", "Operadora", "Destino", "Origem", "trip",
        "Tipologia", "Data", "DataFim", "Autoriza", "ValidadeAutoriza", "AutorizaComunitaria", "ValidadeAC", "OBS1", "OBS2", "OBS3,Faturação"
    ];

    /// <summary>Intervalo [de, ate] de um período a partir de uma data de referência.</summary>
    public static (DateOnly De, DateOnly Ate) Intervalo(PeriodoExportacao periodo, DateOnly data, DateOnly? ate = null) => periodo switch
    {
        PeriodoExportacao.Semanal => (data.AddDays(-(((int)data.DayOfWeek + 6) % 7)), data.AddDays(-(((int)data.DayOfWeek + 6) % 7) + 6)),
        PeriodoExportacao.Mensal => (new DateOnly(data.Year, data.Month, 1), new DateOnly(data.Year, data.Month, 1).AddMonths(1).AddDays(-1)),
        PeriodoExportacao.Anual => (new DateOnly(data.Year, 1, 1), new DateOnly(data.Year, 12, 31)),
        PeriodoExportacao.Personalizado when ate is { } a && a >= data => (data, a),
        _ => (data, data)
    };

    private sealed record Info(
        int Id, string ViagemId, string? IdTic, string? LinhaId, string? DesignacaoLinha, Ambito? Ambito, ZonaCais? Categoria,
        string? Autorizacao, DateOnly? AutorizacaoValidade, string? IdDcp, string? Comentarios, TimeOnly? Chegada, TimeOnly? Partida,
        int? TempoEmbarque, string Operador, string? Alvara, DateOnly? AlvaraValidade, string? Origem, string? Destino);

    /// <summary>
    /// Linhas com saída do terminal entre <paramref name="de"/> e <paramref name="ate"/>, por data e hora de saída.
    /// <paramref name="operadorId"/>: um operador ou um grupo (inclui os operadores do grupo).
    /// Com <paramref name="calcularCais"/>, o cais vem do planeamento de cada dia (só para poucos dias: o cálculo é pesado).
    /// </summary>
    public async IAsyncEnumerable<LinhaPlaneamento> LinhasAsync(DateOnly de, DateOnly ate, int? operadorId = null, bool calcularCais = false,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var permanenciaPorOmissao = config.GetValue("Terminal:PermanenciaPorOmissaoMinutos", 10);
        var circulacoes = db.Circulacoes.AsNoTracking();
        if (operadorId is { } op) circulacoes = circulacoes.Where(c => c.OperadorId == op || c.Operador!.GrupoId == op);

        var infos = await circulacoes.Select(c => new Info(c.Id, c.ViagemId, c.IdTic, c.LinhaId, c.DesignacaoLinha, c.Ambito, c.Categoria,
                c.Autorizacao, c.AutorizacaoValidade, c.IdDcp, c.Comentarios, c.Chegada, c.Partida, c.TempoEmbarqueMinutos,
                c.Operador!.Abreviatura ?? c.Operador.Designacao, c.Operador.Alvara, c.Operador.AlvaraValidade,
                c.Origem == null ? null : c.Origem.Nome, c.Destino == null ? null : c.Destino.Nome))
            .ToDictionaryAsync(c => c.Id, ct);
        if (infos.Count == 0) yield break;
        var ids = infos.Keys.ToList();

        // Calendário de cada circulação (todos os dias): dias da semana em que se realiza, feriados e último dia.
        var semana = (await db.CirculacaoDias.AsNoTracking().Where(d => ids.Contains(d.CirculacaoId))
                .GroupBy(d => new { d.CirculacaoId, Dia = d.Data.DayOfWeek }).Select(g => g.Key).ToListAsync(ct))
            .ToLookup(x => x.CirculacaoId, x => x.Dia);
        var limites = await db.CirculacaoDias.AsNoTracking().Where(d => ids.Contains(d.CirculacaoId))
            .GroupBy(d => d.CirculacaoId).Select(g => new { g.Key, Primeiro = g.Min(d => d.Data), Ultimo = g.Max(d => d.Data) })
            .ToDictionaryAsync(x => x.Key, ct);
        var anos = limites.Values.SelectMany(l => Enumerable.Range(l.Primeiro.Year, l.Ultimo.Year - l.Primeiro.Year + 1)).Distinct().ToList();
        var feriados = anos.SelectMany(Feriados).ToList();
        var emFeriado = (await db.CirculacaoDias.AsNoTracking().Where(d => ids.Contains(d.CirculacaoId) && feriados.Contains(d.Data))
            .Select(d => d.CirculacaoId).Distinct().ToListAsync(ct)).ToHashSet();

        var periodos = (await db.CirculacaoHorariosTic.AsNoTracking()
                .Where(h => ids.Contains(h.CirculacaoId) && h.DataInicio <= ate && h.DataFim >= de.AddDays(-1))
                .Select(h => new { h.CirculacaoId, h.DataInicio, h.DataFim, h.Chegada, h.Partida }).ToListAsync(ct))
            .ToLookup(h => h.CirculacaoId);

        var dsemanas = new Dictionary<int, string>();
        // Por blocos de um mês, para não carregar um ano inteiro de dias de uma vez.
        for (var inicio = de; inicio <= ate; inicio = inicio.AddMonths(1))
        {
            var fimMes = inicio.AddMonths(1).AddDays(-1);
            var fim = fimMes < ate ? fimMes : ate;
            // Um dia de circulação antes: a saída pode ser já no dia seguinte.
            var desde = inicio.AddDays(-1);
            var dias = await db.CirculacaoDias.AsNoTracking()
                .Where(d => ids.Contains(d.CirculacaoId) && d.Data >= desde && d.Data <= fim)
                .Select(d => new { d.CirculacaoId, d.Data }).ToListAsync(ct);

            var linhas = new List<LinhaPlaneamento>();
            foreach (var d in dias)
            {
                var c = infos[d.CirculacaoId];
                var p = periodos[c.Id].FirstOrDefault(h => h.DataInicio <= d.Data && d.Data <= h.DataFim);
                var (chegada, partida) = p is not null ? (p.Chegada, p.Partida) : (c.Chegada, c.Partida);
                if (OcupacaoTerminal.Permanencia(d.Data, chegada, partida, c.TempoEmbarque ?? permanenciaPorOmissao) is not { } perm) continue;
                var saida = DateOnly.FromDateTime(perm.Fim);
                if (saida < inicio || saida > fim) continue;
                var lim = limites.GetValueOrDefault(c.Id);
                if (!dsemanas.TryGetValue(c.Id, out var ds))
                    dsemanas[c.Id] = ds = DSemana(semana[c.Id].ToHashSet(), emFeriado.Contains(c.Id), feriados, lim?.Primeiro, lim?.Ultimo);
                linhas.Add(Linha(c, perm.Inicio, perm.Fim, ds, lim?.Ultimo ?? saida));
            }

            if (calcularCais) linhas = AtribuirCais(linhas, infos);
            foreach (var linha in linhas.OrderBy(x => x.Saida).ThenBy(x => x.Entrada).ThenBy(x => x.Trip, StringComparer.Ordinal))
                yield return linha;
        }
    }

    private static LinhaPlaneamento Linha(Info c, DateTime entrada, DateTime saida, string dsemana, DateOnly dataFim)
    {
        static string M(string? s) => (s ?? "").Trim().ToUpper(CultureInfo.GetCultureInfo("pt-PT"));
        var nome = !string.IsNullOrWhiteSpace(c.DesignacaoLinha) ? c.DesignacaoLinha : $"{c.Origem} - {c.Destino}";
        return new LinhaPlaneamento(M(nome), c.IdTic ?? "", "", "", dsemana, entrada, saida, "",
            M(c.Operador), M(c.Destino), M(c.Origem), c.ViagemId,
            c.Ambito == Ambito.Internacional ? "INTERNACIONAL" : "EXPRESSO", dataFim,
            c.Autorizacao ?? "", c.AutorizacaoValidade, c.Alvara ?? "", c.AlvaraValidade, c.IdDcp ?? "", UmaLinha(c.Comentarios), "", c.Id);
    }

    /// <summary>
    /// dsemanat: 8 posições (Seg, Ter, Qua, Qui, Sex, Sáb, Dom, Feriados), 1 = realiza-se.
    /// Feriados: 1 se se realiza em algum feriado nacional do seu calendário; sem feriados no período, igual ao domingo.
    /// </summary>
    private static string DSemana(HashSet<DayOfWeek> dias, bool emFeriado, List<DateOnly> feriados, DateOnly? primeiro, DateOnly? ultimo)
    {
        var ordem = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday };
        var s = new StringBuilder(8);
        foreach (var d in ordem) s.Append(dias.Contains(d) ? '1' : '0');
        var haFeriados = primeiro is { } a && ultimo is { } b && feriados.Any(f => f >= a && f <= b);
        s.Append(haFeriados ? (emFeriado ? '1' : '0') : s[6]);
        return s.ToString();
    }

    /// <summary>Cais de cada passagem pelo planeamento do dia (como na simulação); sem cais fica vazio.</summary>
    private List<LinhaPlaneamento> AtribuirCais(List<LinhaPlaneamento> linhas, Dictionary<int, Info> infos)
    {
        var resultado = new List<LinhaPlaneamento>(linhas.Count);
        foreach (var dia in linhas.GroupBy(l => l.Data))
        {
            var inicioDia = dia.Key.ToDateTime(TimeOnly.MinValue);
            var lista = dia.ToList();
            var autocarros = lista.Select((l, i) => new AutocarroDto(i.ToString(CultureInfo.InvariantCulture),
                infos[l.CirculacaoId].Categoria ?? ZonaCais.Comum,
                (int)(l.Entrada - inicioDia).TotalMinutes, (int)(l.Saida - inicioDia).TotalMinutes, null)).ToList();
            var plano = cais.PlanearDia(autocarros);
            var porId = plano?.Alocacoes.ToDictionary(a => a.AutocarroId, a => a.Cais) ?? [];
            resultado.AddRange(lista.Select((l, i) => porId.TryGetValue(i.ToString(CultureInfo.InvariantCulture), out var n) ? l with { Cais = n.ToString() } : l));
        }
        return resultado;
    }

    private static string UmaLinha(string? s) => string.Join(" ", (s ?? "").Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)).Trim();

    /// <summary>Escreve o CSV (separador ";", ISO-8859-1, CRLF), com o cabeçalho do ficheiro original.</summary>
    public static async Task EscreverCsvAsync(Stream destino, IAsyncEnumerable<LinhaPlaneamento> linhas, CancellationToken ct = default)
    {
        await using var w = new StreamWriter(destino, Encoding.Latin1, 64 * 1024, leaveOpen: true) { NewLine = "\r\n" };
        await w.WriteLineAsync(string.Join(";", Colunas) + ";");
        var n = 0;
        await foreach (var l in linhas.WithCancellation(ct))
        {
            await w.WriteLineAsync(string.Join(";", Campos(l)) + ";");
            if (++n % 5000 == 0) await w.FlushAsync(ct);
        }
        await w.FlushAsync(ct);
    }

    public static IEnumerable<string> Campos(LinhaPlaneamento l)
    {
        static string D(DateOnly? d) => d?.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture) ?? "";
        static string T(DateTime t) => t.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        static string Limpo(string s) => s.Replace(';', ',').Replace('"', '\'');
        return new[]
        {
            l.LinhaNome, l.Linha, l.Matricula, l.Cais, l.DSemana, T(l.Entrada), T(l.Saida), l.Mov, l.Operadora, l.Destino, l.Origem, l.Trip,
            l.Tipologia, D(l.Data), D(l.DataFim), l.Autoriza, D(l.ValidadeAutoriza), l.AutorizaComunitaria, D(l.ValidadeAc), l.Obs1, l.Obs2, l.Obs3
        }.Select(Limpo);
    }

    /// <summary>Feriados nacionais de Portugal num ano (fixos, Sexta-feira Santa, Páscoa e Corpo de Deus).</summary>
    public static IEnumerable<DateOnly> Feriados(int ano)
    {
        var pascoa = Pascoa(ano);
        return new[]
        {
            new DateOnly(ano, 1, 1), pascoa.AddDays(-2), pascoa, new DateOnly(ano, 4, 25), new DateOnly(ano, 5, 1), pascoa.AddDays(60),
            new DateOnly(ano, 6, 10), new DateOnly(ano, 8, 15), new DateOnly(ano, 10, 5), new DateOnly(ano, 11, 1), new DateOnly(ano, 12, 1),
            new DateOnly(ano, 12, 8), new DateOnly(ano, 12, 25)
        };
    }

    /// <summary>Domingo de Páscoa (algoritmo de Meeus/Jones/Butcher).</summary>
    public static DateOnly Pascoa(int ano)
    {
        int a = ano % 19, b = ano / 100, c = ano % 100, d = b / 4, e = b % 4, f = (b + 8) / 25, g = (b - f + 1) / 3,
            h = (19 * a + b - d - g + 15) % 30, i = c / 4, k = c % 4, l = (32 + 2 * e + 2 * i - h - k) % 7, m = (a + 11 * h + 22 * l) / 451;
        var mes = (h + l - 7 * m + 114) / 31;
        var dia = (h + l - 7 * m + 114) % 31 + 1;
        return new DateOnly(ano, mes, dia);
    }
}
