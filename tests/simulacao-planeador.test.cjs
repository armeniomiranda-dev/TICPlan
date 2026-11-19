'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const { alocar } = require('../src/AppTIC.Web/wwwroot/js/simulacao-planeador.js');

const viagem = (Id, zona, InicioSegundos, FimSegundos) => ({ Id, zona, InicioSegundos, FimSegundos });

test('respeita zonas e não inclui viagens sem zona ou duração', () => {
    const resultado = alocar([
        viagem('a', 'Lisboa', 0, 100), viagem('b', 'Internacional', 0, 100),
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

test('zonas lotadas usam os cais comuns, nunca os de outra zona, e libertam na partida', () => {
    const resultado = alocar([
        ...Array.from({ length: 4 }, (_, i) => viagem(`l${i}`, 'Lisboa', 0, 100)),
        viagem('l4', 'Lisboa', 2, 50),
        viagem('l5', 'Lisboa', 50, 100),
        ...Array.from({ length: 2 }, (_, i) => viagem(`n${i}`, 'Norte', 0, 100)),
        viagem('n2', 'Norte', 5, 100)
    ]);
    assert.equal(resultado.get('l4'), 11);
    assert.equal(resultado.get('l5'), 11);
    assert.equal(resultado.get('n2'), 12);
    assert.ok([...resultado.values()].every(c => c == null || c < 5 || c > 8));
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

test('zonamento por permanência: categoria pela duração e cais pela distância à saída', () => {
    const { esquemasPorOmissao, categoriaPorPermanencia, opcoes, pedido, ORDEM_SAIDA } =
        require('../src/AppTIC.Web/wwwroot/js/simulacao-planeador.js');
    const esquema = esquemasPorOmissao().Permanencia;
    assert.deepEqual([2, 5, 5.5, 10, 45].map(m => categoriaPorPermanencia(esquema, m)),
        ['Rapida', 'Rapida', 'Intermedia', 'Intermedia', 'Espera']);
    assert.deepEqual(opcoes(esquema, 'Rapida').primarios, ORDEM_SAIDA.slice(0, 6));
    assert.deepEqual(opcoes(esquema, 'Rapida').reserva, ORDEM_SAIDA.slice(6, 11));
    assert.deepEqual(opcoes(esquema, 'Espera').primarios, ORDEM_SAIDA.slice(11).reverse());
    assert.equal(opcoes(esquema, 'Espera').outros.length, 6);

    const corpo = pedido(esquema, []);
    assert.deepEqual(corpo.Categorias.flatMap(c => c.Cais).sort((a, b) => a - b), Array.from({ length: 17 }, (_, i) => i + 1));
    assert.deepEqual(corpo.OrdemSaida, ORDEM_SAIDA);

    // Regra provisória: a paragem rápida fica no cais mais próximo da saída e a espera no mais distante.
    const resultado = alocar([viagem('r', 'Rapida', 0, 120), viagem('e', 'Espera', 0, 3600)], esquema);
    assert.equal(resultado.get('r'), 9);
    assert.equal(resultado.get('e'), 1);
});

test('em último recurso usa cais de outra categoria; sem ele fica sem cais', () => {
    const { esquemasPorOmissao } = require('../src/AppTIC.Web/wwwroot/js/simulacao-planeador.js');
    const esquema = esquemasPorOmissao().Permanencia;
    const rapidas = Array.from({ length: 12 }, (_, i) => viagem('r' + String(i).padStart(2, '0'), 'Rapida', 0, 100));
    assert.equal(alocar(rapidas, esquema).get('r11'), 16);
    assert.equal(alocar(rapidas, { ...esquema, ultimoRecurso: false }).get('r11'), null);
});
