'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const planta = require('../src/AppTIC.Web/wwwroot/js/simulacao-planta.js');
const planeador = require('../src/AppTIC.Web/wwwroot/js/simulacao-planeador.js');

test('os cais 1 a 8 ficam em baixo e os cais 9 a 17 seguem a diagonal superior', () => {
    assert.deepEqual(planta.cais.map(c => c.id), Array.from({ length: 17 }, (_, i) => i + 1));
    const inferiores = planta.cais.filter(c => c.id <= 8);
    const superiores = planta.cais.filter(c => c.id >= 9);
    assert.ok(inferiores.every(c => c.y > Math.max(...superiores.map(s => s.y))));
    assert.ok(inferiores.every((c, i) => i === 0 || c.x > inferiores[i - 1].x));
    assert.ok(superiores.every((c, i) => i === 0 || (c.x < superiores[i - 1].x && c.y > superiores[i - 1].y)));
});

test('apenas os cais do planeador estão ativos na planta', () => {
    assert.deepEqual(
        planta.cais.filter(c => c.ativo).map(c => c.id),
        [...new Set([...Object.values(planeador.zonas).flat(), ...planeador.caisComuns])]
    );
});

test('a seleção encontra todos os cais e ignora pontos fora deles', () => {
    for (const cais of planta.cais) {
        assert.equal(planta.encontrarCais(cais.x, cais.y)?.id, cais.id);
        const x = cais.x + Math.cos(cais.angulo) * (cais.comprimento / 2 - 1);
        const y = cais.y + Math.sin(cais.angulo) * (cais.comprimento / 2 - 1);
        assert.equal(planta.encontrarCais(x, y)?.id, cais.id);
    }
    assert.equal(planta.encontrarCais(10, 10), null);
});

test('o percurso animado começa e termina nos pontos indicados', () => {
    const pontos = [{ x: 0, y: 0 }, { x: 10, y: 0 }, { x: 10, y: 10 }];
    assert.deepEqual(planta.pontoNoPercurso(pontos, 0), { x: 0, y: 0, angulo: 0 });
    assert.deepEqual(planta.pontoNoPercurso(pontos, .75), { x: 10, y: 5, angulo: Math.PI / 2 });
    assert.deepEqual(planta.pontoNoPercurso(pontos, 1), { x: 10, y: 10, angulo: Math.PI / 2 });
});
