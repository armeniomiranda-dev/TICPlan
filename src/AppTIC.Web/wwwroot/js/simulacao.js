(() => {
    'use strict';

    const root = document.getElementById('simulacao');
    const el = id => document.getElementById(id);
    const zonas = SimulacaoPlaneador.zonas;
    const caisComuns = SimulacaoPlaneador.caisComuns;
    const cores = { Lisboa: '#3b82f6', Fora: '#f59e0b', Norte: '#a855f7', Outros: '#10b981' };
    const canvas = el('sim-canvas');
    const ctx = canvas.getContext('2d');

    let viagens = [];
    let alocacoes = new Map();
    let tempo = 0;
    let emExecucao = false;
    let ultimoFrame = null;
    let carregamento = null;
    let caisSelecionado = null;
    let tokenTerminal = null;
    let estadoTerminal = [];
    let versaoPlaneamento = 0;

    function hora(segundos) {
        const dia = Math.floor(segundos / 86400);
        const valor = ((Math.floor(segundos) % 86400) + 86400) % 86400;
        const texto = [Math.floor(valor / 3600), Math.floor(valor % 3600 / 60), valor % 60].map(n => String(n).padStart(2, '0')).join(':');
        return texto + (dia < 0 ? ' (dia anterior)' : dia > 0 ? ' (dia seguinte)' : '');
    }

    function texto(tag, valor, classe) {
        const elemento = document.createElement(tag);
        elemento.textContent = valor;
        if (classe) elemento.className = classe;
        return elemento;
    }

    function adicionarEvento(mensagem, erro = false) {
        const eventos = el('sim-events');
        if (!eventos) return;
        const linha = document.createElement('div');
        linha.className = `sim-event-entry${erro ? ' error' : ''}`;
        const agora = new Date();
        linha.textContent = `[${agora.toTimeString().slice(0, 8)}] ${mensagem}`;
        eventos.prepend(linha);
        while (eventos.children.length > 80) eventos.removeChild(eventos.lastChild);
    }

    function mostrarAlertaLotacao(mensagem) {
        const alerta = el('sim-overflow-alert');
        const textoAlerta = el('sim-overflow-text');
        if (!alerta || !textoAlerta) return;
        textoAlerta.textContent = mensagem;
        alerta.hidden = false;
    }

    function esconderAlertaLotacao() {
        const alerta = el('sim-overflow-alert');
        if (alerta) alerta.hidden = true;
    }

    async function planear() {
        const versao = ++versaoPlaneamento;
        const candidatas = viagens
            .filter(v => Object.hasOwn(zonas, v.zona) && v.FimSegundos > v.InicioSegundos)
            .map(v => ({
                AutocarroId: v.Id,
                Zona: v.zona,
                ChegadaMinutos: Math.floor(v.InicioSegundos / 60),
                PartidaMinutos: Math.max(Math.floor(v.InicioSegundos / 60) + 1, Math.ceil(v.FimSegundos / 60)),
                CaisFixo: null
            }));

        if (candidatas.length === 0) {
            alocacoes = new Map();
            atualizar();
            return;
        }

        try {
            const resultado = await pedidoTerminal('alocar-preview', 'POST', candidatas);
            if (versao !== versaoPlaneamento) return;

            const mapa = new Map();
            for (const item of candidatas)
                mapa.set(item.AutocarroId, null);
            for (const alocacao of resultado ?? [])
                mapa.set(alocacao.AutocarroId, alocacao.Cais);

            alocacoes = mapa;
            esconderAlertaLotacao();
            atualizar();
        } catch (erro) {
            if (versao !== versaoPlaneamento) return;
            alocacoes = SimulacaoPlaneador.alocar(viagens);
            mostrarAlertaLotacao(`Preview OR-Tools indisponível: ${erro.message}`);
            adicionarEvento(`[OR-Tools] Falha no preview: ${erro.message}`, true);
            atualizar();
        }
    }

    function criarLista() {
        const lista = el('sim-list');
        lista.replaceChildren();
        el('sim-count').textContent = `${viagens.length} circulações`;
        if (!viagens.length) {
            lista.append(texto('p', 'Não existem circulações com horário para esta data.', 'sim-empty'));
            return;
        }

        for (const v of viagens) {
            const artigo = document.createElement('article');
            artigo.className = 'sim-trip';
            const titulo = document.createElement('div');
            titulo.className = 'sim-trip-heading';
            titulo.append(texto('strong', v.ViagemId), texto('span', v.Operador));
            artigo.append(titulo);
            artigo.append(texto('small', `${v.Linha || 'Linha não indicada'} · ${v.Origem || 'Origem não indicada'} → ${v.Destino || 'Destino não indicado'}`));

            let horario = `${hora(v.InicioSegundos)} – ${hora(v.FimSegundos)}`;
            if (v.PermanenciaEstimada) horario += ' · permanência estimada';
            if (v.FimSegundos <= v.InicioSegundos) horario += ' · sem duração para simular';
            artigo.append(texto('small', horario));

            const label = texto('label', 'Zona simulada');
            const select = document.createElement('select');
            select.setAttribute('aria-label', `Zona simulada para ${v.ViagemId}`);
            for (const nome of ['', 'Lisboa', 'Fora', 'Norte', 'Outros']) {
                const option = document.createElement('option');
                option.value = nome;
                option.textContent = nome || 'Não atribuída';
                select.append(option);
            }
            select.value = v.zona;
            select.disabled = v.FimSegundos <= v.InicioSegundos;
            select.addEventListener('change', () => { v.zona = select.value; planear(); });
            label.append(select);
            artigo.append(label);
            lista.append(artigo);
        }
    }

    function desenharAutocarro(x, y, angulo, cor) {
        ctx.save();
        ctx.translate(x, y);
        ctx.rotate(angulo);
        ctx.fillStyle = cor;
        ctx.fillRect(-28, -10, 56, 20);
        ctx.fillStyle = '#e2e8f0';
        ctx.fillRect(-21, -7, 37, 4);
        ctx.fillRect(-21, 3, 37, 4);
        ctx.fillStyle = '#0f172a';
        ctx.fillRect(19, -6, 5, 12);
        ctx.restore();
    }

    function pintarMapa(ativos) {
        const w = canvas.width;
        const h = canvas.height;
        ctx.clearRect(0, 0, w, h);
        ctx.fillStyle = '#0a0f1d';
        ctx.fillRect(0, 0, w, h);

        ctx.strokeStyle = 'rgba(255,255,255,.035)';
        for (let x = 0; x < w; x += 30) { ctx.beginPath(); ctx.moveTo(x, 0); ctx.lineTo(x, h); ctx.stroke(); }
        for (let y = 0; y < h; y += 30) { ctx.beginPath(); ctx.moveTo(0, y); ctx.lineTo(w, y); ctx.stroke(); }

        ctx.strokeStyle = '#475569';
        ctx.lineWidth = 2;
        ctx.beginPath();
        ctx.moveTo(280, 180);
        ctx.lineTo(1070, 20);
        ctx.lineTo(1070, 255);
        ctx.stroke();

        ctx.fillStyle = '#0f172a';
        ctx.fillRect(285, 313, 785, 125);
        ctx.strokeStyle = '#334155';
        ctx.strokeRect(285, 313, 785, 125);

        ctx.fillStyle = '#1e293b';
        ctx.fillRect(245, 204, 825, 40);
        ctx.setLineDash([10, 10]);
        ctx.strokeStyle = '#38bdf8';
        ctx.beginPath();
        ctx.moveTo(260, 224);
        ctx.lineTo(1060, 224);
        ctx.stroke();
        ctx.setLineDash([]);

        ctx.beginPath();
        ctx.arc(145, 348, 62, 0, Math.PI * 2);
        ctx.fillStyle = '#1e293b';
        ctx.fill();
        ctx.beginPath();
        ctx.arc(145, 348, 33, 0, Math.PI * 2);
        ctx.fillStyle = '#0f172a';
        ctx.fill();

        ctx.strokeStyle = '#475569';
        ctx.beginPath();
        ctx.moveTo(195, 309);
        ctx.lineTo(245, 224);
        ctx.stroke();

        ctx.fillStyle = '#94a3b8';
        ctx.font = 'bold 12px sans-serif';
        ctx.textAlign = 'center';
        ctx.fillText('ROTUNDA', 145, 352);
        ctx.fillText('ENTRADA ↑', 185, 477);
        ctx.fillText('ENTRADA →', 290, 196);
        ctx.fillText('SAÍDA →', 1030, 196);
        ctx.fillStyle = '#64748b';
        ctx.fillText('GARE DE PASSAGEIROS', 670, 380);

        for (const cais of SimulacaoPlanta.cais) {
            const zona = Object.keys(zonas).find(nome => zonas[nome].includes(cais.id));
            const corCais = zona ? cores[zona] : '#10b981';
            const v = ativos.find(a => alocacoes.get(a.Id) === cais.id);
            ctx.save();
            ctx.translate(cais.x, cais.y);
            ctx.rotate(cais.angulo);
            ctx.fillStyle = v ? '#263c55' : '#172437';
            ctx.strokeStyle = corCais;
            ctx.lineWidth = 2;
            ctx.fillRect(-cais.comprimento / 2, -cais.largura / 2, cais.comprimento, cais.largura);
            ctx.strokeRect(-cais.comprimento / 2, -cais.largura / 2, cais.comprimento, cais.largura);
            ctx.restore();

            if (v) {
                const transicao = Math.min(120, (v.FimSegundos - v.InicioSegundos) / 4);
                const decorrido = tempo - v.InicioSegundos;
                const restante = v.FimSegundos - tempo;
                let posicao;
                if (decorrido < transicao) {
                    posicao = SimulacaoPlanta.pontoNoPercurso([
                        { x: 185, y: 465 }, { x: 145, y: 410 }, { x: 100, y: 348 },
                        { x: 190, y: 309 }, { x: 245, y: 224 }, { x: cais.x, y: 224 }, cais
                    ], decorrido / transicao);
                } else if (restante < transicao) {
                    posicao = SimulacaoPlanta.pontoNoPercurso([cais, { x: cais.x, y: 224 }, { x: 1080, y: 224 }], 1 - restante / transicao);
                } else {
                    posicao = cais;
                }
                desenharAutocarro(posicao.x, posicao.y, posicao.angulo, cores[v.zona]);
            }

            ctx.textAlign = 'center';
            ctx.fillStyle = corCais;
            ctx.font = 'bold 12px sans-serif';
            ctx.fillText(`C${cais.id}`, cais.x, cais.id <= 8 ? cais.y - 23 : cais.y + 32);
            ctx.fillStyle = '#e2e8f0';
            ctx.font = '11px sans-serif';
            ctx.fillText(v ? v.ViagemId : 'Livre', cais.x, cais.id <= 8 ? cais.y + 38 : cais.y + 46, 80);
        }
    }

    function atualizar() {
        const ativos = viagens.filter(v => alocacoes.has(v.Id) && v.InicioSegundos <= tempo && tempo < v.FimSegundos);
        const ocupados = ativos.filter(v => alocacoes.get(v.Id) != null);
        const semCais = ativos.filter(v => alocacoes.get(v.Id) == null);

        el('sim-clock').textContent = hora(tempo);
        el('sim-time').value = Math.min(86400, Math.floor(tempo));

        const resumo = el('sim-summary');
        resumo.replaceChildren();
        for (const zona of Object.keys(zonas)) {
            const numero = ocupados.filter(v => zonas[zona].includes(alocacoes.get(v.Id))).length;
            resumo.append(texto('span', `${zona}: ${numero} / ${zonas[zona].length}`));
        }
        const comunsOcupados = ocupados.filter(v => caisComuns.includes(alocacoes.get(v.Id))).length;
        resumo.append(texto('span', `Comuns: ${comunsOcupados} / ${caisComuns.length}`));

        el('sim-conflicts').textContent = semCais.length
            ? `Sem cais na simulação (${semCais.length}): ${semCais.map(v => v.ViagemId).join(', ')}`
            : '';

        pintarMapa(ocupados);

        if (caisSelecionado !== null) {
            const v = ocupados.find(a => alocacoes.get(a.Id) === caisSelecionado);
            const livreComum = caisComuns.includes(caisSelecionado) ? ' (reserva comum)' : '';
            el('sim-inspector').textContent = v
                ? `Cais ${caisSelecionado}: ${v.ViagemId} · ${v.Operador} · ${hora(v.InicioSegundos)} – ${hora(v.FimSegundos)}`
                : `Cais ${caisSelecionado}: livre na simulação${livreComum}.`;
        }
    }

    function renderPainelCais() {
        const painel = el('sim-bays');
        if (!painel) return;
        painel.replaceChildren();
        const porCais = new Map();
        for (const registo of estadoTerminal) {
            if (registo.CaisAlocado != null && registo.Estado !== 'Concluido') porCais.set(registo.CaisAlocado, registo);
        }

        for (const c of SimulacaoPlanta.cais) {
            const registo = porCais.get(c.id);
            const linha = document.createElement('div');
            linha.className = 'sim-bay-row';
            const estado = registo ? (registo.Estado === 'Atracado' ? 'Atracado' : 'Alocado') : 'Livre';
            const detalhe = registo ? `${registo.AutocarroId} · ${registo.Zona}` : 'Disponível';
            linha.append(texto('span', `Cais ${c.id} · ${estado}`), texto('span', detalhe));
            painel.append(linha);
        }

        const ocupados = estadoTerminal.filter(r => r.CaisAlocado != null && r.Estado !== 'Concluido').length;
        const total = SimulacaoPlanta.cais.length;
        el('sim-bay-count').textContent = `${ocupados} / ${total}`;
    }

    async function garantirTokenTerminal() {
        if (tokenTerminal) return tokenTerminal;
        const resposta = await fetch('/api/terminal/antiforgery', { headers: { Accept: 'application/json' }, cache: 'no-store', credentials: 'same-origin' });
        if (!resposta.ok) throw new Error('Não foi possível obter token de segurança do terminal.');
        const json = await resposta.json();
        tokenTerminal = json.Token;
        return tokenTerminal;
    }

    async function pedidoTerminal(caminho, metodo = 'GET', corpo = null) {
        const opcoes = {
            method: metodo,
            headers: { Accept: 'application/json' },
            cache: 'no-store',
            credentials: 'same-origin'
        };

        if (metodo !== 'GET') {
            opcoes.headers['Content-Type'] = 'application/json';
            opcoes.headers.RequestVerificationToken = await garantirTokenTerminal();
            opcoes.body = JSON.stringify(corpo ?? {});
        }

        const resposta = await fetch(`/api/terminal/${caminho}`, opcoes);
        const json = resposta.headers.get('content-type')?.includes('application/json') ? await resposta.json() : null;
        if (!resposta.ok) throw new Error(json?.Mensagem || `Falha HTTP ${resposta.status}.`);
        return json;
    }

    async function sincronizarEstadoTerminal(registarEvento = false) {
        const estado = await pedidoTerminal('estado');
        estadoTerminal = Array.isArray(estado) ? estado : [];
        renderPainelCais();
        if (registarEvento) adicionarEvento(`Estado do terminal sincronizado (${estadoTerminal.length} ativos).`);
    }

    function parar() {
        emExecucao = false;
        ultimoFrame = null;
        el('sim-play').textContent = 'Iniciar';
    }

    function frame(agora) {
        if (emExecucao) {
            if (ultimoFrame !== null) tempo = Math.min(86400, tempo + (agora - ultimoFrame) / 1000 * Number(el('sim-speed').value));
            ultimoFrame = agora;
            if (tempo >= 86400) parar();
            atualizar();
        }
        requestAnimationFrame(frame);
    }

    async function carregar() {
        if (carregamento) carregamento.abort();
        carregamento = new AbortController();
        const pedido = carregamento;

        parar();
        viagens = [];
        alocacoes.clear();
        tempo = 0;
        criarLista();
        atualizar();

        ['sim-play', 'sim-reset', 'sim-time', 'sim-bulk-apply'].forEach(id => { el(id).disabled = true; });
        const status = el('sim-status');
        status.className = 'sim-status';
        status.textContent = 'A carregar circulações...';

        try {
            if (!el('sim-date').value) throw new Error('Selecione uma data.');
            const url = new URL(root.dataset.url, window.location.href);
            url.searchParams.set('data', el('sim-date').value);
            const resposta = await fetch(url, { signal: pedido.signal, headers: { Accept: 'application/json' }, cache: 'no-store', credentials: 'same-origin' });
            if (!resposta.ok || !resposta.headers.get('content-type')?.includes('application/json'))
                throw new Error('Não foi possível carregar os dados. Confirme a sessão e tente novamente.');

            const dados = await resposta.json();
            if (pedido !== carregamento) return;

            viagens = dados.map(v => ({ ...v, zona: '' }));
            criarLista();
            planear();

            ['sim-play', 'sim-reset', 'sim-time', 'sim-bulk-apply'].forEach(id => { el(id).disabled = false; });
            status.textContent = `${viagens.length} circulações carregadas. Escolha as zonas para iniciar a simulação.`;
        } catch (erro) {
            if (erro.name === 'AbortError' || pedido !== carregamento) return;
            status.className = 'sim-status error';
            status.textContent = erro.message;
        }
    }

    async function onDespachar(evento) {
        evento.preventDefault();
        const id = el('sim-dispatch-id').value.trim();
        const zona = el('sim-dispatch-zone').value;
        const chegadaMinutos = Number(el('sim-dispatch-arrival').value);
        const partidaMinutos = Number(el('sim-dispatch-departure').value);

        if (!id || !Number.isFinite(chegadaMinutos) || !Number.isFinite(partidaMinutos) || chegadaMinutos < 0 || chegadaMinutos >= partidaMinutos) {
            mostrarAlertaLotacao('Dados inválidos para despacho.');
            adicionarEvento('Pedido de despacho rejeitado por dados inválidos.', true);
            return;
        }

        try {
            const resposta = await pedidoTerminal('chegada', 'POST', { id, zona, chegadaMinutos, partidaMinutos });
            esconderAlertaLotacao();
            adicionarEvento(`[OR-Tools] ${id} atribuído ao cais ${resposta.CaisAtribuido}.`);
            caisSelecionado = resposta.CaisAtribuido;
            await sincronizarEstadoTerminal();
        } catch (erro) {
            mostrarAlertaLotacao(erro.message);
            adicionarEvento(`[OR-Tools] Falta de espaço para ${id}: ${erro.message}`, true);
            await sincronizarEstadoTerminal();
        }
    }

    async function onAtracar() {
        const id = el('sim-dispatch-id').value.trim();
        if (!id) return;
        try {
            await pedidoTerminal(`atracar/${encodeURIComponent(id)}`, 'POST');
            esconderAlertaLotacao();
            adicionarEvento(`[Operação] ${id} marcado como atracado.`);
            await sincronizarEstadoTerminal();
        } catch (erro) {
            mostrarAlertaLotacao(erro.message);
            adicionarEvento(`[Operação] Falha ao atracar ${id}: ${erro.message}`, true);
        }
    }

    async function onPartida() {
        const id = el('sim-dispatch-id').value.trim();
        if (!id) return;
        try {
            await pedidoTerminal(`partida/${encodeURIComponent(id)}`, 'POST');
            esconderAlertaLotacao();
            adicionarEvento(`[Operação] ${id} marcado como partida.`);
            await sincronizarEstadoTerminal();
        } catch (erro) {
            mostrarAlertaLotacao(erro.message);
            adicionarEvento(`[Operação] Falha ao concluir ${id}: ${erro.message}`, true);
        }
    }

    el('sim-load').addEventListener('click', carregar);
    el('sim-date').addEventListener('change', carregar);
    el('sim-dispatch-form').addEventListener('submit', onDespachar);
    el('sim-dispatch-dock').addEventListener('click', onAtracar);
    el('sim-dispatch-leave').addEventListener('click', onPartida);
    el('sim-events-clear').addEventListener('click', () => el('sim-events').replaceChildren());

    canvas.addEventListener('click', evento => {
        const rect = canvas.getBoundingClientRect();
        const x = (evento.clientX - rect.left) * canvas.width / rect.width;
        const y = (evento.clientY - rect.top) * canvas.height / rect.height;
        const cais = SimulacaoPlanta.encontrarCais(x, y);
        if (!cais) return;
        caisSelecionado = cais.id;
        const registo = estadoTerminal.find(r => r.CaisAlocado === cais.id && r.Estado !== 'Concluido');
        if (registo) el('sim-dispatch-id').value = registo.AutocarroId;
        atualizar();
    });

    el('sim-play').addEventListener('click', () => {
        if (emExecucao) parar();
        else {
            if (tempo >= 86400) tempo = 0;
            emExecucao = true;
            ultimoFrame = null;
            el('sim-play').textContent = 'Pausar';
        }
    });

    el('sim-reset').addEventListener('click', () => { parar(); tempo = 0; atualizar(); });
    el('sim-time').addEventListener('input', e => {
        tempo = Number(e.target.value);
        atualizar();
        const minutos = Math.floor(tempo / 60);
        el('sim-dispatch-arrival').value = String(minutos);
        el('sim-dispatch-departure').value = String(Math.min(1440, minutos + 20));
    });

    el('sim-bulk-apply').addEventListener('click', () => {
        const zona = el('sim-bulk-zone').value;
        viagens.forEach(v => { v.zona = zona; });
        criarLista();
        planear();
    });

    carregar();
    sincronizarEstadoTerminal(true).catch(() => adicionarEvento('Não foi possível sincronizar o estado do terminal.', true));
    requestAnimationFrame(frame);
})();
