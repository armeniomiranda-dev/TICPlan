'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const planta = require('../src/AppTIC.Web/wwwroot/js/simulacao-planta.js');
const { calcular, uniaoPorHora } = require('../src/AppTIC.Web/wwwroot/js/simulacao-indicadores.js');

const H = 3600;

test('rotação, permanência, ocupação e processamento do plano', () => {
    // 4 cais; às 10h entram 4 (10 min cada), às 11h entram 2 (30 min), um deles em cais com limite de 10 min.
    const passagens = [
        ...[1, 2, 3, 4].map(c => ({ id: 'a' + c, inicio: 10 * H, fim: 10 * H + 600, cais: c, limiteMinutos: 10 })),
        { id: 'b1', inicio: 11 * H, fim: 11 * H + 1800, cais: 1, limiteMinutos: 10 },
        { id: 'b2', inicio: 11 * H, fim: 11 * H + 1800, cais: 2, limiteMinutos: null }
    ];
    const k = calcular({ passagens, semCais: 2, totalCais: 4 });

    assert.equal(k.rotacaoDia, 1.5);
    assert.equal(k.rotacaoHora, 0.75);
    assert.equal(k.rotacaoHoraPico, 1);
    assert.equal(k.horaPico, 10);
    assert.ok(Math.abs(k.permanenciaMedia - 100 / 6) < 1e-9);
    assert.equal(k.permanenciaMaxima, 30);
    assert.equal(k.ocupacaoMaxima, 4);
    assert.equal(k.taxaOcupacaoMaxima, 1);
    assert.equal(k.instanteOcupacaoMaxima, 10 * H);
    assert.equal(k.minutosAcimaDoLimiar, 10);
    assert.equal(k.taxaParagemIndevida, 0.25);
    assert.equal(k.throughputPico, 4);
    assert.equal(k.throughputMedio, 3);
    assert.ok(Math.abs(k.capacidadeHora - 4 * 60 / (100 / 6)) < 1e-9);
    assert.equal(k.desvio, 0.2);
    assert.equal(k.entradasPorHora[10], 4);
    assert.equal(k.ocupacaoPorHora[11], 2);
    assert.equal(calcular({ passagens: [], totalCais: 4 }), null);
});

test('filas e bloqueio do canal vêm dos horários da animação', () => {
    const cais = id => planta.cais.find(c => c.id === id);
    const passagens = [1, 2, 3].map(c => ({ id: 'a' + c, inicio: 10 * H, fim: 11 * H, cais: c, limiteMinutos: null }));
    const filas = planta.filas(passagens.map(p => ({ ...p, cais: cais(p.cais) })));
    const k = calcular({ passagens, totalCais: 17, filas });
    const intervalo = planta.ESPACAMENTO / planta.VELOCIDADE;

    assert.ok(Math.abs(k.filaEntradaMaxima - 2 * intervalo) < 1e-9);
    assert.ok(Math.abs(k.filaEntradaMedia - intervalo) < 1e-9);
    assert.ok(Math.abs(k.filaSaidaMaxima - 2 * intervalo) < 1e-9);
    assert.ok(k.bloqueioMaximoHora > 0 && k.bloqueioMaximoHora <= 6 * 64 / planta.VELOCIDADE + 1e-9);
    assert.equal(k.horaBloqueioMaximo, 10);
    assert.equal(k.desvio, null);
});

test('a união de intervalos não conta sobreposições e reparte pelas horas', () => {
    const horas = uniaoPorHora([[3500, 3700], [3600, 3650], [-50, 10]]);
    assert.deepEqual([horas[0], horas[1]], [110, 100]);
});
