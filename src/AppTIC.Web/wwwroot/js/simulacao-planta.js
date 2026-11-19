'use strict';

const SimulacaoPlanta = (() => {
    // Graus no sentido do ecrã (positivo = horário), como na planta TerminalPlanta.jpg:
    // cais 1–8 rodados 20° em relação à horizontal; cais 9–17 (espinha) rodados -30° em relação à vertical.
    const graus = g => g * Math.PI / 180;
    const ESTRADA_Y = 224;
    const cais = [
        ...Array.from({ length: 8 }, (_, i) => ({
            id: i + 1, x: 350 + i * 94, y: 272, angulo: graus(20),
            comprimento: 64, largura: 24, ativo: true
        })),
        ...Array.from({ length: 9 }, (_, i) => ({
            id: i + 9, x: 1008 - i * 62, y: 83 + i * 10, angulo: graus(90 - 30),
            comprimento: 56, largura: 22, ativo: true
        }))
    ];

    /** Ponto à frente da extremidade do cais virada para a via; o autocarro entra por aí, alinhado com o cais. */
    function entrada(c) {
        const dx = Math.cos(c.angulo) * c.comprimento;
        const dy = Math.sin(c.angulo) * c.comprimento;
        const a = { x: c.x - dx, y: c.y - dy };
        const b = { x: c.x + dx, y: c.y + dy };
        return Math.abs(a.y - ESTRADA_Y) <= Math.abs(b.y - ESTRADA_Y) ? a : b;
    }

    /** Orientação do autocarro estacionado: a de quem entrou de frente pela entrada. */
    function anguloEstacionado(c) {
        const e = entrada(c);
        return Math.atan2(c.y - e.y, c.x - e.x);
    }

    function encontrarCais(x, y) {
        return cais.find(c => {
            const dx = x - c.x;
            const dy = y - c.y;
            const localX = dx * Math.cos(c.angulo) + dy * Math.sin(c.angulo);
            const localY = -dx * Math.sin(c.angulo) + dy * Math.cos(c.angulo);
            return Math.abs(localX) <= c.comprimento / 2 + 5 && Math.abs(localY) <= c.largura / 2 + 5;
        }) ?? null;
    }

    function pontoNoPercurso(pontos, progresso) {
        const distancias = pontos.slice(1).map((p, i) => Math.hypot(p.x - pontos[i].x, p.y - pontos[i].y));
        let restante = Math.max(0, Math.min(1, progresso)) * distancias.reduce((a, b) => a + b, 0);
        for (let i = 0; i < distancias.length; i++) {
            const distancia = distancias[i];
            if (distancia === 0) continue;
            if (restante <= distancia || i === distancias.length - 1) {
                const inicio = pontos[i], fim = pontos[i + 1];
                const proporcao = Math.min(1, restante / distancia);
                return {
                    x: inicio.x + (fim.x - inicio.x) * proporcao,
                    y: inicio.y + (fim.y - inicio.y) * proporcao,
                    angulo: Math.atan2(fim.y - inicio.y, fim.x - inicio.x),
                    segmento: i
                };
            }
            restante -= distancia;
        }
        return { ...pontos.at(-1), angulo: 0 };
    }

    // Circulação em fila indiana: todos os autocarros andam à mesma velocidade e guardam um espaço entre si.
    const SAIDA_X = 1130;           // fim do canal de saída, já fora do desenho
    const VELOCIDADE = 10;          // píxeis por segundo simulado
    const ESPACAMENTO = 68;         // comprimento do autocarro (56) + folga
    const PORTAO = [{ x: 185, y: 465 }, { x: 145, y: 410 }, { x: 100, y: 348 }, { x: 190, y: 309 }, { x: 245, y: ESTRADA_Y }];

    const comprimento = pontos => pontos.slice(1).reduce((soma, p, i) => soma + Math.hypot(p.x - pontos[i].x, p.y - pontos[i].y), 0);

    function percursoEntrada(c) {
        const e = entrada(c);
        return [...PORTAO, { x: e.x, y: ESTRADA_Y }, e, c];
    }

    function percursoSaida(c) {
        const e = entrada(c);
        return [c, e, { x: e.x, y: ESTRADA_Y }, { x: SAIDA_X, y: ESTRADA_Y }];
    }

    /** Cais ordenados do mais próximo ao mais distante da saída (ao longo da via). */
    const ordemSaida = [...cais].sort((a, b) => entrada(b).x - entrada(a).x || a.id - b.id).map(c => c.id);

    /**
     * Horários de animação de cada passagem ({ id, cais, inicio, fim }) sem sobreposições na via:
     * quem chega no mesmo instante entra em fila (o seguinte espera o intervalo de segurança) e quem parte
     * no mesmo instante sai em fila, com o mais próximo da saída à frente e todos fora do terminal até à hora de fim.
     */
    function filas(passagens) {
        const intervalo = ESPACAMENTO / VELOCIDADE;
        const dados = passagens.map(p => {
            const entra = percursoEntrada(p.cais), sai = percursoSaida(p.cais);
            return { ...p, entra, sai, duracaoEntrada: comprimento(entra) / VELOCIDADE, duracaoSaida: comprimento(sai) / VELOCIDADE };
        });
        const porId = (a, b) => String(a.id).localeCompare(String(b.id));

        let anterior = -Infinity;
        for (const d of [...dados].sort((a, b) => a.inicio - b.inicio || b.duracaoEntrada - a.duracaoEntrada || porId(a, b))) {
            d.chegada = Math.max(d.inicio, anterior + intervalo);
            anterior = d.chegada;
        }
        let seguinte = Infinity;
        for (const d of [...dados].sort((a, b) => b.fim - a.fim || b.duracaoSaida - a.duracaoSaida || porId(a, b))) {
            d.partida = Math.min(d.fim, seguinte - intervalo);
            seguinte = d.partida;
        }

        const resultado = new Map();
        for (const d of dados) {
            let { chegada, partida } = d;
            if (partida <= chegada) { chegada = d.inicio; partida = d.fim; }
            let estacionado = chegada + d.duracaoEntrada;
            let arranque = partida - d.duracaoSaida;
            // Permanência curta: reparte o tempo disponível entre a entrada e a saída.
            if (estacionado > arranque)
                estacionado = arranque = chegada + (partida - chegada) * d.duracaoEntrada / (d.duracaoEntrada + d.duracaoSaida);
            // Manobra no cais (entrar e sair em marcha-atrás): a parte do percurso feita sobre o próprio cais.
            const manobraEntrada = (estacionado - chegada) * d.cais.comprimento / comprimento(d.entra);
            const manobraSaida = (partida - arranque) * d.cais.comprimento / comprimento(d.sai);
            resultado.set(d.id, { cais: d.cais, entra: d.entra, sai: d.sai, chegada, estacionado, arranque, partida, manobraEntrada, manobraSaida });
        }
        return resultado;
    }

    /** Posição do autocarro num instante, ou null enquanto espera a vez de entrar ou depois de sair. */
    function posicao(fila, tempo) {
        if (!fila || tempo < fila.chegada || tempo >= fila.partida) return null;
        const parado = { x: fila.cais.x, y: fila.cais.y, angulo: anguloEstacionado(fila.cais) };
        if (tempo < fila.estacionado)
            return pontoNoPercurso(fila.entra, (tempo - fila.chegada) / (fila.estacionado - fila.chegada));
        if (tempo < fila.arranque) return parado;
        const ponto = pontoNoPercurso(fila.sai, (tempo - fila.arranque) / (fila.partida - fila.arranque));
        // Sai do cais em marcha-atrás, mantendo a orientação de estacionado.
        if (ponto.segmento === 0) ponto.angulo = parado.angulo;
        return ponto;
    }

    return {
        cais, ESTRADA_Y, SAIDA_X, VELOCIDADE, ESPACAMENTO, ordemSaida,
        entrada, anguloEstacionado, encontrarCais, pontoNoPercurso, filas, posicao
    };
})();

if (typeof module !== 'undefined') module.exports = SimulacaoPlanta;
