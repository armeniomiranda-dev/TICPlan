// Mapa OpenLayers com as posições GPS dos autocarros FlixBus das viagens que passam no TIC.
// Dados: /api/flixbus/tic (cache do servidor, atualizada a cada 10 s). Clique num autocarro mostra o traçado (/api/flixbus/rota/{id}).
// Requer ol.js.
window.flixbusMapa = function (el, popup, opcoes) {
    const CORES = { ACaminho: '#0d6efd', NoTic: '#198754', Partiu: '#6c757d' };
    const SENTIDO = { ACaminho: 'A caminho do TIC', NoTic: 'No TIC', Partiu: 'Partiu do TIC' };
    const CHAVE = 'flixbusMapa.vista';
    const hhmm = s => s ? new Date(s).toLocaleTimeString('pt-PT', { hour: '2-digit', minute: '2-digit' }) : '–';
    const atraso = m => m == null ? '' : m === 0 ? ' (a horas)' : m > 0 ? ` (+${m} min)` : ` (${m} min)`;

    const estilos = {};
    function estilo(f) {
        const sentido = f.get('Sentido'), velho = f.get('Desatualizada'), rumo = f.get('Rumo');
        const chave = `${sentido}|${velho}|${rumo ?? 'x'}|${f.get('Linha')}`;
        if (estilos[chave]) return estilos[chave];
        const cor = CORES[sentido] ?? '#6c757d';
        const s = [new ol.style.Style({
            image: new ol.style.Circle({
                radius: 11,
                fill: new ol.style.Fill({ color: velho ? '#ffffff' : cor }),
                stroke: new ol.style.Stroke({ color: cor, width: 2.5, lineDash: velho ? [3, 3] : undefined })
            }),
            text: new ol.style.Text({
                text: f.get('Linha') ?? '', font: 'bold 9px system-ui, sans-serif',
                fill: new ol.style.Fill({ color: velho ? cor : '#fff' })
            }),
            zIndex: sentido === 'ACaminho' ? 3 : sentido === 'NoTic' ? 2 : 1
        })];
        if (rumo != null) {
            // Seta no sentido de marcha, fora do círculo.
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

    const fonteVeiculos = new ol.source.Vector();
    const fonteTic = new ol.source.Vector();
    const fonteRota = new ol.source.Vector();
    const mapa = new ol.Map({
        target: el,
        layers: [
            new ol.layer.Tile({ source: new ol.source.OSM() }),
            new ol.layer.Vector({ source: fonteRota, style: new ol.style.Style({ stroke: new ol.style.Stroke({ color: '#73d700', width: 4 }) }) }),
            new ol.layer.Vector({
                source: fonteTic,
                style: new ol.style.Style({
                    image: new ol.style.RegularShape({ points: 4, radius: 12, angle: Math.PI / 4, fill: new ol.style.Fill({ color: '#dc3545' }), stroke: new ol.style.Stroke({ color: '#fff', width: 2 }) }),
                    text: new ol.style.Text({ text: 'TIC', font: 'bold 9px system-ui, sans-serif', fill: new ol.style.Fill({ color: '#fff' }) })
                })
            }),
            new ol.layer.Vector({ source: fonteVeiculos, style: estilo })
        ],
        view: new ol.View({ center: ol.proj.fromLonLat([-8.583, 41.153]), zoom: 7 })
    });

    // Mantém zoom, centro e viagem selecionada entre recarregamentos da página.
    let vista = null, selecionada = null;
    try { vista = JSON.parse(sessionStorage.getItem(CHAVE)); } catch { }
    if (vista) { mapa.getView().setCenter(vista.c); mapa.getView().setZoom(vista.z); selecionada = vista.r ?? null; }
    const guardar = () => {
        try { sessionStorage.setItem(CHAVE, JSON.stringify({ c: mapa.getView().getCenter(), z: mapa.getView().getZoom(), r: selecionada })); } catch { }
    };
    mapa.on('moveend', guardar);

    function texto(f) {
        const p = f.getProperties();
        const linhas = [
            `<strong>${esc(p.Linha)}${p.Viagem != null ? ' #' + esc(p.Viagem) : ''}</strong> · ${SENTIDO[p.Sentido] ?? ''}`,
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
    const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));

    function mostrarPopup(f, pixel) {
        popup.replaceChildren(texto(f));
        popup.style.left = (pixel[0] + 14) + 'px';
        popup.style.top = (pixel[1] + 14) + 'px';
        popup.style.display = 'block';
    }

    mapa.on('pointermove', e => {
        if (e.dragging) return;
        const f = mapa.forEachFeatureAtPixel(e.pixel, f => f.get('RideUuid') ? f : null, { hitTolerance: 4 });
        el.style.cursor = f ? 'pointer' : '';
        if (f) mostrarPopup(f, e.pixel); else popup.style.display = 'none';
    });

    async function desenharRota(rideUuid) {
        fonteRota.clear();
        selecionada = rideUuid;
        guardar();
        if (!rideUuid) return;
        try {
            const r = await fetch(`/api/flixbus/rota/${encodeURIComponent(rideUuid)}`, { headers: { Accept: 'application/json' } });
            if (!r.ok) return;
            const fmt = new ol.format.Polyline({ factor: 1e5 });
            for (const p of await r.json())
                fonteRota.addFeature(fmt.readFeature(p, { dataProjection: 'EPSG:4326', featureProjection: 'EPSG:3857' }));
        } catch { /* traçado é opcional */ }
    }

    mapa.on('singleclick', e => {
        const f = mapa.forEachFeatureAtPixel(e.pixel, f => f.get('RideUuid') ? f : null, { hitTolerance: 4 });
        desenharRota(f && f.get('RideUuid') !== selecionada ? f.get('RideUuid') : null);
    });

    function desenhar(dados) {
        fonteVeiculos.clear();
        fonteTic.clear();
        if (dados.Estacao)
            fonteTic.addFeature(new ol.Feature({ geometry: new ol.geom.Point(ol.proj.fromLonLat([dados.Estacao.Longitude, dados.Estacao.Latitude])) }));
        for (const p of dados.Posicoes ?? []) {
            const f = new ol.Feature({ ...p, geometry: new ol.geom.Point(ol.proj.fromLonLat([p.Longitude, p.Latitude])) });
            fonteVeiculos.addFeature(f);
        }
        const n = s => (dados.Posicoes ?? []).filter(p => p.Sentido === s).length;
        opcoes?.onContagem?.({ ACaminho: n('ACaminho'), NoTic: n('NoTic'), Partiu: n('Partiu'), Total: (dados.Posicoes ?? []).length });

        if (!vista && fonteVeiculos.getFeatures().length) {
            const ext = fonteVeiculos.getExtent().slice();
            ol.extent.extend(ext, fonteTic.getExtent());
            mapa.getView().fit(ext, { padding: [40, 40, 40, 40], maxZoom: 12 });
            vista = true;
        }
    }

    async function atualizar() {
        try {
            const r = await fetch('/api/flixbus/tic', { headers: { Accept: 'application/json' } });
            if (r.ok) desenhar(await r.json());
        } catch { /* mantém o último estado */ }
    }

    atualizar();
    if (selecionada) desenharRota(selecionada);
    setInterval(atualizar, opcoes?.intervaloMs ?? 10000);
    return mapa;
};
