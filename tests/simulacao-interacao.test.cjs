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

/** Resolvedor falso: respeita o cais fixo e dá o primeiro cais livre da zona (todas as janelas se sobrepõem no teste). */
function planoFalso(pedidos) {
    const ocupados = new Set(pedidos.filter(p => p.CaisFixo != null).map(p => p.CaisFixo));
    const Alocacoes = [];
    const SemCais = [];
    for (const p of pedidos) {
        const permitidos = [...planeador.zonas[p.Zona], ...planeador.caisComuns];
        const cais = p.CaisFixo ?? permitidos.find(c => !ocupados.has(c));
        if (cais == null) { SemCais.push(p.AutocarroId); continue; }
        ocupados.add(cais);
        Alocacoes.push({ AutocarroId: p.AutocarroId, Cais: cais });
    }
    return { Alocacoes, SemCais, Otimo: true };
}

test('plano do dia usa a categoria e o despacho acrescenta um carro fora do plano', async () => {
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
    get('sim-dispatch-zone').value = 'Comum';
    get('sim-dispatch-arrival').value = '00:10:00';
    get('sim-dispatch-departure').value = '00:30:00';

    const document = { getElementById: get, createElement: () => new Elemento() };
    const dados = Array.from({ length: 8 }, (_, i) => ({
        Id: String(i + 1),
        ViagemId: `VIAGEM-${i + 1}`,
        Operador: 'Operador',
        InicioSegundos: 0,
        FimSegundos: 3600,
        Categoria: i < 3 ? 'Norte' : 'Comum'
    }));

    const pedidosPlaneamento = [];
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
        setTimeout,
        clearTimeout,
        SimulacaoPlanta: planta,
        SimulacaoPlaneador: planeador,
        requestAnimationFrame: () => {},
        fetch: async (url, options = {}) => {
            const alvo = String(url);
            if (alvo.includes('/Simulacao/Dados')) return respostaJson(200, dados);
            if (alvo.includes('/api/terminal/antiforgery')) return respostaJson(200, { Token: 'abc' });
            if (alvo.includes('/api/terminal/planear-dia')) {
                const pedidos = JSON.parse(options.body);
                pedidosPlaneamento.push(pedidos);
                return respostaJson(200, planoFalso(pedidos));
            }
            return respostaJson(404, { Mensagem: 'not found' });
        }
    };

    const codigo = fs.readFileSync(require.resolve('../src/AppTIC.Web/wwwroot/js/simulacao.js'), 'utf8');
    vm.runInNewContext(codigo, ambiente);
    await new Promise(resolve => setImmediate(resolve));

// A zona vem da categoria; o 3.º autocarro Norte passa para um cais comum (11), não para outra zona.    assert.deepEqual(pedidosPlaneamento[0].map(p => p.Zona), ['Norte', 'Norte', 'Norte', 'Comum', 'Comum', 'Comum', 'Comum', 'Comum']);    assert.equal(get('sim-bays').children.length, 17);    assert.equal(get('sim-bay-count').textContent, '8 / 17');    assert.equal(get('sim-overflow-alert').hidden, true);

    // Despacho: carro fora do plano com horas HH:mm:ss.
    await get('sim-dispatch-form').listeners.submit({ preventDefault() {} });
    const extra = pedidosPlaneamento.at(-1).find(p => p.AutocarroId === 'extra-BUS-1');
    assert.deepEqual([extra.Zona, extra.ChegadaMinutos, extra.PartidaMinutos], ['Comum', 10, 30]);
    assert.match(get('sim-events').children[0].textContent, /BUS-1.*cais 17/);

    get('sim-time').listeners.input({ target: { value: '900' } });
    assert.equal(get('sim-bay-count').textContent, '9 / 17');
    assert.equal(get('sim-dispatch-arrival').value, '00:15:00');
    assert.equal(get('sim-dispatch-departure').value, '00:35:00');

    get('sim-dispatch-dock').listeners.click();
    assert.match(get('sim-events').children[0].textContent, /atracado no cais 17/);

    await get('sim-dispatch-leave').listeners.click();
    assert.equal(get('sim-bay-count').textContent, '8 / 17');

    // Carro fora do plano sem espaço: fica sem cais e é assinalado.
    get('sim-dispatch-id').value = 'SEM-ESPACO';
    get('sim-dispatch-zone').value = 'Norte';
    await get('sim-dispatch-form').listeners.submit({ preventDefault() {} });
    assert.match(get('sim-events').children[0].textContent, /Sem cais para SEM-ESPACO/);
    assert.match(get('sim-overflow-text').textContent, /SEM-ESPACO/);

    const clicar = id => {
        const cais = planta.cais.find(c => c.id === id);
        canvas.listeners.click({ clientX: 10 + cais.x / 2, clientY: 20 + cais.y / 2 });
    };

    clicar(9);
    assert.match(get('sim-inspector').textContent, /VIAGEM-1/);
    clicar(1);
    assert.match(get('sim-inspector').textContent, /livre/);
});
