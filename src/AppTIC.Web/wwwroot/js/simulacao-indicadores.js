'use strict';

/**
 * Indicadores de desempenho do plano do dia (rotação, fluidez e processamento).
 * Tudo em segundos do dia; os valores vêm do plano e do modelo de circulação da animação, não de medições reais.
 */
const SimulacaoIndicadores = (() => {
    const DIA = 86400;
    const media = lista => lista.length ? lista.reduce((a, b) => a + b, 0) / lista.length : 0;

    /** Soma dos intervalos [a, b] sem contar sobreposições, repartida pelas 24 horas do dia. */
    function uniaoPorHora(intervalos) {
        const horas = new Array(24).fill(0);
        let fimAnterior = -Infinity;
        for (const [a, b] of [...intervalos].sort((x, y) => x[0] - y[0])) {
            let inicio = Math.max(a, fimAnterior, 0);
            const fim = Math.min(b, DIA);
            fimAnterior = Math.max(fimAnterior, b);
            while (inicio < fim) {
                const hora = Math.floor(inicio / 3600);
                const limite = Math.min(fim, (hora + 1) * 3600);
                horas[hora] += limite - inicio;
                inicio = limite;
            }
        }
        return horas;
    }

    /**
     * passagens: [{ id, inicio, fim, cais, limiteMinutos }] — só as que têm cais; limiteMinutos é o tempo máximo
     * da categoria do cais (null se não tiver). filas: Map id → horários da animação (SimulacaoPlanta.filas).
     */
    function calcular({ passagens, semCais = 0, totalCais, filas = new Map() }) {
        const n = passagens.length;
        if (!n) return null;

        // Entradas por hora (só as que chegam dentro do dia).
        const entradasPorHora = new Array(24).fill(0);
        for (const p of passagens)
            if (p.inicio >= 0 && p.inicio < DIA) entradasPorHora[Math.floor(p.inicio / 3600)]++;
        const horasComEntradas = entradasPorHora.filter(e => e > 0).length || 1;
        const picoEntradas = Math.max(...entradasPorHora);
        const horaPico = entradasPorHora.indexOf(picoEntradas);

        // Ocupação simultânea ao longo do dia.
        const eventos = [];
        for (const p of passagens) {
            const a = Math.max(0, p.inicio), b = Math.min(DIA, p.fim);
            if (b > a) eventos.push([a, 1], [b, -1]);
        }
        eventos.sort((x, y) => x[0] - y[0] || x[1] - y[1]);
        const limiar = Math.ceil(totalCais * 0.9);
        const ocupacaoPorHora = new Array(24).fill(0);
        let ocupados = 0, ocupacaoMaxima = 0, instanteMaximo = 0, segundosAcimaDoLimiar = 0, anterior = 0;
        for (const [instante, delta] of eventos) {
            if (ocupados >= limiar) segundosAcimaDoLimiar += instante - anterior;
            anterior = instante;
            ocupados += delta;
            if (ocupados > ocupacaoMaxima) { ocupacaoMaxima = ocupados; instanteMaximo = instante; }
            const hora = Math.min(23, Math.floor(instante / 3600));
            ocupacaoPorHora[hora] = Math.max(ocupacaoPorHora[hora], ocupados);
        }

        // Filas e manobras, segundo o modelo de circulação da animação.
        const esperasEntrada = [], esperasSaida = [], manobras = [];
        for (const p of passagens) {
            const f = filas.get(p.id);
            if (!f) continue;
            esperasEntrada.push(Math.max(0, f.chegada - p.inicio));
            esperasSaida.push(Math.max(0, p.fim - f.partida));
            manobras.push([f.estacionado - f.manobraEntrada, f.estacionado], [f.arranque, f.arranque + f.manobraSaida]);
        }
        const bloqueioPorHora = uniaoPorHora(manobras);

        const permanencias = passagens.map(p => (p.fim - p.inicio) / 60);
        const permanenciaMedia = media(permanencias);
        const comLimite = passagens.filter(p => p.limiteMinutos != null);
        const capacidadeHora = permanenciaMedia > 0 ? totalCais * 60 / permanenciaMedia : 0;

        return {
            veiculos: n,
            semCais,
            totalCais,
            rotacaoDia: n / totalCais,
            rotacaoHora: n / totalCais / horasComEntradas,
            rotacaoHoraPico: picoEntradas / totalCais,
            horaPico,
            permanenciaMedia,
            permanenciaMaxima: Math.max(...permanencias),
            ocupacaoMaxima,
            taxaOcupacaoMaxima: ocupacaoMaxima / totalCais,
            instanteOcupacaoMaxima: instanteMaximo,
            minutosAcimaDoLimiar: segundosAcimaDoLimiar / 60,
            filaEntradaMedia: media(esperasEntrada),
            filaEntradaMaxima: Math.max(0, ...esperasEntrada),
            filaSaidaMedia: media(esperasSaida),
            filaSaidaMaxima: Math.max(0, ...esperasSaida),
            bloqueioMedioHora: bloqueioPorHora.reduce((a, b) => a + b, 0) / horasComEntradas,
            bloqueioMaximoHora: Math.max(...bloqueioPorHora),
            horaBloqueioMaximo: bloqueioPorHora.indexOf(Math.max(...bloqueioPorHora)),
            taxaParagemIndevida: semCais / (n + semCais),
            throughputPico: picoEntradas,
            throughputMedio: n / horasComEntradas,
            capacidadeHora,
            usoDaCapacidade: capacidadeHora > 0 ? picoEntradas / capacidadeHora : 0,
            desvio: comLimite.length ? comLimite.filter(p => (p.fim - p.inicio) / 60 > p.limiteMinutos).length / comLimite.length : null,
            veiculosComLimite: comLimite.length,
            entradasPorHora,
            ocupacaoPorHora,
            bloqueioPorHora
        };
    }

    return { calcular, uniaoPorHora };
})();

if (typeof module !== 'undefined') module.exports = SimulacaoIndicadores;
