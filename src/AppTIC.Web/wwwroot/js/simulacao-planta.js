'use strict';

const SimulacaoPlanta = (() => {
    const cais = [
        ...Array.from({ length: 8 }, (_, i) => ({
            id: i + 1, x: 350 + i * 94, y: 272, angulo: -0.20,
            comprimento: 64, largura: 24, ativo: true
        })),
        ...Array.from({ length: 9 }, (_, i) => ({
            id: i + 9, x: 1008 - i * 62, y: 83 + i * 10, angulo: -0.80,
            comprimento: 56, largura: 22, ativo: true
        }))
    ];

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
                    angulo: Math.atan2(fim.y - inicio.y, fim.x - inicio.x)
                };
            }
            restante -= distancia;
        }
        return { ...pontos.at(-1), angulo: 0 };
    }

    return { cais, encontrarCais, pontoNoPercurso };
})();

if (typeof module !== 'undefined') module.exports = SimulacaoPlanta;
