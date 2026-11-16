using AppTIC.Web.Data;
using AppTIC.Web.Models.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AppTIC.Web.Services;

public record ExcelImportOperadorPreview(string Nif, string Nome, bool Novo, int Circulacoes, int Novas, int Atualizar, int Conflitos, int Dias);

public record ExcelImportPreview(
    int OperadoresNovos, int ParagensNovas, int ParagensAtualizar, int ParagensIguais,
    IReadOnlyList<ExcelImportOperadorPreview> Operadores, IReadOnlyList<string> Conflitos, IReadOnlyList<string> Avisos,
    DateOnly? Inicio, DateOnly? Fim, int Dias, int Ausentes)
{
    public int Novas => Operadores.Sum(o => o.Novas);
    public int Atualizar => Operadores.Sum(o => o.Atualizar);
    public int EmConflito => Operadores.Sum(o => o.Conflitos);
}

public record ExcelImportResultado(int OperadoresNovos, int ParagensNovas, int ParagensAtualizadas, int CirculacoesNovas,
    int CirculacoesAtualizadas, int Ignoradas, int Removidas, int Dias);

/// <summary>
/// Importa operadores, paragens, circulações (com percurso) e calendário a partir da folha Excel de planeamento.
/// Operador ↔ NIF; Paragem ↔ paragem_id (Código); Circulação ↔ viagem_id (ID de viagem). Voltar a importar atualiza os existentes.
/// </summary>
public class ExcelImportService(ApplicationDbContext db, IOptions<GtfsOptions> options, IActivityLog log)
{
    private const int MaxConflitos = 50;

    /// <summary>O que cada linha da folha "circulações" vai fazer: criar, atualizar ou ficar de fora (conflito).</summary>
    private sealed record Plano(
        Dictionary<string, Operador> Operadores,
        Dictionary<string, Paragem> Paragens,
        List<(ExcelCirculacao Excel, Circulacao? Existente, string? Conflito)> Linhas);

    public async Task<ExcelImportPreview> PreverAsync(Importacao importacao, ExcelDados dados, CancellationToken ct = default)
    {
        var plano = await PlanearAsync(importacao.OwnerId, dados, seguir: false, ct);

        int paragensNovas = 0, paragensAtualizar = 0, paragensIguais = 0;
        foreach (var p in dados.Paragens.Values)
        {
            if (!plano.Paragens.TryGetValue(p.Codigo, out var e)) paragensNovas++;
            else if (Difere(e, p)) paragensAtualizar++;
            else paragensIguais++;
        }

        var operadores = plano.Linhas.Where(l => l.Excel.Nif.Length > 0).GroupBy(l => l.Excel.Nif, StringComparer.OrdinalIgnoreCase).Select(g =>
        {
            var nome = plano.Operadores.TryGetValue(g.Key, out var o) ? o.Abreviatura ?? o.Designacao
                : dados.Operadores.TryGetValue(g.Key, out var x) ? x.Abreviatura ?? x.Designacao
                : g.First().Excel.OperadorNome ?? g.Key;
            var validas = g.Where(l => l.Conflito is null).ToList();
            return new ExcelImportOperadorPreview(g.Key, nome, o is null, g.Count(), validas.Count(l => l.Existente is null),
                validas.Count(l => l.Existente is not null), g.Count() - validas.Count, validas.Sum(l => l.Excel.Dias.Count));
        }).OrderBy(o => o.Nome, StringComparer.CurrentCultureIgnoreCase).ToList();

        var conflitos = plano.Linhas.Where(l => l.Conflito is not null)
            .Select(l => $"Linha {l.Excel.Linha}, viagem {l.Excel.ViagemId}: {l.Conflito}.").ToList();
        if (conflitos.Count > MaxConflitos)
            conflitos = [.. conflitos.Take(MaxConflitos), $"… e mais {conflitos.Count - MaxConflitos}."];

        var ids = plano.Linhas.Where(l => l.Conflito is null).Select(l => l.Excel.ViagemId).ToList();
        var ausentes = importacao.Id == 0 ? 0
            : await db.Circulacoes.CountAsync(c => c.ImportacaoId == importacao.Id && !ids.Contains(c.ViagemId), ct);

        return new ExcelImportPreview(operadores.Count(o => o.Novo), paragensNovas, paragensAtualizar, paragensIguais,
            operadores, conflitos, dados.Avisos, dados.Inicio, dados.Fim, operadores.Sum(o => o.Dias), ausentes);
    }

