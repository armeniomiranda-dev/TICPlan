'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { alocar } = require('../src/AppTIC.Web/wwwroot/js/simulacao-planeador.js');

const viagem = (Id, zona, InicioSegundos, FimSegundos) => ({ Id, zona, InicioSegundos, FimSegundos });

test('respeita zonas e não inclui viagens sem zona ou duração', () => {
    const resultado = alocar([
        viagem('a', 'Lisboa', 0, 100), viagem('b', 'Fora', 0, 100),
        viagem('c', 'Norte', 0, 100), viagem('f', 'Comum', 0, 100),
        viagem('d', '', 0, 100), viagem('e', 'Norte', 0, 0)
    ]);
    assert.equal(resultado.get('a'), 1);
    assert.equal(resultado.get('b'), 5);
    assert.equal(resultado.get('c'), 9);
    assert.equal(resultado.get('f'), 11);
    assert.equal(resultado.has('d'), false);
    assert.equal(resultado.has('e'), false);
});

test('rejeita sobrelotação sem libertar cais ocupado por outro autocarro', () => {
    const resultado = alocar([
        ...Array.from({ length: 2 }, (_, i) => viagem(`a${i}`, 'Norte', 0, 100)),
        ...Array.from({ length: 7 }, (_, i) => viagem(`o${i}`, 'Comum', 0, 100)),
        viagem('c', 'Norte', 10, 20), viagem('d', 'Norte', 20, 30)
    ]);
    assert.deepEqual(Array.from({ length: 2 }, (_, i) => resultado.get(`a${i}`)), [9, 10]);
    assert.deepEqual(Array.from({ length: 7 }, (_, i) => resultado.get(`o${i}`)), [11, 12, 13, 14, 15, 16, 17]);
    assert.equal(resultado.get('c'), null);
    assert.equal(resultado.get('d'), null);
});

test('a reserva é partilhada por zonas e liberta na partida', () => {
    const resultado = alocar([
        ...Array.from({ length: 7 }, (_, i) => viagem(`o${i}`, 'Comum', 0, 100)),
        viagem('o7', 'Comum', 1, 50),
        ...Array.from({ length: 4 }, (_, i) => viagem(`l${i}`, 'Lisboa', 0, 100)),
        viagem('l4', 'Lisboa', 2, 100),
        viagem('o8', 'Comum', 50, 100)
    ]);
    assert.equal(resultado.get('o7'), 9);
    assert.equal(resultado.get('l4'), 10);
    assert.equal(resultado.get('o8'), 9);
});

test('cada zona prefere os seus próprios cais mesmo com reserva livre', () => {
    const resultado = alocar([
        viagem('o1', 'Comum', 0, 10), viagem('o2', 'Comum', 0, 100),
        viagem('o3', 'Comum', 1, 100), viagem('o4', 'Comum', 10, 100)
    ]);
    assert.equal(resultado.get('o3'), 13);
    assert.equal(resultado.get('o4'), 11);
});

test('liberta um cais antes de outra chegada no mesmo instante', () => {
    const resultado = alocar([viagem('a', 'Norte', -600, 0), viagem('b', 'Norte', 0, 600)]);
    assert.equal(resultado.get('a'), 9);
    assert.equal(resultado.get('b'), 9);
});
