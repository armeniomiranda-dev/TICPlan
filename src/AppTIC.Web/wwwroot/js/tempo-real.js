// Página Tempo real: liga a tabela única de passagens por Campanhã ao mapa (tempo-real-mapa.js).
//  - filtro por tipo de serviço (autocarro / comboio / metro), guardado na sessão do browser;
//  - clique numa linha isola o veículo no mapa (e mostra o rasto de 10 min); clique num veículo no mapa marca a linha;
//  - a cada 10 s troca a tabela pela versão nova do servidor sem recarregar (o mapa mantém zoom e seleção).
(function () {
    const INTERVALO = 10000;
    const CHAVE_TIPO = 'tempoReal.tipo';
    const $ = id => document.getElementById(id);
    const ler = k => { try { return sessionStorage.getItem(k); } catch { return null; } };
    const gravar = (k, v) => { try { sessionStorage.setItem(k, v); } catch { } };

    let tipo = ler(CHAVE_TIPO) || 'todos';

    const mapa = tempoRealMapa($('tempo-real-mapa'), $('tempo-real-popup'), {
        intervaloMs: INTERVALO,
        onContagem: n => ['ACaminho', 'NoTic', 'Partiu', 'FlixBus', 'Stcp', 'Cp']
            .forEach(s => { const e = $('n-' + s); if (e) e.textContent = `(${n[s]})`; }),
        onSelecionar: chave => { marcarLinha(chave, true); mostrarSelecao(chave); },
        onCamada: (op, visivel) => { const cb = $('mostrar-' + op); if (cb) cb.checked = visivel; }
    });

    // ---- Camadas e rasto ----
    for (const op of ['flixbus', 'stcp', 'cp']) {
        const cb = $('mostrar-' + op);
        cb.checked = mapa.visivel(op);
        cb.addEventListener('change', () => mapa.mostrar(op, cb.checked));
    }
    const cbRastos = $('mostrar-rastos');
    cbRastos.checked = mapa.rastosVisiveis;
    cbRastos.addEventListener('change', () => mapa.mostrarRastos(cbRastos.checked));

    // ---- Filtro por tipo de serviço ----
    function aplicarTipo() {
        const corpo = $('tempo-real-tabelas');
        for (const tr of corpo.querySelectorAll('tr.linha-passagem'))
            tr.classList.toggle('d-none', tipo !== 'todos' && tr.dataset.tipo !== tipo);
        for (const b of document.querySelectorAll('#filtro-tipo [data-tipo]')) {
            b.classList.toggle('active', b.dataset.tipo === tipo);
            b.setAttribute('aria-pressed', b.dataset.tipo === tipo);
        }
        const contagens = Object.fromEntries((corpo.dataset.contagens || '').split(',').filter(Boolean).map(x => x.split(':')));
        for (const e of document.querySelectorAll('[data-contagem]'))
            e.textContent = e.dataset.contagem === 'todos' ? (corpo.dataset.total ?? '') : (contagens[e.dataset.contagem] ?? '0');
    }
    document.querySelectorAll('#filtro-tipo [data-tipo]').forEach(b => b.addEventListener('click', () => {
        tipo = b.dataset.tipo;
        gravar(CHAVE_TIPO, tipo);
        aplicarTipo();
    }));

    // ---- Seleção (tabela <-> mapa) ----
    let descricao = null;   // texto do veículo selecionado (guardado ao clicar, a linha pode sair da tabela)

    function linhaDe(chave) {
        return chave ? $('tempo-real-tabelas').querySelector(`tr[data-chave="${CSS.escape(chave)}"]`) : null;
    }

    function descrever(tr) {
        if (!tr) return null;
        const c = tr.cells;
        return `${c[3].textContent.trim()} ${c[4].textContent.trim()} · ${c[5].textContent.trim()} · ${c[6].textContent.trim().replace(/\s+/g, ' ')} · ${c[1].textContent.trim()}`;
    }

    function marcarLinha(chave, rolar) {
        for (const tr of $('tempo-real-tabelas').querySelectorAll('tr.selecionada')) tr.classList.remove('selecionada');
        const tr = linhaDe(chave);
        if (!tr) return;
        tr.classList.add('selecionada');
        descricao = descrever(tr);
        if (rolar) {
            // Rola só a tabela (não a página) até à linha.
            const caixa = tr.closest('.tabela-tempo-real');
            const topo = tr.offsetTop - caixa.querySelector('thead').offsetHeight;
            if (topo < caixa.scrollTop || tr.offsetTop + tr.offsetHeight > caixa.scrollTop + caixa.clientHeight)
                caixa.scrollTo({ top: Math.max(0, topo - caixa.clientHeight / 3), behavior: 'smooth' });
        }
    }

    function mostrarSelecao(chave, semPosicao) {
        const caixa = $('selecao-mapa');
        caixa.classList.toggle('d-none', !chave);
        if (!chave) { descricao = null; return; }
        $('selecao-texto').textContent = (descricao ?? chave) +
            (semPosicao ? ' — sem posição GPS, a mostrar a estação' : '');
    }

    function selecionar(chave) {
        marcarLinha(chave, false);
        const r = mapa.selecionar(chave, true);
        mostrarSelecao(chave, chave && r.semPosicao);
    }

    $('tempo-real-tabelas').closest('.tabela-tempo-real').addEventListener('click', e => {
        const tr = e.target.closest('tr.linha-passagem');
        if (!tr) return;
        selecionar(tr.dataset.chave === mapa.filtro ? null : tr.dataset.chave);
    });
    $('selecao-limpar').addEventListener('click', () => selecionar(null));

    // ---- Atualização da tabela a cada 10 s ----
    setInterval(async () => {
        try {
            const r = await fetch(location.href, { headers: { Accept: 'text/html' } });
            if (!r.ok || r.redirected) return; // sessão expirada: mantém o que está
            const doc = new DOMParser().parseFromString(await r.text(), 'text/html');
            for (const id of ['tempo-real-hora', 'tempo-real-erro', 'tempo-real-tabelas']) {
                const novo = doc.getElementById(id), atual = $(id);
                if (novo && atual) atual.replaceWith(novo);
            }
            aplicarTipo();
            marcarLinha(mapa.filtro, false);
        } catch { /* mantém o último estado */ }
    }, INTERVALO);

    aplicarTipo();
    if (mapa.filtro) {
        marcarLinha(mapa.filtro, true);
        mostrarSelecao(mapa.filtro);
    }
})();
