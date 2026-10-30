(() => {
    'use strict';

    const root = document.getElementById('simulacao');
    const el = id => document.getElementById(id);
    const zonas = SimulacaoPlaneador.zonas;
    const caisComuns = SimulacaoPlaneador.caisComuns;
    const cores = { Lisboa: '#3b82f6', Fora: '#f59e0b', Norte: '#a855f7', Comum: '#10b981' };
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
    let versaoPlaneamento = 0;
    let temporizadorPlaneamento = null;
    let assinaturaPainel = '';

    function hora(segundos) {
        const dia = Math.floor(segundos / 86400);
        const valor = ((Math.floor(segundos) % 86400) + 86400) % 86400;
        const texto = [Math.floor(valor / 3600), Math.floor(valor % 3600 / 60), valor % 60].map(n => String(n).padStart(2, '0')).join(':');
        return texto + (dia < 0 ? ' (dia anterior)' : dia > 0 ? ' (dia seguinte)' : '');
    }

    /** "HH:mm[:ss]" → segundos do dia, ou null se inválido. */
    function lerHora(valor) {
        const partes = /^(\d{1,2}):(\d{2})(?::(\d{2}))?$/.exec(valor ?? '');
        if (!partes) return null;
        const [h, m, s] = [Number(partes[1]), Number(partes[2]), Number(partes[3] ?? 0)];
        if (h > 23 || m > 59 || s > 59) return null;
        return h * 3600 + m * 60 + s;
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
        linha.textContent = `[${hora(tempo)}] ${mensagem}`;
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

    function caisPermitidos(zona) {
        return zona === 'Norte' ? zonas.Norte : [...zonas[zona], ...caisComuns];
    }

    /** Cais que não pode mudar ao replanear: atracado manualmente ou já ocupado à hora simulada. */
    function caisFixo(v) {
        const atual = alocacoes.get(v.Id);
        const fixo = v.caisAtracado ?? (tempo > 0 && v.InicioSegundos <= tempo && tempo < v.FimSegundos ? atual : null);
        return fixo != null && caisPermitidos(v.zona).includes(fixo) ? fixo : null;
    }

    function agendarPlaneamento() {
        clearTimeout(temporizadorPlaneamento);
        temporizadorPlaneamento = setTimeout(() => planear(), 300);
    }

    /** Planeamento do dia com OR-Tools: devolve a promessa do plano (ou null se falhou). */
    async function planear() {
        clearTimeout(temporizadorPlaneamento);
        const versao = ++versaoPlaneamento;
        const candidatas = viagens
            .filter(v => Object.hasOwn(zonas, v.zona) && v.FimSegundos > v.InicioSegundos)
            .map(v => ({
                AutocarroId: v.Id,
                Zona: v.zona,
                ChegadaMinutos: Math.floor(v.InicioSegundos / 60),
                PartidaMinutos: Math.max(Math.floor(v.InicioSegundos / 60) + 1, Math.ceil(v.FimSegundos / 60)),
                CaisFixo: caisFixo(v)
            }));

        if (candidatas.length === 0) {
            alocacoes = new Map();
            el('sim-plan').disabled = !viagens.length;
            esconderAlertaLotacao();
            el('sim-status').textContent = `${viagens.length} circulações · nenhuma com zona para planear.`;
            atualizar();
            return null;
        }

        el('sim-plan').disabled = true;
        el('sim-status').textContent = `A otimizar o plano do dia com OR-Tools (${candidatas.length} autocarros)...`;
        try {
            const plano = await pedidoTerminal('planear-dia', 'POST', candidatas);
            if (versao !== versaoPlaneamento) return null;

            const mapa = new Map(candidatas.map(c => [c.AutocarroId, null]));
            for (const alocacao of plano.Alocacoes ?? [])
                mapa.set(alocacao.AutocarroId, alocacao.Cais);
            alocacoes = mapa;

            const semCais = viagens.filter(v => (plano.SemCais ?? []).includes(v.Id));
            el('sim-status').textContent =
                `Plano OR-Tools: ${candidatas.length - semCais.length} com cais, ${semCais.length} sem cais` +
                (plano.Otimo ? ' (ótimo).' : ' (melhor solução encontrada no limite de tempo).');
            if (semCais.length)
                mostrarAlertaLotacao(`${semCais.length} sem cais no plano: ${semCais.map(v => `${v.ViagemId} (${hora(v.InicioSegundos)})`).join(', ')}`);
            else
                esconderAlertaLotacao();
            adicionarEvento(`[OR-Tools] Plano do dia: ${candidatas.length - semCais.length}/${candidatas.length} com cais.`);
            atualizar();
            return plano;
        } catch (erro) {
            if (versao !== versaoPlaneamento) return null;
            alocacoes = SimulacaoPlaneador.alocar(viagens);
            el('sim-status').textContent = 'Plano provisório pela regra do primeiro cais livre (OR-Tools indisponível).';
            mostrarAlertaLotacao(`OR-Tools indisponível: ${erro.message}`);
            adicionarEvento(`[OR-Tools] Falha no planeamento: ${erro.message}`, true);
            atualizar();
            return null;
        } finally {
            if (versao === versaoPlaneamento) el('sim-plan').disabled = false;
        }
    }

    function criarLista() {
        const lista = el('sim-list');
        lista.replaceChildren();
        const extras = viagens.filter(v => v.ForaDoPlano).length;
        el('sim-count').textContent = `${viagens.length - extras} circulações${extras ? ` + ${extras} fora do plano` : ''}`;
        if (!viagens.length) {
            lista.append(texto('p', 'Não existem circulações com horário para esta data.', 'sim-empty'));
            return;
        }

        for (const v of viagens) {
            const artigo = document.createElement('article');
            artigo.className = `sim-trip${v.ForaDoPlano ? ' fora-plano' : ''}`;
            const titulo = document.createElement('div');
            titulo.className = 'sim-trip-heading';
            titulo.append(texto('strong', v.ViagemId), texto('span', v.Operador));
            artigo.append(titulo);
            if (!v.ForaDoPlano)
                artigo.append(texto('small', `${v.Linha || 'Linha não indicada'} · ${v.Origem || 'Origem não indicada'} → ${v.Destino || 'Destino não indicado'}`));

            let horario = `${hora(v.InicioSegundos)} – ${hora(v.FimSegundos)}`;
            if (v.PermanenciaEstimada) horario += ' · permanência estimada';
            if (v.FimSegundos <= v.InicioSegundos) horario += ' · sem duração para simular';
            artigo.append(texto('small', horario));
            artigo.append(texto('small', v.ForaDoPlano ? 'Carro fora do plano (despacho)' : `Categoria: ${v.Categoria || 'não indicada'}`));

            const label = texto('label', 'Zona simulada');
            const select = document.createElement('select');
            select.setAttribute('aria-label', `Zona simulada para ${v.ViagemId}`);
            for (const nome of ['', ...Object.keys(zonas)]) {
                const option = document.createElement('option');
                option.value = nome;
                option.textContent = nome || 'Não atribuída';
                select.append(option);
            }
            select.value = v.zona;
            select.disabled = v.FimSegundos <= v.InicioSegundos;
            select.addEventListener('change', () => { v.zona = select.value; v.caisAtracado = null; agendarPlaneamento(); });
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
                desenharAutocarro(posicao.x, posicao.y, posicao.angulo, v.ForaDoPlano ? '#f43f5e' : cores[v.zona]);
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
        const emReserva = ocupados.filter(v => v.zona !== 'Norte' && caisComuns.includes(alocacoes.get(v.Id))).length;
        resumo.append(texto('span', `Reserva 9–10 (Lisboa/Fora): ${emReserva}`));

        el('sim-conflicts').textContent = semCais.length
            ? `Sem cais na simulação (${semCais.length}): ${semCais.map(v => v.ViagemId).join(', ')}`
            : '';

        pintarMapa(ocupados);
        renderPainelCais(ocupados);

        if (caisSelecionado !== null) {
            const v = ocupados.find(a => alocacoes.get(a.Id) === caisSelecionado);
            const reserva = caisComuns.includes(caisSelecionado) ? ' (reserva de Lisboa/Fora)' : '';
            el('sim-inspector').textContent = v
                ? `Cais ${caisSelecionado}: ${v.ViagemId} · ${v.Operador} · ${v.zona} · ${hora(v.InicioSegundos)} – ${hora(v.FimSegundos)}${v.caisAtracado ? ' · atracado' : ''}`
                : `Cais ${caisSelecionado}: livre na simulação${reserva}.`;
        }
    }

    /** Painel de cais à hora simulada; só redesenha quando a ocupação muda. */
    function renderPainelCais(ocupados) {
        const painel = el('sim-bays');
        const porCais = new Map(ocupados.map(v => [alocacoes.get(v.Id), v]));
        const assinatura = SimulacaoPlanta.cais.map(c => `${c.id}:${porCais.get(c.id)?.Id ?? ''}:${porCais.get(c.id)?.caisAtracado ?? ''}`).join('|');
        if (assinatura === assinaturaPainel) return;
        assinaturaPainel = assinatura;

        painel.replaceChildren();
        for (const c of SimulacaoPlanta.cais) {
            const v = porCais.get(c.id);
            const linha = document.createElement('div');
            linha.className = 'sim-bay-row';
            const estado = v ? (v.caisAtracado ? 'Atracado' : 'Ocupado') : 'Livre';
            const detalhe = v ? `${v.ViagemId} · ${v.zona}${v.ForaDoPlano ? ' · fora do plano' : ''}` : 'Disponível';
            linha.append(texto('span', `Cais ${c.id} · ${estado}`), texto('span', detalhe));
            painel.append(linha);
        }
        el('sim-bay-count').textContent = `${ocupados.length} / ${SimulacaoPlanta.cais.length}`;
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
        versaoPlaneamento++;
        viagens = [];
        alocacoes = new Map();
        tempo = 0;
        criarLista();
        atualizar();

        ['sim-play', 'sim-reset', 'sim-time', 'sim-bulk-apply', 'sim-plan'].forEach(id => { el(id).disabled = true; });
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

            viagens = dados.map(v => ({ ...v, zona: Object.hasOwn(zonas, v.Categoria ?? '') ? v.Categoria : '', caisAtracado: null }));
            criarLista();
            ['sim-play', 'sim-reset', 'sim-time', 'sim-bulk-apply', 'sim-plan'].forEach(id => { el(id).disabled = false; });

            const semCategoria = viagens.filter(v => !v.zona).length;
            await planear();
            if (semCategoria)
                adicionarEvento(`${semCategoria} circulações sem categoria ficaram fora do plano; escolha-lhes uma zona.`, true);
        } catch (erro) {
            if (erro.name === 'AbortError' || pedido !== carregamento) return;
            status.className = 'sim-status error';
            status.textContent = erro.message;
        }
    }

    /** Viagem visada pelo ID do formulário: um carro fora do plano ou uma circulação (de preferência a que está no terminal). */
    function viagemPorId(id) {
        const candidatas = viagens.filter(v => v.ViagemId === id);
        return candidatas.find(v => v.InicioSegundos <= tempo && tempo < v.FimSegundos) ?? candidatas[0] ?? null;
    }

    async function onDespachar(evento) {
        evento.preventDefault();
        const id = el('sim-dispatch-id').value.trim();
        const zona = el('sim-dispatch-zone').value;
        const chegada = lerHora(el('sim-dispatch-arrival').value);
        let partida = lerHora(el('sim-dispatch-departure').value);
        if (chegada != null && partida != null && partida <= chegada) partida += 86400; // parte no dia seguinte

        if (!id || chegada == null || partida == null || !Object.hasOwn(zonas, zona)) {
            mostrarAlertaLotacao('Indique a matrícula, a zona e as horas de chegada e partida (HH:mm:ss).');
            adicionarEvento('Despacho rejeitado por dados inválidos.', true);
            return;
        }
        if (viagens.some(v => v.ForaDoPlano && v.ViagemId === id)) {
            mostrarAlertaLotacao(`Já existe um carro fora do plano com o identificador ${id}.`);
            return;
        }

        const extra = {
            Id: `extra-${id}`, ViagemId: id, Operador: 'Fora do plano', Linha: null, Origem: null, Destino: null,
            InicioSegundos: chegada, FimSegundos: partida, PermanenciaEstimada: false, Categoria: null,
            ForaDoPlano: true, zona, caisAtracado: null
        };
        viagens.push(extra);
        criarLista();
        adicionarEvento(`[Despacho] ${id} (${zona}) ${hora(chegada)} – ${hora(partida)} acrescentado ao plano.`);

        const plano = await planear();
        if (!plano) return;
        const cais = alocacoes.get(extra.Id);
        if (cais != null) {
            caisSelecionado = cais;
            adicionarEvento(`[OR-Tools] ${id} atribuído ao cais ${cais}.`);
            atualizar();
        } else {
            adicionarEvento(`[OR-Tools] Sem cais para ${id}: o terminal está cheio nessa janela.`, true);
        }
    }

    function onAtracar() {
        const v = viagemPorId(el('sim-dispatch-id').value.trim());
        const cais = v ? alocacoes.get(v.Id) : null;
        if (!v || cais == null) {
            adicionarEvento('[Operação] Autocarro não encontrado ou sem cais atribuído.', true);
            return;
        }
        v.caisAtracado = cais;
        adicionarEvento(`[Operação] ${v.ViagemId} atracado no cais ${cais} (fica fixo ao replanear).`);
        assinaturaPainel = '';
        atualizar();
    }

    async function onPartida() {
        const v = viagemPorId(el('sim-dispatch-id').value.trim());
        if (!v || tempo >= v.FimSegundos) {
            adicionarEvento('[Operação] Autocarro não encontrado ou já partiu.', true);
            return;
        }
        if (tempo < v.InicioSegundos) {
            adicionarEvento(`[Operação] ${v.ViagemId} ainda não chegou (${hora(v.InicioSegundos)}).`, true);
            return;
        }
        v.FimSegundos = Math.max(v.InicioSegundos + 60, Math.floor(tempo));
        v.caisAtracado = null;
        adicionarEvento(`[Operação] ${v.ViagemId} partiu às ${hora(v.FimSegundos)}; cais libertado.`);
        criarLista();
        await planear();
    }

    el('sim-load').addEventListener('click', carregar);
    el('sim-plan').addEventListener('click', () => planear());
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
        const v = viagens.find(a => alocacoes.get(a.Id) === cais.id && a.InicioSegundos <= tempo && tempo < a.FimSegundos);
        if (v) el('sim-dispatch-id').value = v.ViagemId;
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
        el('sim-dispatch-arrival').value = hora(Math.min(tempo, 86399));
        el('sim-dispatch-departure').value = hora(Math.min(tempo, 86399) + 20 * 60).slice(0, 8);
    });

    el('sim-bulk-apply').addEventListener('click', () => {
        const zona = el('sim-bulk-zone').value;
        viagens.forEach(v => {
            if (v.ForaDoPlano) return;
            v.zona = zona === '*' ? (Object.hasOwn(zonas, v.Categoria ?? '') ? v.Categoria : '') : zona;
            v.caisAtracado = null;
        });
        criarLista();
        planear();
    });

    carregar();
    requestAnimationFrame(frame);
})();