    /// <summary>
    /// Aplica a folha: cria os operadores e paragens em falta, cria ou atualiza as circulações (percurso, horas no TIC e
    /// dias dentro do intervalo do calendário da folha) e marca-as com o Id da importação.
    /// Com <paramref name="removerAusentes"/>, elimina as circulações desta importação que já não constam do ficheiro.
    /// </summary>
    public async Task<ExcelImportResultado> AplicarAsync(Importacao importacao, ExcelDados dados, bool removerAusentes, CancellationToken ct = default)
    {
        var hoje = DateOnly.FromDateTime(DateTime.Today);
        var owner = importacao.OwnerId;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var plano = await PlanearAsync(owner, dados, seguir: true, ct);
        var aImportar = plano.Linhas.Where(l => l.Conflito is null).ToList();

        // 1) Operadores em falta (os existentes não são alterados)
        var operadoresNovos = 0;
        foreach (var nif in aImportar.Select(l => l.Excel.Nif).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (plano.Operadores.ContainsKey(nif)) continue;
            var x = dados.Operadores.GetValueOrDefault(nif);
            var o = new Operador
            {
                OwnerId = owner, Nif = nif,
                Designacao = x?.Designacao ?? aImportar.First(l => l.Excel.Nif.Equals(nif, StringComparison.OrdinalIgnoreCase)).Excel.OperadorNome ?? nif,
                Alvara = x?.Alvara, AlvaraValidade = x?.AlvaraValidade, Ambito = x?.Ambito, Abreviatura = x?.Abreviatura, Comentarios = x?.Comentarios
            };
            db.Operadores.Add(o);
            plano.Operadores[nif] = o;
            operadoresNovos++;
        }

        // 2) Paragens da folha "paragens"
        int paragensNovas = 0, paragensAtualizadas = 0;
        foreach (var x in dados.Paragens.Values)
        {
            if (!plano.Paragens.TryGetValue(x.Codigo, out var p))
            {
                plano.Paragens[x.Codigo] = p = new Paragem { OwnerId = owner, Codigo = x.Codigo };
                db.Paragens.Add(p);
                paragensNovas++;
            }
            else if (Difere(p, x)) paragensAtualizadas++;
            else continue;
            p.Nome = x.Nome;
            p.Latitude = x.Latitude;
            p.Longitude = x.Longitude;
            if (x.Comentarios is not null) p.Comentarios = x.Comentarios;
        }
        await db.SaveChangesAsync(ct);

        // 3) Circulações existentes: substitui o percurso e os dias dentro do intervalo do calendário da folha
        var idsAtualizar = aImportar.Where(l => l.Existente is not null).Select(l => l.Existente!.Id).ToList();
        await db.Percursos.Where(p => idsAtualizar.Contains(p.CirculacaoId)).ExecuteDeleteAsync(ct);
        if (dados.Inicio is { } inicio && dados.Fim is { } fim)
            await db.CirculacaoDias.Where(d => idsAtualizar.Contains(d.CirculacaoId) && d.Data >= inicio && d.Data <= fim).ExecuteDeleteAsync(ct);

        // 4) Circulações, percurso e calendário
        int novas = 0, totalDias = 0;
        var ticPorOperador = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (x, existente, _) in aImportar)
        {
            var operador = plano.Operadores[x.Nif];
            var c = existente;
            if (c is null)
            {
                c = new Circulacao { OwnerId = owner, ViagemId = x.ViagemId };
                db.Circulacoes.Add(c);
                novas++;
            }
            c.Operador = operador;
            c.LinhaId = x.LinhaId;
            c.DesignacaoLinha = x.DesignacaoLinha;
            c.Ambito = x.Ambito ?? c.Ambito ?? operador.Ambito;
            c.Autorizacao = x.Autorizacao;
            c.AutorizacaoValidade = x.AutorizacaoValidade;
            c.DeclaracaoCapacidade = x.DeclaracaoCapacidade;
            c.IdDcp = x.IdDcp;
            c.IdTic = x.IdTic;
            c.Origem = x.OrigemCodigo is null ? null : plano.Paragens[x.OrigemCodigo];
            c.Destino = x.DestinoCodigo is null ? null : plano.Paragens[x.DestinoCodigo];
            c.TempoEmbarqueMinutos = x.TempoEmbarqueMinutos;
            if (x.Comentarios is not null || existente is null) c.Comentarios = x.Comentarios ?? $"Importado do Excel \"{importacao.NomeFicheiro}\".";
            c.Importacao = importacao;

            // Percurso a partir da paragem do TIC (inclusive), como na importação do GTFS.
            if (!ticPorOperador.TryGetValue(x.Nif, out var tic))
                ticPorOperador[x.Nif] = tic = ParagensTic(operador).ToHashSet(StringComparer.Ordinal);
            var noTic = Math.Max(0, x.Percurso.FindIndex(tic.Contains));
            c.Percurso = x.Percurso.Skip(noTic).Select((p, i) => new PercursoParagem { Sequencia = i + 1, Paragem = plano.Paragens[p] }).ToList();

            totalDias += x.Dias.Count;
            c.Dias = x.Dias.Select(d => new CirculacaoDia { Data = d }).ToList();
            // Horas no TIC para o período dos dias importados; os períodos editados à mão mantêm-se.
            if (x.Dias.Count > 0)
                HorariosTic.Definir(c, x.Dias.Min, x.Dias.Max, x.Chegada, x.Partida, OrigemHorario.Excel, hoje, manterManuais: true);
            else if (c.HorariosTic.Count == 0)
            {
                c.Chegada = x.Chegada;
                c.Partida = x.Partida;
            }
        }

