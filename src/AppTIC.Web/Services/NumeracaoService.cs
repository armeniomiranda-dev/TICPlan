using AppTIC.Web.Data;
using AppTIC.Web.Models.Domain;
using Microsoft.EntityFrameworkCore;

namespace AppTIC.Web.Services;

/// <summary>
/// Numeração das declarações (DCP: Ano.Numero.Versao) e do ID TIC.
/// <list type="bullet">
/// <item>Cada nova circulação recebe uma nova declaração: número seguinte no ano, versão 00.</item>
/// <item>A versão sobe quando muda o horário de pelo menos uma circulação da declaração.</item>
/// <item>ID TIC = prefixo do operador (ou do seu grupo) seguido de 3 dígitos sequenciais (8 → 8001, 8002, …).</item>
/// </list>
/// A numeração é global (não depende do login): as consultas ignoram os filtros.
/// </summary>
public class NumeracaoService(ApplicationDbContext db, ICurrentUser currentUser)
{
    public const int DigitosIdTic = 3;

    /// <summary>
    /// Ano das declarações em vigor: o mais recente com declarações (o ano seguinte só abre com a renovação);
    /// sem declarações, o ano atual.
    /// </summary>
    public async Task<int> AnoAtualAsync(CancellationToken ct = default) =>
        await db.Declaracoes.IgnoreQueryFilters().MaxAsync(d => (int?)d.Ano, ct) ?? DateTime.Today.Year;

    /// <summary>Cria uma nova declaração (número seguinte no ano; por omissão o ano em vigor). Fica por gravar (SaveChanges do chamador).</summary>
    public async Task<Declaracao> NovaDeclaracaoAsync(Operador operador, string motivo, int? ano = null, CancellationToken ct = default)
    {
        var a = ano ?? await AnoAtualAsync(ct);
        var ultimo = await db.Declaracoes.IgnoreQueryFilters().Where(d => d.Ano == a).MaxAsync(d => (int?)d.Numero, ct) ?? 0;
        // Declarações criadas neste contexto e ainda não gravadas.
        var pendentes = db.ChangeTracker.Entries<Declaracao>().Where(e => e.State == EntityState.Added && e.Entity.Ano == a)
            .Select(e => e.Entity.Numero).DefaultIfEmpty(0).Max();
        var d = new Declaracao
        {
            Ano = a, Numero = Math.Max(ultimo, pendentes) + 1, Versao = 0,
            OperadorId = operador.GrupoId ?? (operador.Id == 0 ? null : operador.Id),
            Operador = operador.GrupoId is null && operador.Id == 0 ? operador : null
        };
        d.Versoes.Add(new DeclaracaoVersao { Versao = 0, Motivo = motivo, Utilizador = currentUser.UserName });
        db.Declaracoes.Add(d);
        return d;
    }

    /// <summary>Associa a circulação a uma nova declaração.</summary>
    public async Task<Declaracao> AtribuirNovaDeclaracaoAsync(Circulacao c, Operador operador, string motivo, int? ano = null, CancellationToken ct = default)
    {
        var d = await NovaDeclaracaoAsync(operador, motivo, ano, ct);
        c.Declaracao = d;
        c.IdDcp = d.Codigo;
        return d;
    }

    /// <summary>
    /// Nova versão da declaração da circulação (mudança de horário). Atualiza o código em todas as circulações da declaração.
    /// Sem declaração, não faz nada e devolve null.
    /// </summary>
    public async Task<Declaracao?> NovaVersaoAsync(Circulacao c, string motivo, CancellationToken ct = default)
    {
        if (c.DeclaracaoId is not { } id) return null;
        var d = await db.Declaracoes.IgnoreQueryFilters().FirstAsync(x => x.Id == id, ct);
        d.Versao++;
        d.AtualizadoEm = DateTime.UtcNow;
        d.Versoes.Add(new DeclaracaoVersao { Versao = d.Versao, Motivo = motivo, Utilizador = currentUser.UserName, CirculacaoId = c.Id });
        await SincronizarCodigoAsync(d, ct);
        return d;
    }

