using System.IO.Compression;
using System.Security;
using System.Text;
using AppTIC.Web.Data;
using AppTIC.Web.Models.Domain;
using AppTIC.Web.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace AppTIC.Web.Tests;

public class ExcelImportTests : IDisposable
{
    private readonly string _pasta = Directory.CreateTempSubdirectory("apptic-excel-").FullName;

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    private sealed class Admin : ICurrentUser
    {
        public string? UserId => "admin";
        public string? UserName => "admin";
        public bool IsAdmin => true;
        public int? OperadorId => null;
        public string? IpAddress => null;
    }

    private sealed class SemRegisto : IActivityLog
    {
        public Task LogAsync(string categoria, string acao, string mensagem, NivelRegisto nivel = NivelRegisto.Info,
            int? gtfsFeedId = null, string? ownerId = null, string? userName = null) => Task.CompletedTask;
    }

    // ---- Folha de teste: 1 operador, 3 paragens, 4 circulações (uma com viagem_id repetido), 3 dias ----

    private static readonly object?[][] Operadores =
    [
        ["operador_nif", "operador_designação", "operador_alvará (licença comunitária)", "alvará_validade", "âmbito", "operador_abreviatura", "comentários"],
        [111, "Operador Um, Lda", 200437, 46441, "nacional", "OP1"],
    ];

    private static readonly object?[][] Paragens =
    [
        ["paragem_id", "paragem_nome", "latitude", "longitude", "comentários"],
        [100, "Porto (Campanhã)", 41.15119, -8.58277],
        [1, "Lisboa (Sete Rios)", 38.741790000000002, -9.1663200000000007],
        [201, "Bragança", 41.809444, -6.760028],
    ];

    private static object?[][] Circulacoes(bool comSegunda = true)
    {
        var linhas = new List<object?[]>
        {
            new object?[] { "nif do operador", "designação do operador" },
            new object?[] { "operador_nif", "operador", "linha_id", "designação_linha", "âmbito", "autorização", "aut_validade", "declaração_capacidade",
                "id_DCP", "viagem_id", "ID_TIC", "origem_id", "origem", "destino_id", "destino", "chegada", "partida",
                "tempo embarque/desembarque (em caso de término)", "comentários" },
            // 008-01: Bragança → TIC (12:00–12:15) → Lisboa
            new object?[] { 111, "OP1", 8, "Bragança/Lisboa", "NACIONAL", "AUT. 1/2026", 46399, "sim", 50, "008-01", 9001, 201, "Bragança", 1, "Lisboa", 0.5, 0.51041666666666663, 9.7222222222221877E-3 },
            // X1 repete-se com ID_TIC diferentes
            new object?[] { 111, "OP1", 9, "Porto/Lisboa", "INTERNACIONAL", null, null, "não", null, "X1", 9003, 100, "Porto", 1, "Lisboa", null, 0.75, 0.0104166666 },
            new object?[] { 111, "OP1", 9, "Porto/Lisboa", "INTERNACIONAL", null, null, "não", null, "X1", 9004, 100, "Porto", 1, "Lisboa", null, 0.875, 0.0104166666 },
        };
        // 008-02: Lisboa → TIC (chegada 06:00, término)
        if (comSegunda)
            linhas.Insert(3, [111, "OP1", 8, "Lisboa/Porto", "NACIONAL", null, null, "sim", 50, "008-02", 9002, 1, "Lisboa", 100, "Porto", 0.25, null, 9.7222222222221877E-3]);
        return [.. linhas];
    }

    private static readonly object?[][] Calendario =
    [
        [null, "viagem_id (folha \"circulações\")", "realiza-se? (sim=1;não=0)"],
        ["id_TIC", "id_viagem/data", 46296, 46297, 46298], // 01, 02 e 03/10/2026
        [9001, "008-01", 1, new Erro(), 1],
        [9002, "008-02", 0, 1, 0],
        [9003, "X1", 1, 1, 1],
        [9004, "X1", 0, 0, 1],
    ];

    private static readonly object?[][] Percursos =
    [
        [null, "viagem_id (folha \"circulações\")"],
        ["ID_TIC", "id_viagem/sequência", 1, 2, 3],
        [9001, "008-01", 201, 100, 1],
        [9002, "008-02", 1, 100],
        [9003, "X1", 100, 1],
        [9004, "X1", 100, 1],
    ];

    /// <summary>Célula com erro de fórmula (#N/A).</summary>
    private sealed record Erro;

