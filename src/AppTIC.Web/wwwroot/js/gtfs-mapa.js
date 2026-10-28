// Mapa OpenLayers com paragens e traçados de feeds GTFS (GeoJSON de GtfsFeeds/Mapa). Requer ol.js.
window.gtfsMapa = function (el, popup) {
    const stopNormal = new ol.style.Style({
        image: new ol.style.Circle({ radius: 3.5, fill: new ol.style.Fill({ color: '#fff' }), stroke: new ol.style.Stroke({ color: '#495057', width: 1.5 }) })
    });
    const stopTic = new ol.style.Style({
        image: new ol.style.Circle({ radius: 7, fill: new ol.style.Fill({ color: '#dc3545' }), stroke: new ol.style.Stroke({ color: '#fff', width: 2 }) }),
        zIndex: 10
    });
    const shapeStyles = {};
    const shapeStyle = cor => shapeStyles[cor] ??= new ol.style.Style({ stroke: new ol.style.Stroke({ color: cor, width: 3 }) });

    const map = new ol.Map({
        target: el,
        layers: [new ol.layer.Tile({ source: new ol.source.OSM() })],
        view: new ol.View({ center: ol.proj.fromLonLat([-8.61, 41.15]), zoom: 11 })
    });
    const format = new ol.format.GeoJSON({ featureProjection: 'EPSG:3857' });
    const camadas = [];

    map.on('pointermove', e => {
        if (e.dragging) return;
        const f = map.forEachFeatureAtPixel(e.pixel, f => f, { hitTolerance: 4 });
        if (!f) { popup.style.display = 'none'; el.style.cursor = ''; return; }
        el.style.cursor = 'pointer';
        const op = f.get('operador') ? f.get('operador') + ' · ' : '';
        popup.textContent = f.get('kind') === 'stop'
            ? `${op}${f.get('stop_id')} – ${f.get('nome')}${f.get('tic') ? ' (TIC)' : ''}`
            : `${op}Traçado ${f.get('shape_id')}${f.get('rotas') ? ' · ' + f.get('rotas') : ''}`;
        popup.style.left = (e.pixel[0] + 12) + 'px';
        popup.style.top = (e.pixel[1] + 12) + 'px';
        popup.style.display = 'block';
    });
    el.addEventListener('mouseleave', () => popup.style.display = 'none');

    return {
        map,

        /// Par de camadas (traçados + paragens). cor: cor fixa dos traçados (senão route_color do GTFS); operador: nome no popup.
        camada: function (cor, operador) {
            const shapes = new ol.layer.Vector({ source: new ol.source.Vector(), style: f => shapeStyle(cor || f.get('cor') || '#0d6efd') });
            const stops = new ol.layer.Vector({ source: new ol.source.Vector(), style: f => f.get('tic') ? stopTic : stopNormal, zIndex: 1 });
            map.addLayer(shapes);
            map.addLayer(stops);
            const c = {
                shapes, stops, visivel: true,
                carregar: async function (url) {
                    shapes.getSource().clear();
                    stops.getSource().clear();
                    const resp = await fetch(url);
                    if (!resp.ok) throw new Error(resp.status + ' ' + resp.statusText);
                    const features = format.readFeatures(await resp.json());
                    if (operador) features.forEach(f => f.set('operador', operador));
                    const s = features.filter(f => f.get('kind') === 'shape');
                    const p = features.filter(f => f.get('kind') === 'stop');
                    shapes.getSource().addFeatures(s);
                    stops.getSource().addFeatures(p);
                    return { paragens: p.length, tracados: s.length };
                },
                setVisible: function (mostrarShapes, mostrarStops) {
                    shapes.setVisible(c.visivel && mostrarShapes);
                    stops.setVisible(c.visivel && mostrarStops);
                }
            };
            camadas.push(c);
            return c;
        },

        /// Ajusta a vista ao conteúdo das camadas visíveis (traçados, ou paragens se não houver traçados).
        ajustar: function () {
            const extent = ol.extent.createEmpty();
            for (const c of camadas.filter(c => c.visivel)) {
                const src = c.shapes.getSource().getFeatures().length ? c.shapes.getSource() : c.stops.getSource();
                if (src.getFeatures().length) ol.extent.extend(extent, src.getExtent());
            }
            if (!ol.extent.isEmpty(extent)) map.getView().fit(extent, { padding: [30, 30, 30, 30], maxZoom: 16 });
        }
    };
};