    /// <summary>Copia o código da declaração para as circulações (as carregadas e as da base de dados).</summary>
    public async Task SincronizarCodigoAsync(Declaracao d, CancellationToken ct = default)
    {
        var codigo = d.Codigo;
        foreach (var e in db.ChangeTracker.Entries<Circulacao>().Where(e => e.Entity.DeclaracaoId == d.Id && d.Id != 0))
            e.Entity.IdDcp = codigo;
        if (d.Id != 0)
            await db.Circulacoes.IgnoreQueryFilters().Where(c => c.DeclaracaoId == d.Id && c.IdDcp != codigo)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.IdDcp, codigo), ct);
    }

    /// <summary>Prefixo do ID TIC que se aplica ao operador (o do grupo, quando pertence a um grupo).</summary>
    public async Task<int?> PrefixoIdTicAsync(Operador operador, CancellationToken ct = default)
    {
        if (operador.GrupoId is { } g)
        {
            var doGrupo = operador.Grupo?.PrefixoIdTic
                ?? await db.Operadores.IgnoreQueryFilters().Where(o => o.Id == g).Select(o => o.PrefixoIdTic).FirstOrDefaultAsync(ct);
            if (doGrupo is not null) return doGrupo;
        }
        return operador.PrefixoIdTic;
    }

    /// <summary>Próximo ID TIC do operador (null se nem o operador nem o grupo têm prefixo).</summary>
    public async Task<string?> ProximoIdTicAsync(Operador operador, CancellationToken ct = default)
    {
        if (await PrefixoIdTicAsync(operador, ct) is not { } prefixo) return null;
        var texto = prefixo.ToString();
        var tamanho = texto.Length + DigitosIdTic;
        var existentes = await db.Circulacoes.IgnoreQueryFilters()
            .Where(c => c.IdTic != null && c.IdTic.Length == tamanho && c.IdTic.StartsWith(texto))
            .Select(c => c.IdTic!).ToListAsync(ct);
        existentes.AddRange(db.ChangeTracker.Entries<Circulacao>().Select(e => e.Entity.IdTic)
            .Where(t => t is not null && t.Length == tamanho && t.StartsWith(texto)).Select(t => t!));
        var ultimo = existentes.Select(t => int.TryParse(t.AsSpan(texto.Length), out var n) ? n : 0).DefaultIfEmpty(0).Max();
        var max = (int)Math.Pow(10, DigitosIdTic) - 1;
        if (ultimo >= max)
            throw new InvalidOperationException($"A numeração do ID TIC com o prefixo {prefixo} está esgotada ({texto}{max}).");
        return texto + (ultimo + 1).ToString(new string('0', DigitosIdTic));
    }

    public record ResultadoRenovacao(int Renovadas, int JaRenovadas, int SemCirculacoes, IReadOnlyList<string> NumerosOcupados, int Circulacoes);

    /// <summary>
    /// Renova as declarações de <paramref name="ano"/> para o ano seguinte: cada declaração com circulações dá origem a
    /// uma declaração nova (versão 00), para onde passam as circulações; a do ano encerrado guarda a lista das suas circulações.
    /// Numeração: recomeça no ano seguinte (a seguir às que já existam), ou mantém o número com <paramref name="manterNumeros"/>.
    /// </summary>
    public async Task<ResultadoRenovacao> RenovarAnoAsync(int ano, bool manterNumeros, CancellationToken ct = default)
    {
        // Só se renova o ano mais recente, e sempre para o ano seguinte (2026 → 2027, depois 2027 → 2028).
        var atual = await AnoAtualAsync(ct);
        if (ano != atual)
            throw new InvalidOperationException($"Só é possível renovar as declarações do ano mais recente ({atual} → {atual + 1}).");
        var seguinte = ano + 1;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var declaracoes = await db.Declaracoes.IgnoreQueryFilters().Where(d => d.Ano == ano).Include(d => d.Renovacao)
            .OrderBy(d => d.Numero).ToListAsync(ct);
        var circulacoes = (await db.Circulacoes.IgnoreQueryFilters()
                .Where(c => c.Declaracao != null && c.Declaracao.Ano == ano).ToListAsync(ct))
            .ToLookup(c => c.DeclaracaoId!.Value);
        var ocupados = (await db.Declaracoes.IgnoreQueryFilters().Where(d => d.Ano == seguinte).Select(d => d.Numero).ToListAsync(ct)).ToHashSet();
        var proximo = ocupados.DefaultIfEmpty(0).Max();

        int renovadas = 0, jaRenovadas = 0, vazias = 0, total = 0;
        var emConflito = new List<string>();
        foreach (var d in declaracoes)
        {
            if (d.Renovacao is not null) { jaRenovadas++; continue; }
            var lista = circulacoes[d.Id].ToList();
            if (lista.Count == 0) { vazias++; continue; }
            int numero;
            if (manterNumeros)
            {
                if (!ocupados.Add(d.Numero)) { emConflito.Add(d.Codigo); continue; }
                numero = d.Numero;
            }
            else numero = ++proximo;

            var nova = new Declaracao { Ano = seguinte, Numero = numero, Versao = 0, OperadorId = d.OperadorId, Anterior = d };
            nova.Versoes.Add(new DeclaracaoVersao { Versao = 0, Motivo = $"Renovação de {d.Codigo}.", Utilizador = currentUser.UserName });
            db.Declaracoes.Add(nova);
            d.Versoes.Add(new DeclaracaoVersao { Versao = d.Versao, Motivo = $"Encerrada: renovada para {nova.Codigo}.", Utilizador = currentUser.UserName });
            foreach (var c in lista)
            {
                db.DeclaracaoCirculacoes.Add(new DeclaracaoCirculacao { Declaracao = d, CirculacaoId = c.Id });
                c.Declaracao = nova;
                c.IdDcp = nova.Codigo;
            }
            renovadas++;
            total += lista.Count;
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new ResultadoRenovacao(renovadas, jaRenovadas, vazias, emConflito, total);
    }

    /// <summary>
    /// Declaração indicada num ficheiro (id_DCP): encontra ou cria Ano.Numero e sobe a versão se a do ficheiro for maior.
    /// Devolve null se o texto não for um id_DCP reconhecido.
    /// </summary>
    public async Task<Declaracao?> DeclaracaoDoFicheiroAsync(string? idDcp, int anoPorOmissao, Operador operador, string motivo,
        Dictionary<(int, int), Declaracao> cache, CancellationToken ct = default)
    {
        if (Declaracao.Ler(idDcp, anoPorOmissao) is not { } x) return null;
        if (!cache.TryGetValue((x.Ano, x.Numero), out var d))
        {
            d = await db.Declaracoes.IgnoreQueryFilters().FirstOrDefaultAsync(e => e.Ano == x.Ano && e.Numero == x.Numero, ct);
            if (d is null)
            {
                d = new Declaracao
                {
                    Ano = x.Ano, Numero = x.Numero, Versao = x.Versao,
                    OperadorId = operador.GrupoId ?? (operador.Id == 0 ? null : operador.Id),
                    Operador = operador.GrupoId is null && operador.Id == 0 ? operador : null
                };
                d.Versoes.Add(new DeclaracaoVersao { Versao = x.Versao, Motivo = motivo, Utilizador = currentUser.UserName });
                db.Declaracoes.Add(d);
            }
            // Declaração já renovada (ano encerrado): as circulações ficam na renovação mais recente.
            var encerrada = false;
            while (d.Id != 0 && await db.Declaracoes.IgnoreQueryFilters().FirstOrDefaultAsync(r => r.AnteriorId == d.Id, ct) is { } r)
            {
                d = r;
                encerrada = true;
            }
            cache[(x.Ano, x.Numero)] = d;
            if (encerrada) return d;
        }
        if (d.Ano == x.Ano && d.Numero == x.Numero && x.Versao > d.Versao)
        {
            d.Versao = x.Versao;
            d.AtualizadoEm = DateTime.UtcNow;
            d.Versoes.Add(new DeclaracaoVersao { Versao = x.Versao, Motivo = motivo, Utilizador = currentUser.UserName });
        }
        return d;
    }
}
