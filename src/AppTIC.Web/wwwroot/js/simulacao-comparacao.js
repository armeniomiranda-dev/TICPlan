'use strict';

/**
 * Comparação de cenários de zonamento sobre as mesmas circulações do mesmo dia:
 * Livre (qualquer cais), Zonas geográficas (esquema por região) e Perto da saída (esquema por permanência).
 * Cada cenário é planeado pelo servidor (OR-Tools) e avaliado com os indicadores da simulação.
 */
const SimulacaoComparacao = (() => {
    const Planeador = typeof SimulacaoPlaneador !== 'undefined' ? SimulacaoPlaneador : require('./simulacao-planeador.js');
    const Indicadores = typeof SimulacaoIndicadores !== 'undefined' ? SimulacaoIndicadores : require('./simulacao-indicadores.js');

    /** Cores das séries (validadas para o fundo escuro da simulação, incluindo daltonismo). */
    const CORES = ['#3987e5', '#d95926', '#199e70'];
    /** Paragem curta: até este número de minutos. */
    const MINUTOS_CURTA = 10;
    /** Cais "junto à saída" e "no fundo": os primeiros / últimos desta quantidade na ordem de saída. */
    const CAIS_EXTREMOS = 6;

    /** Os três cenários, com os esquemas de zonamento configurados na simulação. */
    function cenarios(esquemas) {
        return [
            { id: 'Livre', nome: 'Livre', descricao: 'qualquer cais livre, sem zonas', esquema: Planeador.esquemaLivre() },
            { id: 'Regiao', nome: 'Zonas geográficas', descricao: 'cais da região da circulação, depois reserva', esquema: esquemas.Regiao },
            { id: 'Permanencia', nome: 'Perto da saída', descricao: 'paragens rápidas junto à saída, esperas no fundo', esquema: esquemas.Permanencia }
        ].map((c, i) => ({ ...c, cor: CORES[i] }));
    }

    const comDuracao = p => p.fim > p.inicio;
    const temZona = (cenario, p) => Planeador.categoriaDe(cenario.esquema, p.zona) !== null;

    /**
     * Corpo do pedido ao servidor para um cenário, ou null se nenhuma passagem tiver zona.
     * passagens: [{ id, inicio, fim, zona }] — a zona já é a deste cenário. O plano é feito de raiz (sem cais fixos).
     */
    function pedido(cenario, passagens, ordemSaida) {
        const autocarros = passagens.filter(p => comDuracao(p) && temZona(cenario, p)).map(p => ({
            AutocarroId: p.id,
            Categoria: p.zona,
            ChegadaMinutos: Math.floor(p.inicio / 60),
            PartidaMinutos: Math.max(Math.floor(p.inicio / 60) + 1, Math.ceil(p.fim / 60)),
            CaisFixo: null
        }));
        return autocarros.length ? Planeador.pedido(cenario.esquema, autocarros, ordemSaida) : null;
    }

    /**
     * Indicadores de um cenário a partir do plano devolvido pelo servidor.
     * filas: função (passagens com cais) → Map id → horários da animação (SimulacaoPlanta.filas).
     */
    function avaliar({ cenario, passagens, plano, ordemSaida, totalCais, filas = () => new Map(), erro = null }) {
        const validas = passagens.filter(comDuracao);
        const planeadas = validas.filter(p => temZona(cenario, p));
        const cais = new Map((plano?.Alocacoes ?? []).map(a => [a.AutocarroId, a.Cais]));
        const comCais = planeadas.filter(p => cais.get(p.id) != null).map(p => ({ ...p, cais: cais.get(p.id) }));
        const esquema = cenario.esquema;

        const k = Indicadores.calcular({
            passagens: comCais.map(p => ({
                id: p.id, inicio: p.inicio, fim: p.fim, cais: p.cais,
                limiteMinutos: Planeador.categoriaDe(esquema, esquema.cais[p.cais])?.ateMinutos ?? null
            })),
            semCais: planeadas.length - comCais.length,
            totalCais,
            filas: filas(comCais)
        });

        const posicao = n => ordemSaida.indexOf(n);
        const curtas = comCais.filter(p => (p.fim - p.inicio) / 60 <= MINUTOS_CURTA);
        const longas = comCais.filter(p => (p.fim - p.inicio) / 60 > MINUTOS_CURTA);
        const fracao = (lista, condicao) => lista.length ? lista.filter(condicao).length / lista.length : null;
        // Sem nenhuma circulação planeada, "0 sem cais" não é um bom resultado: fica sem valor.
        const seHouvePlano = valor => (planeadas.length ? valor : null);

        return {
            cenario,
            k,
            erro,
            otimo: erro ? null : plano?.Otimo ?? true,
            planeadas: planeadas.length,
            comCais: comCais.length,
            semCais: seHouvePlano(planeadas.length - comCais.length),
            semZona: validas.length - planeadas.length,
            foraDaCategoria: seHouvePlano(comCais.filter(p => esquema.cais[p.cais] !== p.zona).length),
            curtasJuntoSaida: fracao(curtas, p => posicao(p.cais) < CAIS_EXTREMOS),
            longasNoFundo: fracao(longas, p => posicao(p.cais) >= ordemSaida.length - CAIS_EXTREMOS),
            curtas: curtas.length,
            longas: longas.length
        };
    }

    const num = (valor, casas = 1) => valor.toLocaleString('pt-PT', { minimumFractionDigits: casas, maximumFractionDigits: casas });
    const formatos = {
        int: v => num(v, 0),
        num1: v => num(v, 1),
        num2: v => num(v, 2),
        pct: v => `${num(v * 100, 0)} %`,
        min: v => `${num(v, 1)} min`,
        seg: v => Math.abs(v) >= 90 ? `${num(v / 60, 1)} min` : `${num(v, 0)} s`
    };
    const formatosDelta = {
        pct: v => `${v > 0 ? '+' : ''}${num(v * 100, 0)} p.p.`
    };

    /**
     * Linhas da tabela. sentido: 'min' (menor é melhor), 'max' (maior é melhor) ou null (só informativo).
     * valor(r) devolve um número, ou null quando não se aplica.
     */
    const INDICADORES = [
        { grupo: 'Plano', nome: 'Circulações planeadas', formato: 'int', sentido: null, valor: r => r.planeadas,
            ajuda: 'com zona neste cenário' },
        { grupo: 'Plano', nome: 'Com cais', formato: 'int', sentido: 'max', valor: r => r.comCais },
        { grupo: 'Plano', nome: 'Sem cais (parariam no canal)', formato: 'int', sentido: 'min', valor: r => r.semCais },
        { grupo: 'Plano', nome: 'Sem zona (fora do plano)', formato: 'int', sentido: 'min', valor: r => r.semZona,
            ajuda: 'circulações sem categoria para este zonamento' },
        { grupo: 'Plano', nome: 'Fora dos cais da sua categoria', formato: 'int', sentido: 'min', valor: r => r.foraDaCategoria,
            ajuda: 'em cais de reserva ou de outra categoria' },
        { grupo: 'Posição', nome: `Paragens até ${MINUTOS_CURTA} min nos ${CAIS_EXTREMOS} cais junto à saída`, formato: 'pct', sentido: 'max',
            valor: r => r.curtasJuntoSaida },
        { grupo: 'Posição', nome: `Paragens com mais de ${MINUTOS_CURTA} min nos ${CAIS_EXTREMOS} cais do fundo`, formato: 'pct', sentido: 'max',
            valor: r => r.longasNoFundo },
        { grupo: 'Rotação e capacidade', nome: 'Índice de rotação', formato: 'num2', sentido: 'max', valor: r => r.k?.rotacaoDia ?? null,
            ajuda: 'veículos por cais no dia' },
        { grupo: 'Rotação e capacidade', nome: 'Tempo médio de permanência', formato: 'min', sentido: null, valor: r => r.k?.permanenciaMedia ?? null },
        { grupo: 'Rotação e capacidade', nome: 'Taxa de ocupação máxima', formato: 'pct', sentido: null, valor: r => r.k?.taxaOcupacaoMaxima ?? null },
        { grupo: 'Rotação e capacidade', nome: 'Tempo acima de 90 % de ocupação', formato: 'min', sentido: 'min', valor: r => r.k?.minutosAcimaDoLimiar ?? null },
        { grupo: 'Fluidez e atrito', nome: 'Fila na entrada (média)', formato: 'seg', sentido: 'min', valor: r => r.k?.filaEntradaMedia ?? null },
        { grupo: 'Fluidez e atrito', nome: 'Fila na entrada (máxima)', formato: 'seg', sentido: 'min', valor: r => r.k?.filaEntradaMaxima ?? null },
        { grupo: 'Fluidez e atrito', nome: 'Fila na saída (média)', formato: 'seg', sentido: 'min', valor: r => r.k?.filaSaidaMedia ?? null },
        { grupo: 'Fluidez e atrito', nome: 'Fila na saída (máxima)', formato: 'seg', sentido: 'min', valor: r => r.k?.filaSaidaMaxima ?? null },
        { grupo: 'Fluidez e atrito', nome: 'Bloqueio do canal por manobras (média/h)', formato: 'seg', sentido: 'min', valor: r => r.k?.bloqueioMedioHora ?? null },
        { grupo: 'Fluidez e atrito', nome: 'Bloqueio do canal na pior hora', formato: 'seg', sentido: 'min', valor: r => r.k?.bloqueioMaximoHora ?? null },
        { grupo: 'Fluidez e atrito', nome: 'Taxa de paragem indevida', formato: 'pct', sentido: 'min', valor: r => r.k?.taxaParagemIndevida ?? null },
        { grupo: 'Processamento', nome: 'Volume na hora de ponta', formato: 'int', sentido: null, valor: r => r.k?.throughputPico ?? null,
            ajuda: 'veículos/h' },
        { grupo: 'Processamento', nome: 'Uso da capacidade teórica', formato: 'pct', sentido: null, valor: r => r.k?.usoDaCapacidade ?? null },
        { grupo: 'Processamento', nome: 'Desvio ao tempo previsto', formato: 'pct', sentido: 'min', valor: r => r.k?.desvio ?? null,
            ajuda: 'só cenários com categorias com limite de tempo' }
    ];

    /**
     * Tabela de comparação: uma linha por indicador e uma célula por cenário, com o melhor valor assinalado
     * (só quando os cenários diferem) e a diferença face ao 1.º cenário (Livre).
     */
    function linhas(resultados) {
        return INDICADORES.map(ind => {
            const formatar = formatos[ind.formato];
            const valores = resultados.map(r => (r.erro ? null : ind.valor(r)));
            const textos = valores.map(v => (v == null ? 'n/d' : formatar(v)));
            const comValor = valores.map((v, i) => [v, i]).filter(([v]) => v != null);
            let melhores = new Set();
            if (ind.sentido && comValor.length > 1 && new Set(comValor.map(([, i]) => textos[i])).size > 1) {
                const alvo = (ind.sentido === 'min' ? Math.min : Math.max)(...comValor.map(([v]) => v));
                melhores = new Set(comValor.filter(([, i]) => textos[i] === formatar(alvo)).map(([, i]) => i));
            }
            const base = valores[0];
            return {
                grupo: ind.grupo,
                nome: ind.nome,
                ajuda: ind.ajuda ?? '',
                celulas: valores.map((v, i) => {
                    let delta = '';
                    if (i > 0 && v != null && base != null) {
                        const d = v - base;
                        const texto = (formatosDelta[ind.formato] ?? (x => `${x > 0 ? '+' : x < 0 ? '−' : ''}${formatar(Math.abs(x))}`))(d);
                        if (formatar(Math.abs(d)) !== formatar(0)) delta = texto;
                    }
                    return { texto: textos[i], delta, melhor: melhores.has(i) };
                })
            };
        });
    }

    /** Gráfico de linhas (SVG) do bloqueio do canal por manobras em cada hora, uma linha por cenário. */
    function grafico(resultados) {
        const series = resultados.filter(r => r.k).map(r => ({ r, valores: r.k.bloqueioPorHora.map(s => s / 60) }));
        if (!series.length) return '';
        const L = 720, A = 230, esq = 40, dir = 120, topo = 12, baixo = 26;
        const largura = L - esq - dir, altura = A - topo - baixo;
        const maximo = Math.max(1, ...series.flatMap(s => s.valores));
        const passo = maximo <= 5 ? 1 : maximo <= 15 ? 5 : maximo <= 30 ? 10 : 15;
        const topoEscala = Math.ceil(maximo / passo) * passo;
        const x = h => esq + (h + 0.5) * largura / 24;
        const y = v => topo + altura - v / topoEscala * altura;
        const partes = [`<svg viewBox="0 0 ${L} ${A}" class="sim-cmp-svg" role="img" aria-label="Bloqueio do canal por manobras, em minutos, por hora e por cenário">`];

        for (let v = 0; v <= topoEscala; v += passo) {
            partes.push(`<line x1="${esq}" x2="${esq + largura}" y1="${y(v)}" y2="${y(v)}" class="sim-cmp-grelha"/>`);
            partes.push(`<text x="${esq - 6}" y="${y(v) + 4}" text-anchor="end" class="sim-cmp-eixo">${v}</text>`);
        }
        for (let h = 0; h < 24; h += 3)
            partes.push(`<text x="${x(h)}" y="${A - 8}" text-anchor="middle" class="sim-cmp-eixo">${String(h).padStart(2, '0')}h</text>`);

        for (const { r, valores } of series) {
            const pontos = valores.map((v, h) => `${x(h).toFixed(1)},${y(v).toFixed(1)}`).join(' ');
            partes.push(`<polyline points="${pontos}" fill="none" stroke="${r.cenario.cor}" stroke-width="2" stroke-linejoin="round" stroke-linecap="round"/>`);
        }

        // Rótulos diretos no fim de cada linha, afastados para não se sobreporem.
        const rotulos = series.map(({ r, valores }) => ({ nome: r.cenario.nome, cor: r.cenario.cor, y: y(valores[23]) }))
            .sort((a, b) => a.y - b.y);
        for (let i = 1; i < rotulos.length; i++) rotulos[i].y = Math.max(rotulos[i].y, rotulos[i - 1].y + 14);
        for (const rotulo of rotulos) {
            partes.push(`<circle cx="${x(23) + 10}" cy="${rotulo.y}" r="4" fill="${rotulo.cor}"/>`);
            partes.push(`<text x="${x(23) + 18}" y="${rotulo.y + 4}" class="sim-cmp-rotulo">${rotulo.nome}</text>`);
        }

        // Zonas de passagem do rato: uma por hora, com os valores de todos os cenários.
        for (let h = 0; h < 24; h++) {
            const detalhe = series.map(({ r, valores }) => `${r.cenario.nome}: ${num(valores[h], 1)} min`).join('\n');
            partes.push(`<rect x="${esq + h * largura / 24}" y="${topo}" width="${largura / 24}" height="${altura}" class="sim-cmp-zona"><title>${String(h).padStart(2, '0')}h–${String(h + 1).padStart(2, '0')}h\n${detalhe}</title></rect>`);
        }
        partes.push('</svg>');
        return partes.join('');
    }

    return { CORES, MINUTOS_CURTA, CAIS_EXTREMOS, INDICADORES, cenarios, pedido, avaliar, linhas, grafico };
})();

if (typeof module !== 'undefined') module.exports = SimulacaoComparacao;
