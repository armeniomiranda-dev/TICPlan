'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const planeador = require('../src/AppTIC.Web/wwwroot/js/simulacao-planeador.js');
const comparacao = require('../src/AppTIC.Web/wwwroot/js/simulacao-comparacao.js');

const ordemSaida = planeador.ORDEM_SAIDA;
const esquemas = planeador.esquemasPorOmissao(ordemSaida);
const [livre, regiao, permanencia] = comparacao.cenarios(esquemas);

test('três cenários: livre com todos os cais numa categoria, região e permanência', () => {
    assert.deepEqual([livre.id, regiao.id, permanencia.id], ['Livre', 'Regiao', 'Permanencia']);
    assert.deepEqual(planeador.caisDaCategoria(livre.esquema, 'Livre'), Array.from({ length: 17 }, (_, i) => i + 1));
    assert.equal(regiao.esquema, esquemas.Regiao);
});

test('pedido ignora passagens sem zona ou sem duração e não fixa cais', () => {
    const corpo = comparacao.pedido(regiao, [
        { id: 'a', inicio: 0, fim: 600, zona: 'Norte' },
        { id: 'b', inicio: 0, fim: 600, zona: '' },
        { id: 'c', inicio: 60, fim: 60, zona: 'Norte' }
    ], ordemSaida);
    assert.deepEqual(corpo.Autocarros.map(a => [a.AutocarroId, a.CaisFixo]), [['a', null]]);
    assert.equal(comparacao.pedido(regiao, [{ id: 'b', inicio: 0, fim: 600, zona: '' }], ordemSaida), null);
});

test('avaliar conta sem cais, sem zona, fora da categoria e posição face à saída', () => {
    const passagens = [
        { id: 'curta', inicio: 0, fim: 300, zona: 'Norte' },
        { id: 'longa', inicio: 0, fim: 3600, zona: 'Norte' },
        { id: 'semcais', inicio: 0, fim: 600, zona: 'Norte' },
        { id: 'semzona', inicio: 0, fim: 600, zona: '' }
    ];
    const plano = { Alocacoes: [{ AutocarroId: 'curta', Cais: 9 }, { AutocarroId: 'longa', Cais: 1 }], SemCais: ['semcais'], Otimo: true };
    const r = comparacao.avaliar({ cenario: regiao, passagens, plano, ordemSaida, totalCais: 17 });
    assert.equal(r.planeadas, 3);
    assert.equal(r.comCais, 2);
    assert.equal(r.semCais, 1);
    assert.equal(r.semZona, 1);
    assert.equal(r.foraDaCategoria, 1); // a longa ficou no cais 1 (Lisboa)
    assert.equal(r.curtasJuntoSaida, 1); // cais 9 é o mais próximo da saída
    assert.equal(r.longasNoFundo, 1); // cais 1 é o mais distante
    assert.equal(r.k.veiculos, 2);
});

test('linhas assinalam o melhor só quando os cenários diferem e mostram a diferença face ao Livre', () => {
    const passagens = [{ id: 'a', inicio: 0, fim: 600, zona: 'X' }, { id: 'b', inicio: 0, fim: 600, zona: 'X' }];
    const base = { passagens: passagens.map(p => ({ ...p, zona: 'Livre' })), ordemSaida, totalCais: 17 };
    const r1 = comparacao.avaliar({ ...base, cenario: livre, plano: { Alocacoes: [{ AutocarroId: 'a', Cais: 1 }, { AutocarroId: 'b', Cais: 2 }] } });
    const r2 = comparacao.avaliar({ ...base, cenario: livre, plano: { Alocacoes: [{ AutocarroId: 'a', Cais: 1 }] } });
    const linhas = comparacao.linhas([r1, r2]);
    const comCais = linhas.find(l => l.nome === 'Com cais');
    assert.deepEqual(comCais.celulas.map(c => [c.texto, c.melhor]), [['2', true], ['1', false]]);
    assert.equal(comCais.celulas[1].delta, '−1');
    const planeadas = linhas.find(l => l.nome === 'Circulações planeadas');
    assert.ok(planeadas.celulas.every(c => !c.melhor && !c.delta));
});

test('cenário com erro fica n/d e o gráfico só desenha os que têm plano', () => {
    const r = comparacao.avaliar({ cenario: regiao, passagens: [], plano: null, ordemSaida, totalCais: 17, erro: 'falhou' });
    assert.ok(comparacao.linhas([r]).every(l => l.celulas[0].texto === 'n/d'));
    assert.equal(comparacao.grafico([r]), '');
});