    private string Ficheiro(string nome, bool comSegunda = true)
    {
        var path = Path.Combine(_pasta, nome);
        (string Nome, object?[][] Linhas)[] folhas =
        [
            ("info_operador", Operadores), ("paragens", Paragens), ("circulações", Circulacoes(comSegunda)),
            ("calendário", Calendario), ("percursos", Percursos)
        ];
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        void Escrever(string parte, string xml)
        {
            using var w = new StreamWriter(zip.CreateEntry(parte).Open(), new UTF8Encoding(false));
            w.Write("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" + xml);
        }
        const string ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        const string rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        Escrever("xl/workbook.xml", $"<workbook xmlns=\"{ns}\" xmlns:r=\"{rel}\"><sheets>"
            + string.Concat(folhas.Select((f, i) => $"<sheet name=\"{SecurityElement.Escape(f.Nome)}\" sheetId=\"{i + 1}\" r:id=\"rId{i + 1}\"/>"))
            + "</sheets></workbook>");
        Escrever("xl/_rels/workbook.xml.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
            + string.Concat(folhas.Select((_, i) => $"<Relationship Id=\"rId{i + 1}\" Type=\"{rel}/worksheet\" Target=\"worksheets/sheet{i + 1}.xml\"/>"))
            + "</Relationships>");
        for (var f = 0; f < folhas.Length; f++)
        {
            var sb = new StringBuilder($"<worksheet xmlns=\"{ns}\"><sheetData>");
            for (var l = 0; l < folhas[f].Linhas.Length; l++)
            {
                sb.Append($"<row r=\"{l + 1}\">");
                for (var c = 0; c < folhas[f].Linhas[l].Length; c++)
                {
                    var r = $"{(char)('A' + c)}{l + 1}";
                    sb.Append(folhas[f].Linhas[l][c] switch
                    {
                        null => "",
                        Erro => $"<c r=\"{r}\" t=\"e\"><f>VLOOKUP(1,A:A,1,0)</f><v>#N/A</v></c>",
                        string s => $"<c r=\"{r}\" t=\"inlineStr\"><is><t>{SecurityElement.Escape(s)}</t></is></c>",
                        IFormattable n => $"<c r=\"{r}\"><v>{n.ToString(null, System.Globalization.CultureInfo.InvariantCulture)}</v></c>",
                        var o => throw new InvalidOperationException(o.ToString())
                    });
                }
                sb.Append("</row>");
            }
            Escrever($"xl/worksheets/sheet{f + 1}.xml", sb.Append("</sheetData></worksheet>").ToString());
        }
        return path;
    }

    [Fact]
    public void LeAFolhaDePlaneamento()
    {
        var dados = ExcelPlaneamento.Ler(Ficheiro("plano.xlsx"));

        var op = Assert.Single(dados.Operadores.Values);
        Assert.Equal(("111", "Operador Um, Lda", "200437", new DateOnly(2027, 2, 23), Ambito.Nacional, "OP1"),
            (op.Nif, op.Designacao, op.Alvara, op.AlvaraValidade!.Value, op.Ambito!.Value, op.Abreviatura));
        Assert.Equal(3, dados.Paragens.Count);
        Assert.Equal(38.74179m, dados.Paragens["1"].Latitude);
        Assert.Equal((new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 3)), (dados.Inicio!.Value, dados.Fim!.Value));

