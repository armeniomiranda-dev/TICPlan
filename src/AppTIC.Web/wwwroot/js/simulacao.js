(() => {
    'use strict';

    const root = document.getElementById('simulacao');
    const el = id => document.getElementById(id);
    const ordemSaida = SimulacaoPlanta.ordemSaida;
    const numerosCais = SimulacaoPlanta.cais.map(c => c.id);
    const caisPorId = new Map(SimulacaoPlanta.cais.map(c => [c.id, c]));
    const CHAVE_CONFIG = 'apptic.simulacao.zonamento.v1';
    const nomesModo = { Livre: 'livre (qualquer cais)', Regiao: 'por região', Permanencia: 'por tempo de permanência' };
    const paleta = ['#06b6d4', '#ec4899', '#84cc16', '#f97316', '#8b5cf6', '#14b8a6', '#f43f5e', '#0ea5e9'];
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
    let filas = new Map();
    let filasDe = null;
    let indicadoresDe = null;
    let versaoComparacao = 0;
    let config = carregarConfig();

    /** Zonamento (modo e tipologias dos cais): fica guardado neste browser, nunca na base de dados. */
    function carregarConfig() {
        const omissao = { modo: 'Regiao', esquemas: SimulacaoPlaneador.esquemasPorOmissao(ordemSaida) };
        try {
            const guardado = JSON.parse(globalThis.localStorage?.getItem(CHAVE_CONFIG) ?? 'null');
            const esquemaValido = e => Array.isArray(e?.categorias) && e.categorias.length > 0 && e.cais && typeof e.cais === 'object';
            if (guardado && Object.hasOwn(omissao.esquemas, guardado.modo)) {
                // Mantém os esquemas guardados válidos; os que faltam (ex.: o modo Livre, acrescentado depois) vêm da predefinição.
                const esquemas = Object.fromEntries(Object.entries(omissao.esquemas)
                    .map(([m, e]) => [m, esquemaValido(guardado.esquemas?.[m]) ? guardado.esquemas[m] : e]));
                if (esquemaValido(esquemas[guardado.modo])) return { modo: guardado.modo, esquemas };
            }
        } catch { /* configuração ilegível: usa a predefinida */ }
        return omissao;
    }

    function guardarConfig() {
        try { globalThis.localStorage?.setItem(CHAVE_CONFIG, JSON.stringify(config)); } catch { /* sem armazenamento local */ }
    }

    const esquema = () => config.esquemas[config.modo];
    const categoria = id => SimulacaoPlaneador.categoriaDe(esquema(), id);
    const cor = id => categoria(id)?.cor ?? '#64748b';
    const rotulo = id => categoria(id)?.nome ?? 'Não atribuída';
    const caisDe = id => SimulacaoPlaneador.caisDaCategoria(esquema(), id);
    const minutosDe = v => (v.FimSegundos - v.InicioSegundos) / 60;

    /**
     * Zona simulada num modo de zonamento: a escolhida à mão nesse modo ou, sem escolha, a automática
     * (a categoria da circulação, por região, a do tempo de paragem, por permanência, ou a única categoria, no livre).
     */
    function zonaNoModo(v, modo) {
        const e = config.esquemas[modo];
        const existe = id => SimulacaoPlaneador.categoriaDe(e, id) !== null;
        const manual = v.manual[modo];
        if (manual !== undefined) return manual === '' || existe(manual) ? manual : '';
        if (e.criterio === 'Livre') return e.categorias[0]?.id ?? '';
        if (e.criterio === 'Permanencia') return SimulacaoPlaneador.categoriaPorPermanencia(e, minutosDe(v));
        return existe(v.Categoria ?? '') ? v.Categoria : '';
    }

    const zonaDe = v => zonaNoModo(v, config.modo);

    /** [1, 2, 3, 4, 9] → "1–4, 9". */
    function formatarCais(lista) {
        const partes = [];
        const ordenada = [...lista].sort((a, b) => a - b);
        for (let i = 0; i < ordenada.length; i++) {
            let j = i;
            while (ordenada[j + 1] === ordenada[j] + 1) j++;
            partes.push(j > i ? `${ordenada[i]}–${ordenada[j]}` : String(ordenada[i]));
            i = j;
        }
        return partes.join(', ') || '—';
    }

    function filasAtuais() {
        if (filasDe !== alocacoes) {
            filas = SimulacaoPlanta.filas(viagens
                .filter(v => alocacoes.get(v.Id) != null && v.FimSegundos > v.InicioSegundos)
                .map(v => ({ id: v.Id, cais: caisPorId.get(alocacoes.get(v.Id)), inicio: v.InicioSegundos, fim: v.FimSegundos })));
            filasDe = alocacoes;
        }
        return filas;
    }

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
        const o = SimulacaoPlaneador.opcoes(esquema(), zona, ordemSaida);
        return [...o.primarios, ...o.reserva, ...o.outros];
    }

    /** Cais que não pode mudar ao replanear: atracado manualmente ou já ocupado à hora simulada. */
    function caisFixo(v) {
        const atual = alocacoes.get(v.Id);
        const fixo = v.caisAtracado ?? (tempo > 0 && v.InicioSegundos <= tempo && tempo < v.FimSegundos ? atual : null);
        return fixo != null && caisPermitidos(zonaDe(v)).includes(fixo) ? fixo : null;
    }

    function agendarPlaneamento() {
        clearTimeout(temporizadorPlaneamento);
        temporizadorPlaneamento = setTimeout(() => planear(), 300);
    }

    /** Planeamento do dia com Otimização: devolve a promessa do plano (ou null se falhou). */
    async function planear() {
        clearTimeout(temporizadorPlaneamento);
        invalidarComparacao();
        const versao = ++versaoPlaneamento;
        const candidatas = viagens
            .filter(v => categoria(zonaDe(v)) && v.FimSegundos > v.InicioSegundos)
            .map(v => ({
                AutocarroId: v.Id,
                Categoria: zonaDe(v),
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
        el('sim-status').textContent = `A otimizar o plano do dia com Otimização (${candidatas.length} autocarros)...`;
        try {
            const plano = await pedidoTerminal('planear-dia', 'POST', SimulacaoPlaneador.pedido(esquema(), candidatas, ordemSaida));
            if (versao !== versaoPlaneamento) return null;

            const mapa = new Map(candidatas.map(c => [c.AutocarroId, null]));
            for (const alocacao of plano.Alocacoes ?? [])
                mapa.set(alocacao.AutocarroId, alocacao.Cais);
            alocacoes = mapa;

            const semCais = viagens.filter(v => (plano.SemCais ?? []).includes(v.Id));
            el('sim-status').textContent =
                `Plano Otimização (${nomesModo[config.modo]}): ${candidatas.length - semCais.length} com cais, ${semCais.length} sem cais` +
                (plano.Otimo ? ' (ótimo).' : ' (melhor solução encontrada no limite de tempo).');
            if (semCais.length)
                mostrarAlertaLotacao(`${semCais.length} sem cais no plano: ${semCais.map(v => `${v.ViagemId} (${hora(v.InicioSegundos)})`).join(', ')}`);
            else
                esconderAlertaLotacao();
            adicionarEvento(`[Otimização] Plano do dia: ${candidatas.length - semCais.length}/${candidatas.length} com cais.`);
            atualizar();
            return plano;
        } catch (erro) {
            if (versao !== versaoPlaneamento) return null;
            alocacoes = SimulacaoPlaneador.alocar(viagens.map(v => ({ ...v, zona: zonaDe(v) })), esquema(), ordemSaida);
            el('sim-status').textContent = 'Plano provisório pela regra do primeiro cais livre (Otimização indisponível).';
            mostrarAlertaLotacao(`Otimização indisponível: ${erro.message}`);
            adicionarEvento(`[Otimização] Falha no planeamento: ${erro.message}`, true);
            atualizar();
            return null;
        } finally {
            if (versao === versaoPlaneamento) el('sim-plan').disabled = false;
        }
    }

    function opcao(valor, rotuloOpcao) {
        const option = document.createElement('option');
        option.value = valor;
        option.textContent = rotuloOpcao;
        return option;
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
            else horario += ` · ${Math.round(minutosDe(v))} min`;
            artigo.append(texto('small', horario));
            artigo.append(texto('small', v.ForaDoPlano ? 'Carro fora do plano (despacho)' : `Categoria: ${v.Categoria || 'não indicada'}`));

            const label = texto('label', 'Zona simulada');
            const select = document.createElement('select');
            select.setAttribute('aria-label', `Zona simulada para ${v.ViagemId}`);
            for (const id of ['', ...esquema().categorias.map(c => c.id)]) select.append(opcao(id, rotulo(id)));
            select.value = zonaDe(v);
            select.disabled = v.FimSegundos <= v.InicioSegundos;
            select.addEventListener('change', () => { v.manual[config.modo] = select.value; v.caisAtracado = null; agendarPlaneamento(); });
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

        const posicoes = filasAtuais();
        for (const cais of SimulacaoPlanta.cais) {
            const corCais = cor(esquema().cais[cais.id]);
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

            ctx.textAlign = 'center';
            ctx.fillStyle = corCais;
            ctx.font = 'bold 12px sans-serif';
            ctx.fillText(`C${cais.id}`, cais.x, cais.id <= 8 ? cais.y - 23 : cais.y + 38);
            ctx.fillStyle = '#e2e8f0';
            ctx.font = '11px sans-serif';
            ctx.fillText(v ? v.ViagemId : 'Livre', cais.x, cais.id <= 8 ? cais.y + 38 : cais.y + 50, 80);
        }

        // Autocarros por cima da planta: na via seguem em fila indiana, sem se sobreporem.
        for (const v of ativos) {
            const posicao = SimulacaoPlanta.posicao(posicoes.get(v.Id), tempo);
            if (posicao) desenharAutocarro(posicao.x, posicao.y, posicao.angulo, v.ForaDoPlano ? '#f43f5e' : cor(zonaDe(v)));
        }
    }

    /** Indicadores do plano do dia; só recalcula quando o plano muda. */
    function renderIndicadores() {
        if (indicadoresDe === alocacoes) return;
        indicadoresDe = alocacoes;
        const grupos = el('sim-kpis');
        const horas = el('sim-kpi-horas');
        grupos.replaceChildren();
        horas.replaceChildren();

        const comDuracao = viagens.filter(v => alocacoes.has(v.Id) && v.FimSegundos > v.InicioSegundos);
        const k = SimulacaoIndicadores.calcular({
            passagens: comDuracao.filter(v => alocacoes.get(v.Id) != null).map(v => ({
                id: v.Id, inicio: v.InicioSegundos, fim: v.FimSegundos, cais: alocacoes.get(v.Id),
                limiteMinutos: categoria(esquema().cais[alocacoes.get(v.Id)])?.ateMinutos ?? null
            })),
            semCais: comDuracao.filter(v => alocacoes.get(v.Id) == null).length,
            totalCais: numerosCais.length,
            filas: filasAtuais()
        });
        el('sim-kpi-nota').textContent = k ? `${k.veiculos} veículos com cais · zonamento ${nomesModo[config.modo]}` : '';
        if (!k) {
            grupos.append(texto('p', 'Sem plano para calcular indicadores.', 'sim-empty'));
            return;
        }

        const num = (valor, casas = 1) => valor.toLocaleString('pt-PT', { minimumFractionDigits: casas, maximumFractionDigits: casas });
        const pct = valor => `${num(valor * 100, 0)} %`;
        const seg = valor => valor >= 90 ? `${num(valor / 60)} min` : `${num(valor, 0)} s`;
        const hh = h => `${String(h).padStart(2, '0')}h`;
        const grupo = (titulo, cartoes) => {
            const secao = document.createElement('div');
            secao.className = 'sim-kpi-grupo';
            const lista = document.createElement('div');
            lista.className = 'sim-kpi-cartoes';
            for (const [nome, valor, detalhe, estado] of cartoes) {
                const cartao = document.createElement('div');
                cartao.className = `sim-kpi${estado ? ` ${estado}` : ''}`;
                cartao.append(texto('span', nome, 'sim-kpi-nome'), texto('strong', valor), texto('small', detalhe));
                lista.append(cartao);
            }
            secao.append(texto('h3', titulo), lista);
            return secao;
        };

        const ocupacao = k.taxaOcupacaoMaxima;
        grupos.append(
            grupo('Rotação e capacidade', [
                ['Índice de rotação', `${num(k.rotacaoDia)} veíc./cais`,
                    `no dia · média ${num(k.rotacaoHora, 2)}/h · pico ${num(k.rotacaoHoraPico, 2)}/h às ${hh(k.horaPico)}`],
                ['Tempo médio de permanência', `${num(k.permanenciaMedia)} min`, `máximo ${num(k.permanenciaMaxima, 0)} min`],
                ['Taxa de ocupação máxima', pct(ocupacao),
                    `${k.ocupacaoMaxima} de ${k.totalCais} cais às ${hora(k.instanteOcupacaoMaxima).slice(0, 5)} · ${num(k.minutosAcimaDoLimiar, 0)} min do dia acima de 90 % (ideal: 85–90 %)`,
                    ocupacao > 0.9 ? 'mau' : ocupacao >= 0.85 ? 'aviso' : 'bom']
            ]),
            grupo('Fluidez e atrito', [
                ['Tempo de fila na entrada', seg(k.filaEntradaMedia), `em média · máximo ${seg(k.filaEntradaMaxima)}`,
                    k.filaEntradaMaxima > 120 ? 'aviso' : ''],
                ['Tempo de fila na saída', seg(k.filaSaidaMedia), `em média · máximo ${seg(k.filaSaidaMaxima)} (antecipação para sair em fila)`,
                    k.filaSaidaMaxima > 120 ? 'aviso' : ''],
                ['Bloqueio do canal por manobras', `${seg(k.bloqueioMedioHora)}/h`,
                    `em média · pior hora ${hh(k.horaBloqueioMaximo)}: ${seg(k.bloqueioMaximoHora)}`],
                ['Taxa de paragem indevida', pct(k.taxaParagemIndevida),
                    `${k.semCais} veículos sem cais no plano (parariam no canal)`, k.semCais ? 'mau' : 'bom']
            ]),
            grupo('Processamento', [
                ['Volume processado na hora de ponta', `${k.throughputPico} veíc./h`,
                    `às ${hh(k.horaPico)} · média ${num(k.throughputMedio)}/h · capacidade teórica ${num(k.capacidadeHora, 0)}/h (${pct(k.usoDaCapacidade)} usada)`,
                    k.usoDaCapacidade > 0.9 ? 'mau' : k.usoDaCapacidade >= 0.85 ? 'aviso' : ''],
                ['Desvio ao tempo previsto', k.desvio == null ? 'n/d' : pct(k.desvio),
                    k.desvio == null ? 'só com categorias com limite de tempo (zonamento por permanência)'
                        : `dos ${k.veiculosComLimite} veículos em cais com limite, os que ficam mais tempo do que o da categoria do cais`,
                    k.desvio == null ? '' : k.desvio > 0.1 ? 'aviso' : 'bom']
            ]));

        const maximo = Math.max(1, ...k.entradasPorHora);
        k.entradasPorHora.forEach((entradas, h) => {
            const coluna = document.createElement('div');
            coluna.className = 'sim-kpi-hora';
            coluna.title = `${hh(h)}: ${entradas} entradas · ocupação máxima ${k.ocupacaoPorHora[h]} de ${k.totalCais} cais`;
            const barra = document.createElement('div');
            barra.className = `sim-kpi-barra${k.ocupacaoPorHora[h] > k.totalCais * 0.9 ? ' mau' : ''}`;
            barra.style?.setProperty('height', `${entradas / maximo * 100}%`);
            const traco = document.createElement('div');
            traco.className = 'sim-kpi-traco';
            traco.style?.setProperty('bottom', `${k.ocupacaoPorHora[h] / k.totalCais * 100}%`);
            const area = document.createElement('div');
            area.className = 'sim-kpi-area';
            area.append(barra, traco);
            coluna.append(texto('span', entradas ? String(entradas) : '', 'sim-kpi-valor'), area, texto('span', String(h).padStart(2, '0')));
            horas.append(coluna);
        });
    }

    /** Planeia (de raiz) e avalia um cenário da comparação; uma falha do servidor fica no resultado. */
    async function avaliarCenario(cenario) {
        const passagens = viagens.map(v => ({
            id: v.Id, inicio: v.InicioSegundos, fim: v.FimSegundos,
            zona: cenario.id === 'Livre' ? 'Livre' : zonaNoModo(v, cenario.id)
        }));
        const filasCenario = comCais => SimulacaoPlanta.filas(comCais.map(p => ({ id: p.id, cais: caisPorId.get(p.cais), inicio: p.inicio, fim: p.fim })));
        const base = { cenario, passagens, ordemSaida, totalCais: numerosCais.length, filas: filasCenario };
        const corpo = SimulacaoComparacao.pedido(cenario, passagens, ordemSaida);
        if (!corpo) return SimulacaoComparacao.avaliar({ ...base, plano: { Alocacoes: [], SemCais: [], Otimo: true } });
        try {
            return SimulacaoComparacao.avaliar({ ...base, plano: await pedidoTerminal('planear-dia', 'POST', corpo) });
        } catch (erro) {
            return SimulacaoComparacao.avaliar({ ...base, plano: null, erro: erro.message });
        }
    }

    /** Planeia os três cenários em paralelo sobre as circulações carregadas e mostra os indicadores lado a lado. */
    async function comparar() {
        const versao = ++versaoComparacao;
        const botao = el('sim-cmp-run');
        botao.disabled = true;
        el('sim-cmp-nota').textContent = `A planear 3 cenários com ${viagens.length} circulações...`;
        try {
            const cenarios = SimulacaoComparacao.cenarios(config.esquemas);
            const resultados = await Promise.all(cenarios.map(avaliarCenario));
            if (versao !== versaoComparacao) return;
            renderComparacao(resultados);
            const falhas = resultados.filter(r => r.erro);
            el('sim-cmp-nota').textContent = falhas.length
                ? `Falhou o planeamento de: ${falhas.map(r => `${r.cenario.nome} (${r.erro})`).join(', ')}`
                : `${el('sim-date').value} · ${viagens.length} circulações` +
                  (resultados.some(r => r.otimo === false) ? ' · algum plano ficou no limite de tempo (não provado ótimo)' : ' · todos os planos ótimos');
            adicionarEvento(`[Comparação] ${resultados.map(r => `${r.cenario.nome}: ${r.comCais}/${r.planeadas} com cais`).join(' · ')}`);
        } finally {
            if (versao === versaoComparacao) botao.disabled = !viagens.length;
        }
    }

    function renderComparacao(resultados) {
        const tabela = document.createElement('table');
        tabela.className = 'sim-cmp-tabela';
        const cabecalho = document.createElement('tr');
        cabecalho.append(texto('th', 'Indicador'));
        for (const r of resultados) {
            const th = document.createElement('th');
            th.setAttribute('scope', 'col');
            const marca = document.createElement('span');
            marca.className = 'sim-zona';
            marca.style?.setProperty('--cor', r.cenario.cor);
            th.append(marca, texto('span', r.cenario.nome), texto('small', r.cenario.descricao));
            if (!r.erro && r.planeadas === 0) th.append(texto('small', 'nenhuma circulação tem zona neste zonamento', 'sim-cmp-aviso'));
            cabecalho.append(th);
        }
        const thead = document.createElement('thead');
        thead.append(cabecalho);
        const tbody = document.createElement('tbody');
        let grupo = null;
        for (const linha of SimulacaoComparacao.linhas(resultados)) {
            if (linha.grupo !== grupo) {
                grupo = linha.grupo;
                const tr = document.createElement('tr');
                const th = texto('th', grupo, 'sim-cmp-grupo');
                th.setAttribute('colspan', String(resultados.length + 1));
                tr.append(th);
                tbody.append(tr);
            }
            const tr = document.createElement('tr');
            const nome = document.createElement('th');
            nome.setAttribute('scope', 'row');
            nome.append(texto('span', linha.nome));
            if (linha.ajuda) nome.append(texto('small', linha.ajuda));
            tr.append(nome);
            for (const c of linha.celulas) {
                const td = document.createElement('td');
                if (c.melhor) td.className = 'melhor';
                td.append(texto('strong', c.melhor ? `✓ ${c.texto}` : c.texto));
                if (c.delta) td.append(texto('small', `${c.delta} vs Livre`));
                tr.append(td);
            }
            tbody.append(tr);
        }
        tabela.append(thead, tbody);
        el('sim-cmp-tabela').replaceChildren(tabela);

        const legenda = el('sim-cmp-legenda');
        legenda.replaceChildren(...resultados.filter(r => r.k).map(r => {
            const item = texto('span', r.cenario.nome);
            const marca = document.createElement('span');
            marca.className = 'sim-zona';
            marca.style?.setProperty('--cor', r.cenario.cor);
            item.prepend(marca);
            return item;
        }));
        el('sim-cmp-grafico').innerHTML = SimulacaoComparacao.grafico(resultados);
    }

    /** Os dados ou o zonamento mudaram: a comparação mostrada deixa de corresponder. */
    function invalidarComparacao() {
        versaoComparacao++;
        el('sim-cmp-run').disabled = !viagens.length;
        if (el('sim-cmp-tabela').children.length)
            el('sim-cmp-nota').textContent = 'Os dados ou o zonamento mudaram desde a última comparação; volte a comparar.';
    }

    function atualizar() {
        const ativos = viagens.filter(v => alocacoes.has(v.Id) && v.InicioSegundos <= tempo && tempo < v.FimSegundos);
        const ocupados = ativos.filter(v => alocacoes.get(v.Id) != null);
        const semCais = ativos.filter(v => alocacoes.get(v.Id) == null);

        el('sim-clock').textContent = hora(tempo);
        el('sim-time').value = Math.min(86400, Math.floor(tempo));

        const resumo = el('sim-summary');
        resumo.replaceChildren();
        for (const c of esquema().categorias) {
            const numero = ocupados.filter(v => esquema().cais[alocacoes.get(v.Id)] === c.id).length;
            resumo.append(texto('span', `${c.nome}: ${numero} / ${caisDe(c.id).length}`));
        }
        const foraDaCategoria = ocupados.filter(v => esquema().cais[alocacoes.get(v.Id)] !== zonaDe(v)).length;
        resumo.append(texto('span', `Fora dos cais da sua categoria: ${foraDaCategoria}`));

        el('sim-conflicts').textContent = semCais.length
            ? `Sem cais na simulação (${semCais.length}): ${semCais.map(v => v.ViagemId).join(', ')}`
            : '';

        pintarMapa(ocupados);
        renderPainelCais(ocupados);
        renderIndicadores();

        if (caisSelecionado !== null) {
            const v = ocupados.find(a => alocacoes.get(a.Id) === caisSelecionado);
            const tipologia = ` (${rotulo(esquema().cais[caisSelecionado])})`;
            el('sim-inspector').textContent = v
                ? `Cais ${caisSelecionado}${tipologia}: ${v.ViagemId} · ${v.Operador} · ${rotulo(zonaDe(v))} · ${hora(v.InicioSegundos)} – ${hora(v.FimSegundos)}${v.caisAtracado ? ' · atracado' : ''}`
                : `Cais ${caisSelecionado}${tipologia}: livre na simulação.`;
        }
    }

    /** Painel de cais à hora simulada; só redesenha quando a ocupação muda. */
    function renderPainelCais(ocupados) {
        const painel = el('sim-bays');
        const porCais = new Map(ocupados.map(v => [alocacoes.get(v.Id), v]));
        const assinatura = SimulacaoPlanta.cais.map(c => `${c.id}:${porCais.get(c.id)?.Id ?? ''}:${porCais.get(c.id)?.caisAtracado ?? ''}:${porCais.get(c.id) ? zonaDe(porCais.get(c.id)) : ''}`).join('|');
        if (assinatura === assinaturaPainel) return;
        assinaturaPainel = assinatura;

        painel.replaceChildren();
        for (const c of SimulacaoPlanta.cais) {
            const v = porCais.get(c.id);
            const linha = document.createElement('div');
            linha.className = 'sim-bay-row';
            const estado = v ? (v.caisAtracado ? 'Atracado' : 'Ocupado') : 'Livre';
            const detalhe = v ? `${v.ViagemId} · ${rotulo(zonaDe(v))}${v.ForaDoPlano ? ' · fora do plano' : ''}` : 'Disponível';
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
        invalidarComparacao();

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

            viagens = dados.map(v => ({ ...v, manual: {}, caisAtracado: null }));
            criarLista();
            ['sim-play', 'sim-reset', 'sim-time', 'sim-bulk-apply', 'sim-plan'].forEach(id => { el(id).disabled = false; });

            const semCategoria = viagens.filter(v => !zonaDe(v)).length;
            await planear();
            if (semCategoria)
                adicionarEvento(`${semCategoria} circulações sem zona ficaram fora do plano; escolha-lhes uma zona.`, true);
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

        const automatica = zona === '*' && esquema().criterio === 'Permanencia';
        if (!id || chegada == null || partida == null || !(automatica || categoria(zona))) {
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
            ForaDoPlano: true, manual: automatica ? {} : { [config.modo]: zona }, caisAtracado: null
        };
        viagens.push(extra);
        criarLista();
        adicionarEvento(`[Despacho] ${id} (${rotulo(zonaDe(extra))}) ${hora(chegada)} – ${hora(partida)} acrescentado ao plano.`);

        const plano = await planear();
        if (!plano) return;
        const cais = alocacoes.get(extra.Id);
        if (cais != null) {
            caisSelecionado = cais;
            adicionarEvento(`[Otimização] ${id} atribuído ao cais ${cais}.`);
            atualizar();
        } else {
            adicionarEvento(`[Otimização] Sem cais para ${id}: o terminal está cheio nessa janela.`, true);
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

    /** Legenda, seletores de zona e rótulos que dependem do esquema de zonamento em uso. */
    function renderLegenda() {
        const e = esquema();
        const porPermanencia = e.criterio === 'Permanencia';
        const livre = e.criterio === 'Livre';
        const corpo = el('sim-legenda-corpo');
        corpo.replaceChildren();
        let anterior = null;
        for (const c of e.categorias) {
            const linha = document.createElement('tr');
            const nome = document.createElement('th');
            const marca = document.createElement('span');
            marca.className = 'sim-zona';
            marca.style?.setProperty('--cor', c.cor);
            nome.append(marca, texto('span', c.nome));

            let criterio = livre ? 'Todas as circulações · primeiro cais que o algoritmo encontrar livre, sem zonas'
                : 'Categoria da circulação ou escolha manual';
            if (porPermanencia) {
                criterio = c.ateMinutos != null ? `Paragens até ${c.ateMinutos} min`
                    : anterior != null ? `Paragens com mais de ${anterior} min` : 'Restantes paragens';
                if (c.ateMinutos != null) anterior = Math.max(anterior ?? 0, c.ateMinutos);
                if (c.preferencia === 'Saida') criterio += ' · junto à saída';
                if (c.preferencia === 'Fundo') criterio += ' · no fundo do canal';
            }

            const reserva = numerosCais.filter(n => e.cais[n] !== c.id && SimulacaoPlaneador.categoriaDe(e, e.cais[n])?.reserva);
            let cheia = reserva.length ? `cais de reserva ${formatarCais(reserva)}` : '—';
            if (e.ultimoRecurso) cheia += reserva.length ? '; depois, qualquer cais livre' : ' qualquer cais livre';
            linha.append(nome, texto('td', formatarCais(caisDe(c.id))), texto('td', criterio), texto('td', cheia.replace(/^— /, '')));
            corpo.append(linha);
        }

        el('sim-legenda-titulo').textContent = `Atribuição de cais ${nomesModo[config.modo]}`;
        el('sim-map-legend').textContent = e.categorias.map(c => `${c.nome} ${formatarCais(caisDe(c.id))}`).join(' · ');

        const preencher = (id, opcoes) => {
            const select = el(id);
            // Na primeira vez fica a primeira opção; depois mantém-se a escolha, se ainda existir.
            const atual = select.dataset.preenchido ? select.value : null;
            select.dataset.preenchido = '1';
            select.replaceChildren(...opcoes.map(([valor, nome]) => opcao(valor, nome)));
            select.value = opcoes.some(([valor]) => valor === atual) ? atual : opcoes[0]?.[0] ?? '';
        };
        const categorias = e.categorias.map(c => [c.id, c.nome]);
        preencher('sim-dispatch-zone', porPermanencia ? [['*', 'Automática (pela permanência)'], ...categorias] : categorias);
        const automatica = porPermanencia ? 'Automática (pela permanência)' : livre ? 'Automática (todas em Livre)' : 'Categoria da circulação';
        preencher('sim-bulk-zone', [['*', automatica], ['', 'Sem zona'], ...categorias]);
    }

    /** Painel de gestão: categorias do esquema em uso e tipologia de cada cais. */
    function renderConfig() {
        const e = esquema();
        const porPermanencia = e.criterio === 'Permanencia';
        // No zonamento livre só se muda o nome e a cor: mais categorias ou tipologias deixariam de ser "livre".
        const livre = e.criterio === 'Livre';
        el('sim-config-add').hidden = livre;
        el('sim-config-ultimo-campo').hidden = livre;
        el('sim-config-cais-bloco').hidden = livre;
        const campo = (rotuloCampo, controlo) => {
            const label = texto('label', rotuloCampo);
            label.append(controlo);
            return label;
        };
        const entrada = (tipo, valor, aoMudar) => {
            const input = document.createElement('input');
            input.type = tipo;
            if (tipo === 'checkbox') input.checked = !!valor; else input.value = valor ?? '';
            input.addEventListener('change', () => { aoMudar(input); aplicarConfig(); });
            return input;
        };

        const lista = el('sim-config-categorias');
        lista.replaceChildren();
        for (const c of e.categorias) {
            const linha = document.createElement('div');
            linha.className = 'sim-config-linha';
            linha.append(
                campo('Cor', entrada('color', c.cor, i => { c.cor = i.value; })),
                campo('Nome', entrada('text', c.nome, i => { c.nome = i.value.trim() || c.nome; })));
            if (porPermanencia) {
                const ate = entrada('number', c.ateMinutos, i => {
                    c.ateMinutos = i.value === '' || !Number.isFinite(Number(i.value)) ? null : Math.max(0, Number(i.value));
                });
                ate.min = '0';
                ate.placeholder = 'sem limite';
                linha.append(campo('Paragens até (min)', ate));
            }
            if (livre) {
                linha.append(texto('span', `Cais: ${formatarCais(caisDe(c.id))}`, 'sim-config-cais-lista'));
                lista.append(linha);
                continue;
            }
            const preferencia = document.createElement('select');
            preferencia.append(opcao('Nenhuma', 'Indiferente'), opcao('Saida', 'Junto à saída'), opcao('Fundo', 'No fundo do canal'));
            preferencia.value = c.preferencia || 'Nenhuma';
            preferencia.addEventListener('change', () => { c.preferencia = preferencia.value; aplicarConfig(); });
            const remover = texto('button', 'Remover');
            remover.type = 'button';
            remover.disabled = e.categorias.length <= 1;
            remover.addEventListener('click', () => {
                e.categorias = e.categorias.filter(outra => outra !== c);
                for (const n of numerosCais) if (e.cais[n] === c.id) delete e.cais[n];
                aplicarConfig();
            });
            linha.append(
                campo('Posição preferida', preferencia),
                campo('Reserva das outras', entrada('checkbox', c.reserva, i => { c.reserva = i.checked; })),
                texto('span', `Cais: ${formatarCais(caisDe(c.id))}`, 'sim-config-cais-lista'),
                remover);
            lista.append(linha);
        }

        const grelha = el('sim-config-cais');
        grelha.replaceChildren();
        for (const n of numerosCais) {
            const select = document.createElement('select');
            select.append(opcao('', 'Sem categoria'), ...e.categorias.map(c => opcao(c.id, c.nome)));
            select.value = categoria(e.cais[n]) ? e.cais[n] : '';
            select.style?.setProperty('border-color', cor(e.cais[n]));
            select.addEventListener('change', () => {
                if (select.value) e.cais[n] = select.value; else delete e.cais[n];
                aplicarConfig();
            });
            grelha.append(campo(`Cais ${n}`, select));
        }

        el('sim-config-ultimo').checked = !!e.ultimoRecurso;
        el('sim-config-titulo').textContent = `Gerir categorias e tipologias dos cais · zonamento ${nomesModo[config.modo]}`;
    }

    /** Depois de alterar o zonamento: guarda, redesenha e volta a planear o dia. */
    function aplicarConfig() {
        guardarConfig();
        renderConfig();
        renderLegenda();
        criarLista();
        assinaturaPainel = '';
        if (viagens.length) agendarPlaneamento();
        atualizar();
    }

    el('sim-mode').value = config.modo;
    el('sim-mode').addEventListener('change', () => {
        if (!Object.hasOwn(config.esquemas, el('sim-mode').value)) return;
        config.modo = el('sim-mode').value;
        viagens.forEach(v => { v.caisAtracado = null; });
        aplicarConfig();
    });
    el('sim-config-add').addEventListener('click', () => {
        const e = esquema();
        e.categorias.push({
            id: `cat-${Date.now().toString(36)}`, nome: `Nova categoria ${e.categorias.length + 1}`,
            cor: paleta[e.categorias.length % paleta.length], reserva: false, preferencia: 'Nenhuma', ateMinutos: null
        });
        aplicarConfig();
    });
    el('sim-config-ultimo').addEventListener('change', () => { esquema().ultimoRecurso = !!el('sim-config-ultimo').checked; aplicarConfig(); });
    el('sim-config-reset').addEventListener('click', () => {
        config.esquemas[config.modo] = SimulacaoPlaneador.esquemasPorOmissao(ordemSaida)[config.modo];
        viagens.forEach(v => { delete v.manual[config.modo]; });
        aplicarConfig();
    });
    renderConfig();
    renderLegenda();

    el('sim-load').addEventListener('click', carregar);
    el('sim-plan').addEventListener('click', () => planear());
    el('sim-cmp-run').addEventListener('click', () => comparar());
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
            if (zona === '*') delete v.manual[config.modo];
            else v.manual[config.modo] = zona;
            v.caisAtracado = null;
        });
        criarLista();
        planear();
    });

    carregar();
    requestAnimationFrame(frame);
})();
