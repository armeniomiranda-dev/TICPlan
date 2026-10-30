'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const planta = require('../src/AppTIC.Web/wwwroot/js/simulacao-planta.js');
const planeador = require('../src/AppTIC.Web/wwwroot/js/simulacao-planeador.js');

class Elemento {
    constructor() {
        this.listeners = {};
        this.dataset = {};
        this.children = [];
        this.value = '';
        this.textContent = '';
        this.hidden = false;
        this.disabled = false;
        this.className = '';
    }
    addEventListener(tipo, funcao) { this.listeners[tipo] = funcao; }
    setAttribute() {}
    append(...filhos) { this.children.push(...filhos); }
    replaceChildren(...filhos) { this.children = filhos; }
    prepend(...filhos) { this.children = [...filhos, ...this.children]; }
    removeChild(filho) { this.children = this.children.filter(c => c !== filho); }
}

test('despacho OR-Tools atualiza painel de cais, alerta e eventos', async () => {
    const elementos = new Map();
    const get = id => {
        if (!elementos.has(id)) elementos.set(id, new Elemento());
        return elementos.get(id);
    };

    const ctx = new Proxy({}, {
        get: (obj, prop) => obj[prop] ?? (() => {}),
        set: (obj, prop, valor) => { obj[prop] = valor; return true; }
    });

    const canvas = get('sim-canvas');
    canvas.width = 1100;
    canvas.height = 500;
    canvas.getContext = () => ctx;
    canvas.getBoundingClientRect = () => ({ left: 10, top: 20, width: 550, height: 250 });

    get('simulacao').dataset.url = '/Simulacao/Dados';
    get('sim-date').value = '2026-06-02';
    get('sim-speed').value = '300';
    get('sim-bulk-zone').value = '';
    get('sim-dispatch-id').value = 'BUS-1';
    get('sim-dispatch-zone').value = 'Norte';
    get('sim-dispatch-arrival').value = '600';
    get('sim-dispatch-departure').value = '620';

    const document = { getElementById: get, createElement: () => new Elemento() };
    const dados = Array.from({ length: 3 }, (_, i) => ({
        Id: String(i + 1),
        ViagemId: `VIAGEM-${i + 1}`,
        Operador: 'Operador',
        InicioSegundos: 0,
        FimSegundos: 3600,
        zona: 'Norte'
    }));

    let estadoTerminal = [];
    const respostaJson = (status, json) => ({
        ok: status >= 200 && status < 300,
        status,
        headers: { get: h => (h.toLowerCase() === 'content-type' ? 'application/json' : null) },
        json: async () => json
    });

    const ambiente = {
        document,
        window: { location: { href: 'https://localhost/Simulacao' } },
        URL,
        AbortController,
        SimulacaoPlanta: planta,
        SimulacaoPlaneador: planeador,
        requestAnimationFrame: () => {},
        fetch: async (url, options = {}) => {
            const alvo = String(url);
            if (alvo.includes('/Simulacao/Dados')) return respostaJson(200, dados);
            if (alvo.includes('/api/terminal/estado')) return respostaJson(200, estadoTerminal);
            if (alvo.includes('/api/terminal/antiforgery')) return respostaJson(200, { Token: 'abc' });
            if (alvo.includes('/api/terminal/alocar-preview')) {
                const pedidos = JSON.parse(options.body);
                return respostaJson(200, pedidos.map((p, i) => ({ AutocarroId: p.AutocarroId, Cais: 9 + i })));
            }
            if (alvo.includes('/api/terminal/chegada')) {
                const pedido = JSON.parse(options.body);
                if (pedido.id === 'SEM-ESPACO') return respostaJson(409, { Mensagem: 'Falta de espaço na zona pretendida.' });
                estadoTerminal = [{ AutocarroId: pedido.id, Zona: pedido.zona, CaisAlocado: 9, Estado: 'Agendado' }];
                return respostaJson(200, { CaisAtribuido: 9, AlocacoesAtivas: [{ AutocarroId: pedido.id, Cais: 9 }] });
            }
            if (alvo.includes('/api/terminal/atracar/')) {
                estadoTerminal = estadoTerminal.map(s => ({ ...s, Estado: 'Atracado' }));
                return respostaJson(200, { Mensagem: 'ok' });
            }
            if (alvo.includes('/api/terminal/partida/')) {
                estadoTerminal = [];
                return respostaJson(200, { Mensagem: 'ok' });
            }
            return respostaJson(404, { Mensagem: 'not found' });
        }
    };

    const codigo = fs.readFileSync(require.resolve('../src/AppTIC.Web/wwwroot/js/simulacao.js'), 'utf8');
    vm.runInNewContext(codigo, ambiente);
    await new Promise(resolve => setImmediate(resolve));

    assert.equal(get('sim-bays').children.length, 17);

    await get('sim-dispatch-form').listeners.submit({ preventDefault() {} });
    assert.match(get('sim-events').children[0].textContent, /OR-Tools/);
    assert.equal(get('sim-bay-count').textContent, '1 / 17');

    await get('sim-dispatch-dock').listeners.click();
    assert.match(get('sim-events').children[0].textContent, /atracado/);

    get('sim-dispatch-id').value = 'SEM-ESPACO';
    await get('sim-dispatch-form').listeners.submit({ preventDefault() {} });
    assert.equal(get('sim-overflow-alert').hidden, false);
    assert.match(get('sim-overflow-text').textContent, /Falta de espaço/);

    get('sim-dispatch-id').value = 'BUS-1';
    await get('sim-dispatch-leave').listeners.click();
    assert.equal(get('sim-bay-count').textContent, '0 / 17');

    const clicar = id => {
        const cais = planta.cais.find(c => c.id === id);
        canvas.listeners.click({ clientX: 10 + cais.x / 2, clientY: 20 + cais.y / 2 });
    };

    clicar(9);
    assert.match(get('sim-inspector').textContent, /reserva comum/);
});