        Assert.Equal(4, dados.Circulacoes.Count);
        var a = dados.Circulacoes.Single(c => c.ViagemId == "008-01");
        Assert.Equal((new TimeOnly(12, 0), new TimeOnly(12, 15), 14, true, "50", "9001", new DateOnly(2027, 1, 12)),
            (a.Chegada!.Value, a.Partida!.Value, a.TempoEmbarqueMinutos!.Value, a.DeclaracaoCapacidade, a.IdDcp, a.IdTic, a.AutorizacaoValidade!.Value));
        Assert.Equal(["201", "100", "1"], a.Percurso);
        // A célula com #N/A conta como "não se realiza".
        Assert.Equal([new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 3)], a.Dias);
        Assert.Null(dados.Circulacoes.Single(c => c.ViagemId == "008-02").Partida);

        // viagem_id repetido: distingue-se pelo ID_TIC, e cada linha fica com o seu calendário.
        Assert.Equal(3, dados.Circulacoes.Single(c => c.ViagemId == "X1/9003").Dias.Count);
        Assert.Equal([new DateOnly(2026, 10, 3)], dados.Circulacoes.Single(c => c.ViagemId == "X1/9004").Dias);
        Assert.All(dados.Circulacoes, c => Assert.Null(c.Erro));
        Assert.Contains(dados.Avisos, x => x.Contains("X1"));
        Assert.Contains(dados.Avisos, x => x.Contains("#N/A"));
    }

    [Fact]
    public void RejeitaFicheiroQueNaoEAFolhaDePlaneamento()
    {
        var path = Path.Combine(_pasta, "outro.xlsx");
        File.WriteAllText(path, "não é um zip");
        Assert.Throws<InvalidDataException>(() => ExcelPlaneamento.Ler(path));
    }

    [Fact]
    public async Task AplicaAtualizaEEliminaOLote()
    {
        await using var conexao = new SqliteConnection("Data Source=:memory:");
        await conexao.OpenAsync();
        var opcoes = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(conexao).UseSnakeCaseNamingConvention().Options;
        await using var db = new ApplicationDbContext(opcoes, new Admin());
        await db.Database.EnsureCreatedAsync();
        db.Users.Add(new ApplicationUser { Id = "admin", UserName = "admin" });
        // Circulação feita à mão, fora de qualquer lote: nunca é tocada pela eliminação do lote.
        var outro = new Operador { Nif = "999", Designacao = "Outro", OwnerId = "admin" };
        db.Circulacoes.Add(new Circulacao { ViagemId = "MANUAL", OwnerId = "admin", Operador = outro, Partida = new TimeOnly(8, 0) });
        var lote = new Importacao { OwnerId = "admin", Origem = ImportacaoOrigem.Excel, Nome = "Plano", NomeFicheiro = "plano.xlsx" };
        db.Importacoes.Add(lote);
        await db.SaveChangesAsync();

        var gtfsOpcoes = Options.Create(new GtfsOptions { StoragePath = _pasta, ParagensTicPorOmissao = ["100"] });
        var servico = new ExcelImportService(db, gtfsOpcoes, new SemRegisto());
        var dados = ExcelPlaneamento.Ler(Ficheiro("plano.xlsx"));

        var previsto = await servico.PreverAsync(lote, dados);
        Assert.Equal((1, 3, 4, 0, 0), (previsto.OperadoresNovos, previsto.ParagensNovas, previsto.Novas, previsto.Atualizar, previsto.EmConflito));

        var r = await servico.AplicarAsync(lote, dados, removerAusentes: true);
        Assert.Equal((1, 3, 4, 0, 0, 7), (r.OperadoresNovos, r.ParagensNovas, r.CirculacoesNovas, r.CirculacoesAtualizadas, r.Ignoradas, r.Dias));
        Assert.Equal((ImportacaoEstado.Aplicada, 1, 4), (lote.Estado, lote.Aplicacoes, lote.NumCirculacoes));

        db.ChangeTracker.Clear();
        var a = await db.Circulacoes.Include(c => c.Percurso.OrderBy(p => p.Sequencia)).ThenInclude(p => p.Paragem)
            .Include(c => c.Dias).Include(c => c.HorariosTic).Include(c => c.Operador).Include(c => c.Origem)
            .SingleAsync(c => c.ViagemId == "008-01");
        Assert.Equal((lote.Id, "111", "201", new TimeOnly(12, 0), new TimeOnly(12, 15), 14, "AUT. 1/2026"),
            (a.ImportacaoId, a.Operador!.Nif, a.Origem!.Codigo, a.Chegada!.Value, a.Partida!.Value, a.TempoEmbarqueMinutos, a.Autorizacao));
        // O percurso guarda-se a partir da paragem do TIC.
        Assert.Equal(["100", "1"], a.Percurso.Select(p => p.Paragem!.Codigo));
        Assert.Equal(2, a.Dias.Count);
        var periodo = Assert.Single(a.HorariosTic);
        Assert.Equal((OrigemHorario.Excel, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 3)), (periodo.Origem, periodo.DataInicio, periodo.DataFim));

        // Revisão do ficheiro sem a 008-02: as restantes são atualizadas (sem duplicar dias) e a ausente sai do lote.
        db.ChangeTracker.Clear(); // como num pedido novo: sem as entidades lidas nas verificações acima
        lote = await db.Importacoes.SingleAsync();
        var revisao = ExcelPlaneamento.Ler(Ficheiro("plano_rev2.xlsx", comSegunda: false));
        var previsto2 = await servico.PreverAsync(lote, revisao);
        Assert.Equal((0, 0, 0, 3, 1), (previsto2.OperadoresNovos, previsto2.ParagensNovas, previsto2.Novas, previsto2.Atualizar, previsto2.Ausentes));
        var r2 = await servico.AplicarAsync(lote, revisao, removerAusentes: true);
        Assert.Equal((0, 3, 1, 6), (r2.CirculacoesNovas, r2.CirculacoesAtualizadas, r2.Removidas, r2.Dias));
        db.ChangeTracker.Clear();
        Assert.Equal(3, await db.Circulacoes.CountAsync(c => c.ImportacaoId == lote.Id));
        Assert.Equal(6, await db.CirculacaoDias.CountAsync());
        Assert.False(await db.Circulacoes.AnyAsync(c => c.ViagemId == "008-02"));

        // Eliminar o lote apaga só as suas circulações.
        lote = await db.Importacoes.SingleAsync();
        var eliminadas = await new ImportacaoService(db, gtfsOpcoes, null!, new SemRegisto()).EliminarAsync(lote);
        Assert.Equal(3, eliminadas);
        db.ChangeTracker.Clear();
        Assert.Equal("MANUAL", (await db.Circulacoes.SingleAsync()).ViagemId);
        Assert.Empty(await db.Importacoes.ToListAsync());
        Assert.Empty(await db.CirculacaoDias.ToListAsync());
        Assert.Equal(3, await db.Paragens.CountAsync());
    }

    [Fact]
    public void ComparaComOGtfsPeloCodigoDentroDoTripId()
    {
        var dados = ExcelPlaneamento.Ler(Ficheiro("plano.xlsx"));
        var gtfs = new GtfsDadosImportacao();
        gtfs.Rotas["5"] = new GtfsRota("5", "Expresso:008", null);
        // 008-01: mesma chegada, partida 5 min depois, e realiza-se em 01 e 02/10 (no Excel: 01 e 03/10).
        gtfs.Viagens.Add(new GtfsViagem("101750:00801:63:0", "5", "S1", ["201", "100", "1"], new TimeOnly(12, 0), new TimeOnly(12, 20), 0));
        gtfs.Servicos["S1"] = [new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 2)];
        // 008-02: igual.
        gtfs.Viagens.Add(new GtfsViagem("101796:00802:63:0", "5", "S2", ["1", "100"], new TimeOnly(6, 0), null, 0));
        gtfs.Servicos["S2"] = [new DateOnly(2026, 10, 2), new DateOnly(2026, 12, 25)]; // 25/12 fora do período do Excel
        // Viagem que não está no Excel.
        gtfs.Viagens.Add(new GtfsViagem("101999:01100:63:0", "5", "S1", ["100", "1"], null, new TimeOnly(9, 0), 0));

        var c = ExcelGtfsComparacao.Comparar(dados, gtfs);

        Assert.Equal((new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 3)), (c.Inicio!.Value, c.Fim!.Value));
        Assert.Equal((1, 1, 2, 1), (c.Conta(ComparacaoGtfs.Igual), c.Conta(ComparacaoGtfs.Diferente), c.Conta(ComparacaoGtfs.SoExcel), c.Conta(ComparacaoGtfs.SoGtfs)));

        var dif = c.Linhas.Single(l => l.Estado == ComparacaoGtfs.Diferente);
        Assert.Equal(("008-01", "101750:00801:63:0", "12:15", "12:20", 2, 2, 1, 1),
            (dif.ViagemId, dif.TripId, dif.PartidaExcel, dif.PartidaGtfs, dif.DiasExcel, dif.DiasGtfs, dif.DiasSoExcel, dif.DiasSoGtfs));
        Assert.Contains("Partida: Excel 12:15, GTFS 12:20", dif.Diferencas);
        Assert.Contains("03/10", dif.Diferencas);
        Assert.DoesNotContain("Percurso", dif.Diferencas);

        Assert.Equal("008-02", c.Linhas.Single(l => l.Estado == ComparacaoGtfs.Igual).ViagemId);
        Assert.Equal("101999:01100:63:0", c.Linhas.Single(l => l.Estado == ComparacaoGtfs.SoGtfs).TripId);
        Assert.Equal(["101750:00801:63:0", "101796:00802:63:0"], c.TripIdsCorrespondidos.Order());
    }
}
