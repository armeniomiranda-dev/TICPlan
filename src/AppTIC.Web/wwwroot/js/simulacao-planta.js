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

    return { cais, ESTRADA_Y, entrada, anguloEstacionado, encontrarCais, pontoNoPercurso };
})();

if (typeof module !== 'undefined') module.exports = SimulacaoPlanta;