        importacao.Estado = ImportacaoEstado.Aplicada;
        importacao.AplicadaEm = DateTime.UtcNow;
        importacao.Aplicacoes++;
        importacao.DataInicio = dados.Inicio;
        importacao.DataFim = dados.Fim;
        importacao.NumOperadores = aImportar.Select(l => l.Excel.Nif).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        importacao.NumParagens = dados.Paragens.Count;
        importacao.NumCirculacoes = aImportar.Count;
        importacao.NumDias = totalDias;
        await db.SaveChangesAsync(ct);

        // 5) Circulações desta importação que já não constam do ficheiro
        var removidas = 0;
        if (removerAusentes)
        {
            var ids = aImportar.Select(l => l.Excel.ViagemId).ToList();
            removidas = await db.Circulacoes.Where(c => c.ImportacaoId == importacao.Id && !ids.Contains(c.ViagemId)).ExecuteDeleteAsync(ct);
        }
        await tx.CommitAsync(ct);

        var r = new ExcelImportResultado(operadoresNovos, paragensNovas, paragensAtualizadas, novas, aImportar.Count - novas,
            plano.Linhas.Count - aImportar.Count, removidas, totalDias);
        await log.LogAsync(CategoriaRegisto.Importacao, "Importação Excel",
            $"Importação #{importacao.Id} \"{importacao.Nome}\" ({importacao.NomeFicheiro}) aplicada: operadores {r.OperadoresNovos} novos; "
            + $"paragens {r.ParagensNovas} novas / {r.ParagensAtualizadas} atualizadas; circulações {r.CirculacoesNovas} novas / "
            + $"{r.CirculacoesAtualizadas} atualizadas / {r.Ignoradas} ignoradas (conflito) / {r.Removidas} removidas; {r.Dias} dias de calendário"
            + (dados.Inicio is { } i ? $" ({i:dd/MM/yyyy}–{dados.Fim:dd/MM/yyyy})." : "."),
            r.Ignoradas > 0 ? NivelRegisto.Aviso : NivelRegisto.Info, ownerId: owner);
        return r;
    }

    private async Task<Plano> PlanearAsync(string ownerId, ExcelDados dados, bool seguir, CancellationToken ct)
    {
        var nifs = dados.Circulacoes.Select(c => c.Nif).Where(n => n.Length > 0).Distinct().ToList();
        var operadores = (await Seguir(db.Operadores.Where(o => o.OwnerId == ownerId && nifs.Contains(o.Nif)), seguir).ToListAsync(ct))
            .ToDictionary(o => o.Nif, StringComparer.OrdinalIgnoreCase);

        var codigos = dados.Paragens.Keys
            .Concat(dados.Circulacoes.SelectMany(c => c.Percurso.Append(c.OrigemCodigo).Append(c.DestinoCodigo)).Where(p => p is not null).Select(p => p!))
            .Distinct().ToList();
        var paragens = await Seguir(db.Paragens.Where(p => p.OwnerId == ownerId && codigos.Contains(p.Codigo)), seguir).ToDictionaryAsync(p => p.Codigo, ct);

        var ids = dados.Circulacoes.Select(c => c.ViagemId).Distinct().ToList();
        var consulta = db.Circulacoes.Where(c => c.OwnerId == ownerId && ids.Contains(c.ViagemId));
        var existentes = await (seguir ? consulta.Include(c => c.HorariosTic) : consulta.AsNoTracking()).ToDictionaryAsync(c => c.ViagemId, ct);

        var linhas = new List<(ExcelCirculacao, Circulacao?, string?)>(dados.Circulacoes.Count);
        foreach (var x in dados.Circulacoes)
        {
            var existente = existentes.GetValueOrDefault(x.ViagemId);
            string? conflito = x.Erro;
            if (conflito is null)
            {
                var emFalta = x.Percurso.Append(x.OrigemCodigo).Append(x.DestinoCodigo)
                    .Where(p => p is not null && !dados.Paragens.ContainsKey(p) && !paragens.ContainsKey(p)).Distinct().ToList();
                if (emFalta.Count > 0)
                    conflito = $"paragem(ns) {string.Join(", ", emFalta)} não existe(m) na folha \"paragens\"";
                else if (existente is not null && (!operadores.TryGetValue(x.Nif, out var o) || o.Id != existente.OperadorId))
                    conflito = "já existe uma circulação com este ID de viagem noutro operador";
            }
            linhas.Add((x, conflito is null ? existente : null, conflito));
        }
        return new Plano(operadores, paragens, linhas);
    }

    private static IQueryable<T> Seguir<T>(IQueryable<T> q, bool seguir) where T : class => seguir ? q : q.AsNoTracking();

    /// <summary>stop_id(s) do TIC para o operador (os do operador ou, se vazio, os da configuração).</summary>
    private IReadOnlyList<string> ParagensTic(Operador operador)
    {
        var proprias = Operador.SplitStopIds(operador.GtfsParagensTic);
        return proprias.Count > 0 ? proprias : options.Value.ParagensTicPorOmissao;
    }

    private static bool Difere(Paragem e, ExcelParagem x) =>
        e.Nome != x.Nome || e.Latitude != x.Latitude || e.Longitude != x.Longitude;
}
