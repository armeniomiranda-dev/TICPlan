// Mapa OpenLayers (tempo real em Campanhã) com as posições GPS de:
//  - autocarros FlixBus das viagens que passam no TIC (círculos)  -> /api/flixbus/tic; ao selecionar mostra o traçado;
//  - autocarros STCP das viagens que param no TIC (quadrados)      -> /api/stcp/tic;
//  - comboios CP com paragem em Porto-Campanhã (hexágonos)        -> /api/cp/tic;
//  - rasto dos últimos 10 minutos de cada veículo                 -> /api/tempo-real/rastos.
// Marca também o TIC, as paragens STCP do TIC e as estações CP e Metro (a Metro não tem posições em tempo real).
// Cada veículo tem uma chave igual à da tabela ("flixbus:{ride}", "stcp:{veículo}", "cp:{nº}"): selecionar isola-o no mapa.
// Requer ol.js.
window.tempoRealMapa = function (el, popup, opcoes) {
    const CORES = { ACaminho: '#0d6efd', NoTic: '#198754', Partiu: '#6c757d' };
    const RGB = { ACaminho: '13,110,253', NoTic: '25,135,84', Partiu: '108,117,125' };
    const SENTIDO = { ACaminho: 'A caminho de Campanhã', NoTic: 'Em Campanhã', Partiu: 'Já passou em Campanhã' };
    const CHAVE = 'tempoRealMapa.vista';
    const hhmm = s => s ? new Date(s).toLocaleTimeString('pt-PT', { hour: '2-digit', minute: '2-digit' }) : '–';
    const atraso = m => m == null ? '' : m === 0 ? ' (a horas)' : m > 0 ? ` (+${m} min)` : ` (${m} min)`;
    const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    const operadorDe = chave => chave?.split(':')[0];

    let filtro = null;           // chave do veículo isolado (ou null)
    let rastosVisiveis = true;

    // ---- Estilos ----
    const estilos = {};
    function estiloBase(f) {
        const fonte = f.get('Fonte'), sentido = f.get('Sentido'), velho = f.get('Desatualizada'), rumo = f.get('Rumo');
        const chave = `${fonte}|${sentido}|${velho}|${rumo ?? 'x'}|${f.get('Linha')}`;
        if (estilos[chave]) return estilos[chave];
        const cor = CORES[sentido] ?? '#6c757d';
        const fill = new ol.style.Fill({ color: velho ? '#ffffff' : cor });
        const stroke = new ol.style.Stroke({ color: cor, width: 2.5, lineDash: velho ? [3, 3] : undefined });
        const image = fonte === 'stcp' ? new ol.style.RegularShape({ points: 4, radius: 13, angle: Math.PI / 4, fill, stroke })
            : fonte === 'cp' ? new ol.style.RegularShape({ points: 6, radius: 13, fill, stroke })
            : new ol.style.Circle({ radius: 11, fill, stroke });
        const s = [new ol.style.Style({
            image,
            text: new ol.style.Text({
                text: f.get('Linha') ?? '', font: 'bold 9px system-ui, sans-serif',
                fill: new ol.style.Fill({ color: velho ? cor : '#fff' })
            }),
            zIndex: sentido === 'ACaminho' ? 3 : sentido === 'NoTic' ? 2 : 1
        })];
        if (rumo != null) {
            // Seta no sentido de marcha, fora do símbolo.
            s.push(new ol.style.Style({
                geometry: feat => {
                    const c = feat.getGeometry().getCoordinates(), res = mapa.getView().getResolution() * 15, a = rumo * Math.PI / 180;
                    return new ol.geom.Point([c[0] + Math.sin(a) * res, c[1] + Math.cos(a) * res]);
                },
                image: new ol.style.RegularShape({
                    points: 3, radius: 5, rotation: rumo * Math.PI / 180, rotateWithView: true,
                    fill: new ol.style.Fill({ color: cor })
                })
            }));
        }
        return estilos[chave] = s;
    }
    const halo = new ol.style.Style({
        image: new ol.style.Circle({ radius: 19, fill: new ol.style.Fill({ color: 'rgba(255,193,7,.25)' }), stroke: new ol.style.Stroke({ color: '#ffc107', width: 3 }) }),
        zIndex: 0
    });
    const estiloVeiculo = f => {
        if (filtro && f.get('Chave') !== filtro) return null;
        return filtro ? [halo, ...estiloBase(f)] : estiloBase(f);
    };

    const estiloRasto = f => {
        const chave = f.get('Chave');
        if (!rastosVisiveis || (filtro && chave !== filtro) || !camadas[operadorDe(chave)]?.getVisible()) return null;
        const rgb = RGB[sentidoAtual.get(chave)] ?? '108,117,125';
        return new ol.style.Style({
            stroke: new ol.style.Stroke({ color: `rgba(${rgb},${filtro ? .9 : .45})`, width: filtro ? 5 : 3, lineCap: 'round', lineJoin: 'round' })
        });
    };

    const estiloTic = new ol.style.Style({
        image: new ol.style.RegularShape({ points: 4, radius: 12, angle: Math.PI / 4, fill: new ol.style.Fill({ color: '#dc3545' }), stroke: new ol.style.Stroke({ color: '#fff', width: 2 }) }),
        text: new ol.style.Text({ text: 'TIC', font: 'bold 9px system-ui, sans-serif', fill: new ol.style.Fill({ color: '#fff' }) })
    });
    const estiloParagem = f => new ol.style.Style({
        image: new ol.style.Circle({ radius: 5, fill: new ol.style.Fill({ color: '#fff' }), stroke: new ol.style.Stroke({ color: '#dc3545', width: 2 }) }),
        text: new ol.style.Text({
            text: f.get('Id'), font: '10px system-ui, sans-serif', offsetY: -12,
            fill: new ol.style.Fill({ color: '#dc3545' }), stroke: new ol.style.Stroke({ color: '#fff', width: 3 })
        })
    });
    const estiloEstacao = f => new ol.style.Style({
        image: new ol.style.Circle({ radius: 9, fill: new ol.style.Fill({ color: f.get('Cor') }), stroke: new ol.style.Stroke({ color: '#fff', width: 2 }) }),
        text: new ol.style.Text({ text: f.get('Rotulo'), font: 'bold 9px system-ui, sans-serif', fill: new ol.style.Fill({ color: '#fff' }) })
    });

    // ---- Camadas ----
    const fonteFlix = new ol.source.Vector(), fonteStcp = new ol.source.Vector(), fonteCp = new ol.source.Vector();
    const fonteTic = new ol.source.Vector(), fonteParagens = new ol.source.Vector(), fonteRota = new ol.source.Vector();
    const fonteEstacoes = new ol.source.Vector(), fonteRastos = new ol.source.Vector();
    const camadas = {
        flixbus: new ol.layer.Vector({ source: fonteFlix, style: estiloVeiculo }),
        stcp: new ol.layer.Vector({ source: fonteStcp, style: estiloVeiculo }),
        cp: new ol.layer.Vector({ source: fonteCp, style: estiloVeiculo })
    };
    const camadaRastos = new ol.layer.Vector({ source: fonteRastos, style: estiloRasto });
    const mapa = new ol.Map({
        target: el,
        layers: [
            new ol.layer.Tile({ source: new ol.source.OSM() }),
            new ol.layer.Vector({ source: fonteRota, style: new ol.style.Style({ stroke: new ol.style.Stroke({ color: '#73d700', width: 4 }) }) }),
            camadaRastos,
            new ol.layer.Vector({ source: fonteParagens, style: estiloParagem, minZoom: 14 }),
            new ol.layer.Vector({ source: fonteEstacoes, style: estiloEstacao, minZoom: 12 }),
            new ol.layer.Vector({ source: fonteTic, style: estiloTic }),
            camadas.cp, camadas.flixbus, camadas.stcp
        ],
        view: new ol.View({ center: ol.proj.fromLonLat([-8.583, 41.153]), zoom: 7 })
    });

    // Mantém zoom, centro, veículo isolado e camadas visíveis entre recarregamentos da página.
    let vista = null;
    try { vista = JSON.parse(sessionStorage.getItem(CHAVE)); } catch { }
    if (vista) {
        mapa.getView().setCenter(vista.c); mapa.getView().setZoom(vista.z);
        filtro = vista.f ?? null;
        rastosVisiveis = vista.rastos !== false;
        for (const [op, camada] of Object.entries(camadas)) camada.setVisible(vista.ocultas?.includes(op) !== true);
    }
    const guardar = () => {
        try {
            sessionStorage.setItem(CHAVE, JSON.stringify({
                c: mapa.getView().getCenter(), z: mapa.getView().getZoom(), f: filtro, rastos: rastosVisiveis,
                ocultas: Object.entries(camadas).filter(([, c]) => !c.getVisible()).map(([op]) => op)
            }));
        } catch { }
    };
    mapa.on('moveend', guardar);

    // ---- Popup ----
    function texto(f) {
        const p = f.getProperties();
        const linhas = p.Fonte === 'cp'
            ? [
                `<strong>CP ${esc(p.ServicoNome ?? p.Servico)} ${esc(p.Numero)}</strong> · ${SENTIDO[p.Sentido] ?? ''}`,
                `${esc(p.Origem)} → ${esc(p.Destino)}`,
                p.ChegadaPrevista ? `Chegada: ${hhmm(p.ChegadaEstimada ?? p.ChegadaPrevista)}${atraso(p.AtrasoMinutos)}` : '',
                p.PartidaPrevista ? `Partida: ${hhmm(p.PartidaEstimada ?? p.PartidaPrevista)}${p.Plataforma ? ' · linha ' + esc(p.Plataforma) : ''}` : '',
                p.Suprimido ? '<span class="text-danger fw-semibold">Suprimido</span>' : '',
                `GPS ${hhmm(p.AtualizadoEm)}${p.EstadoGps ? ' · ' + esc(p.EstadoGps.toLowerCase().replace('_', ' ')) : ''}`
            ]
            : p.Fonte === 'stcp'
            ? [
                `<strong>STCP ${esc(p.Linha)}</strong>${p.Destino ? ' → ' + esc(p.Destino) : ''} · ${SENTIDO[p.Sentido] ?? ''}`,
                `${p.Sentido === 'Partiu' ? 'Passou' : 'Passa'} em ${esc(p.ParagemTic)}${p.ParagemTicNome ? ' (' + esc(p.ParagemTicNome) + ')' : ''}: ${hhmm(p.HoraTicEstimada)}${atraso(p.AtrasoMinutos)} · previsto ${hhmm(p.HoraTicPrevista)}`,
                p.ParagemProxima ? `Junto a: ${esc(p.ParagemProxima)} · ${Math.round(p.DistanciaTicMetros)} m do TIC` : '',
                `Veículo ${esc(p.Veiculo)} · GPS ${hhmm(p.AtualizadoEm)}${p.Velocidade != null ? ' · ' + Math.round(p.Velocidade) + ' km/h' : ''}`
            ]
            : [
                `<strong>FlixBus ${esc(p.Linha)}${p.Viagem != null ? ' #' + esc(p.Viagem) : ''}</strong> · ${SENTIDO[p.Sentido] ?? ''}`,
                esc(p.Percurso),
                p.Sentido === 'ACaminho' ? `Chegada ao TIC: ${hhmm(p.ChegadaTic)}${atraso(p.AtrasoMinutos)}` : '',
                p.Sentido !== 'Partiu' && p.PartidaTic ? `Partida do TIC: ${hhmm(p.PartidaTic)}${p.Sentido === 'NoTic' ? atraso(p.AtrasoMinutos) : ''}` : '',
                p.Sentido === 'Partiu' ? `Saiu do TIC: ${hhmm(p.PartidaTic)}${atraso(p.AtrasoMinutos)}` : '',
                `GPS: ${hhmm(p.AtualizadoEm)}${p.Desatualizada ? ' (desatualizado)' : ''}${p.Velocidade ? ' · ' + esc(p.Velocidade.toLowerCase()) : ''}`
            ];
        const div = document.createElement('div');
        div.innerHTML = linhas.filter(Boolean).map(l => `<div>${l}</div>`).join('');
        return div;
    }

    const veiculoEm = pixel => mapa.forEachFeatureAtPixel(pixel, f => f.get('Chave') && f.get('Fonte') ? f : null,
        { hitTolerance: 4, layerFilter: l => Object.values(camadas).includes(l) });

    mapa.on('pointermove', e => {
        if (e.dragging) return;
        const f = veiculoEm(e.pixel);
        el.style.cursor = f ? 'pointer' : '';
        if (!f) { popup.style.display = 'none'; return; }
        popup.replaceChildren(texto(f));
        popup.style.left = (e.pixel[0] + 14) + 'px';
        popup.style.top = (e.pixel[1] + 14) + 'px';
        popup.style.display = 'block';
    });

    // ---- Traçado (só FlixBus) ----
    async function desenharRota(rideUuid) {
        fonteRota.clear();
        if (!rideUuid) return;
        try {
            const r = await fetch(`/api/flixbus/rota/${encodeURIComponent(rideUuid)}`, { headers: { Accept: 'application/json' } });
            if (!r.ok || filtro !== 'flixbus:' + rideUuid) return;
            const fmt = new ol.format.Polyline({ factor: 1e5 });
            for (const p of await r.json())
                fonteRota.addFeature(fmt.readFeature(p, { dataProjection: 'EPSG:4326', featureProjection: 'EPSG:3857' }));
        } catch { /* traçado é opcional */ }
    }

    const todasAsFontes = () => [fonteFlix, fonteStcp, fonteCp];
    const procurar = chave => todasAsFontes().flatMap(s => s.getFeatures()).find(f => f.get('Chave') === chave) ?? null;

    function redesenhar() {
        Object.values(camadas).forEach(c => c.changed());
        camadaRastos.changed();
    }

    /**
     * Isola um veículo no mapa (ou mostra todos, com null). Devolve { encontrado, semPosicao } para a página avisar.
     * centrar: true quando vem da tabela (desloca o mapa até ao veículo ou à estação).
     */
    function selecionar(chave, centrar) {
        filtro = chave || null;
        popup.style.display = 'none';
        const op = operadorDe(filtro);
        desenharRota(op === 'flixbus' ? filtro.slice('flixbus:'.length) : null);
        if (op && camadas[op] && !camadas[op].getVisible()) {
            camadas[op].setVisible(true);
            opcoes?.onCamada?.(op, true);
        }
        redesenhar();
        guardar();
        if (!filtro) return { encontrado: true };

        const f = procurar(filtro);
        if (centrar) {
            const view = mapa.getView();
            const alvo = f?.getGeometry().getCoordinates()
                ?? (op === 'metro' ? estacao('M') : op === 'cp' ? estacao('CP') : fonteTic.getFeatures()[0]?.getGeometry().getCoordinates());
            if (alvo) {
                const rastro = fonteRastos.getFeatures().find(r => r.get('Chave') === filtro);
                if (f && rastro) {
                    const ext = rastro.getGeometry().getExtent().slice();
                    ol.extent.extend(ext, f.getGeometry().getExtent());
                    view.fit(ext, { padding: [60, 60, 60, 60], maxZoom: 15, duration: 400 });
                } else {
                    view.animate({ center: alvo, zoom: Math.max(view.getZoom(), op === 'flixbus' || op === 'cp' ? 10 : 14), duration: 400 });
                }
            }
        }
        return { encontrado: !!f, semPosicao: !f };
    }
    const estacao = rotulo => fonteEstacoes.getFeatures().find(e => e.get('Rotulo') === rotulo)?.getGeometry().getCoordinates();

    mapa.on('singleclick', e => {
        const f = veiculoEm(e.pixel);
        if (!f) return;
        const chave = f.get('Chave') === filtro ? null : f.get('Chave');
        selecionar(chave, false);
        opcoes?.onSelecionar?.(chave);
    });

    // ---- Dados ----
    const ultimo = { flixbus: [], stcp: [], cp: [] };
    const sentidoAtual = new Map();
    function contar() {
        const todos = Object.entries(ultimo).filter(([op]) => camadas[op].getVisible()).flatMap(([, l]) => l);
        const n = s => todos.filter(p => p.Sentido === s).length;
        opcoes?.onContagem?.({
            ACaminho: n('ACaminho'), NoTic: n('NoTic'), Partiu: n('Partiu'),
            FlixBus: ultimo.flixbus.length, Stcp: ultimo.stcp.length, Cp: ultimo.cp.length
        });
    }

    function ponto(lon, lat) { return new ol.geom.Point(ol.proj.fromLonLat([lon, lat])); }

    function desenharFlix(dados) {
        fonteFlix.clear();
        fonteTic.clear();
        if (dados.Estacao) fonteTic.addFeature(new ol.Feature({ geometry: ponto(dados.Estacao.Longitude, dados.Estacao.Latitude) }));
        ultimo.flixbus = dados.Posicoes ?? [];
        for (const p of ultimo.flixbus)
            fonteFlix.addFeature(new ol.Feature({ ...p, Fonte: 'flixbus', Chave: 'flixbus:' + p.RideUuid, geometry: ponto(p.Longitude, p.Latitude) }));
    }

    function desenharStcp(dados) {
        fonteStcp.clear();
        if (!fonteParagens.getFeatures().length)
            for (const p of dados.ParagensTic ?? [])
                fonteParagens.addFeature(new ol.Feature({ Id: p.Id, geometry: ponto(p.Longitude, p.Latitude) }));
        ultimo.stcp = dados.Veiculos ?? [];
        for (const p of ultimo.stcp)
            fonteStcp.addFeature(new ol.Feature({ ...p, Fonte: 'stcp', Chave: 'stcp:' + p.Veiculo, geometry: ponto(p.Longitude, p.Latitude) }));
    }

    function desenharCp(dados) {
        fonteCp.clear();
        ultimo.cp = (dados.Comboios ?? []).filter(c => c.Latitude != null && c.Longitude != null);
        for (const c of ultimo.cp)
            fonteCp.addFeature(new ol.Feature({
                ...c, Fonte: 'cp', Chave: 'cp:' + c.Numero, Linha: c.Servico, Rumo: c.Rumo ?? null, Desatualizada: false,
                geometry: ponto(c.Longitude, c.Latitude)
            }));
    }

    function desenharEstacoes(cp, metro) {
        fonteEstacoes.clear();
        if (cp?.Latitude) fonteEstacoes.addFeature(new ol.Feature({ Rotulo: 'CP', Cor: '#1b7a3a', geometry: ponto(cp.Longitude, cp.Latitude) }));
        if (metro?.Latitude) fonteEstacoes.addFeature(new ol.Feature({ Rotulo: 'M', Cor: '#0b3d91', geometry: ponto(metro.Longitude, metro.Latitude) }));
    }

    function desenharRastos(rastos) {
        fonteRastos.clear();
        for (const [chave, pontos] of Object.entries(rastos ?? {})) {
            if (pontos.length < 2) continue;
            fonteRastos.addFeature(new ol.Feature({
                Chave: chave,
                geometry: new ol.geom.LineString(pontos.map(p => ol.proj.fromLonLat([p[0], p[1]])))
            }));
        }
    }

    async function obter(url) {
        try {
            const r = await fetch(url, { headers: { Accept: 'application/json' } });
            return r.ok ? await r.json() : null;
        } catch { return null; }
    }

    async function atualizar() {
        const [flix, stcp, cp, metro, rastos] = await Promise.all(
            ['/api/flixbus/tic', '/api/stcp/tic', '/api/cp/tic', '/api/metro/tic', '/api/tempo-real/rastos'].map(obter));
        if (flix) desenharFlix(flix);
        if (stcp) desenharStcp(stcp);
        if (cp) desenharCp(cp);
        if (cp || metro) desenharEstacoes(cp, metro);
        sentidoAtual.clear();
        for (const f of todasAsFontes().flatMap(s => s.getFeatures())) sentidoAtual.set(f.get('Chave'), f.get('Sentido'));
        if (rastos) desenharRastos(rastos);
        contar();
        if (!vista && (fonteFlix.getFeatures().length || fonteStcp.getFeatures().length)) {
            // Primeira vez: enquadra Campanhã e os autocarros STCP (os FlixBus/CP estão espalhados pelo país).
            const ext = ol.extent.createEmpty();
            for (const f of [fonteStcp, fonteTic]) if (f.getFeatures().length) ol.extent.extend(ext, f.getExtent());
            if (!ol.extent.isEmpty(ext)) mapa.getView().fit(ext, { padding: [40, 40, 40, 40], maxZoom: 14 });
            vista = true;
        }
    }

    atualizar().then(() => { if (filtro) desenharRota(operadorDe(filtro) === 'flixbus' ? filtro.slice(8) : null); });
    setInterval(atualizar, opcoes?.intervaloMs ?? 10000);

    return {
        mapa,
        selecionar,
        get filtro() { return filtro; },
        mostrar(operador, visivel) {
            camadas[operador]?.setVisible(visivel);
            camadaRastos.changed();
            if (!visivel) popup.style.display = 'none';
            guardar();
            contar();
        },
        visivel: operador => camadas[operador]?.getVisible() ?? false,
        mostrarRastos(visivel) { rastosVisiveis = visivel; camadaRastos.changed(); guardar(); },
        get rastosVisiveis() { return rastosVisiveis; }
    };
};
