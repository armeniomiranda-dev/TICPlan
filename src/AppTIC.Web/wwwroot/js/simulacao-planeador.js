'use strict';

const SimulacaoPlaneador = (() => {
    const zonas = { Lisboa: [1, 2, 3, 4], Internacional: [5, 6, 7, 8], Norte: [9, 10], Comum: [11, 12, 13, 14, 15, 16, 17] };
    const caisComuns = [11, 12, 13, 14, 15, 16, 17];
    const TOTAL_CAIS = 17;
    /** Cais do mais próximo ao mais distante da saída (igual à planta e ao servidor). */
    const ORDEM_SAIDA = [9, 10, 8, 11, 7, 12, 13, 6, 14, 5, 15, 16, 4, 17, 3, 2, 1];

    /**
     * Esquemas de zonamento por omissão. Cada esquema tem as categorias (tipologias) e a categoria de cada cais.
     * - Regiao: a categoria vem da circulação (Lisboa, Internacional, Norte, Comum).
     * - Permanencia: a categoria vem do tempo de paragem; as paragens rápidas ficam junto à saída e as
     *   esperas longas no fundo do canal, para não bloquearem a via com manobras.
     */
    function esquemasPorOmissao(ordemSaida = ORDEM_SAIDA) {
        const cais = {};
        for (const [zona, lista] of Object.entries(zonas)) for (const id of lista) cais[id] = zona;
        const porPermanencia = {};
        ordemSaida.forEach((id, i) => { porPermanencia[id] = i < 6 ? 'Rapida' : i < 11 ? 'Intermedia' : 'Espera'; });
        return {
            Regiao: {
                criterio: 'Categoria',
                ultimoRecurso: false,
                categorias: [
                    { id: 'Lisboa', nome: 'Lisboa', cor: '#3b82f6', reserva: false, preferencia: 'Nenhuma', ateMinutos: null },
                    { id: 'Internacional', nome: 'Internacional', cor: '#f59e0b', reserva: false, preferencia: 'Nenhuma', ateMinutos: null },
                    { id: 'Norte', nome: 'Norte de Portugal', cor: '#a855f7', reserva: false, preferencia: 'Nenhuma', ateMinutos: null },
                    { id: 'Comum', nome: 'Comum', cor: '#10b981', reserva: true, preferencia: 'Nenhuma', ateMinutos: null }
                ],
                cais
            },
            Permanencia: {
                criterio: 'Permanencia',
                ultimoRecurso: true,
                categorias: [
                    { id: 'Rapida', nome: 'Rápida (largada)', cor: '#22c55e', reserva: false, preferencia: 'Saida', ateMinutos: 5 },
                    { id: 'Intermedia', nome: 'Intermédia', cor: '#eab308', reserva: true, preferencia: 'Nenhuma', ateMinutos: 10 },
                    { id: 'Espera', nome: 'Espera (tomada)', cor: '#ef4444', reserva: false, preferencia: 'Fundo', ateMinutos: null }
                ],
                cais: porPermanencia
            }
        };
    }

    const categoriaDe = (esquema, id) => esquema.categorias.find(c => c.id === id) ?? null;

    function caisDaCategoria(esquema, id) {
        return Array.from({ length: TOTAL_CAIS }, (_, i) => i + 1).filter(n => esquema.cais[n] === id);
    }

    /** Cais que uma categoria pode usar, por nível: os seus, os de reserva e, em último recurso, os restantes. */
    function opcoes(esquema, id, ordemSaida = ORDEM_SAIDA) {
        const categoria = categoriaDe(esquema, id);
        if (!categoria) return { primarios: [], reserva: [], outros: [] };
        const posicao = n => ordemSaida.indexOf(n);
        const ordenar = lista => categoria.preferencia === 'Saida' ? lista.sort((a, b) => posicao(a) - posicao(b))
            : categoria.preferencia === 'Fundo' ? lista.sort((a, b) => posicao(b) - posicao(a)) : lista;
        const todos = Array.from({ length: TOTAL_CAIS }, (_, i) => i + 1);
        const deReserva = n => categoriaDe(esquema, esquema.cais[n])?.reserva === true;
        return {
            primarios: ordenar(todos.filter(n => esquema.cais[n] === id)),
            reserva: ordenar(todos.filter(n => esquema.cais[n] !== id && deReserva(n))),
            outros: esquema.ultimoRecurso ? ordenar(todos.filter(n => esquema.cais[n] !== id && !deReserva(n))) : []
        };
    }

    /** Categoria pelo tempo de paragem: a de menor limite que o cobre; sem limite fica com as mais longas. */
    function categoriaPorPermanencia(esquema, minutos) {
        const comLimite = esquema.categorias.filter(c => c.ateMinutos != null).sort((a, b) => a.ateMinutos - b.ateMinutos);
        return (comLimite.find(c => minutos <= c.ateMinutos) ?? esquema.categorias.find(c => c.ateMinutos == null))?.id ?? '';
    }

    /** Corpo do pedido de planeamento ao servidor (OR-Tools). */
    function pedido(esquema, autocarros, ordemSaida = ORDEM_SAIDA) {
        return {
            Categorias: esquema.categorias.map(c => ({
                Id: c.id, Cais: caisDaCategoria(esquema, c.id), Reserva: !!c.reserva, Preferencia: c.preferencia || 'Nenhuma'
            })),
            UltimoRecurso: !!esquema.ultimoRecurso,
            OrdemSaida: ordemSaida,
            Autocarros: autocarros
        };
    }

    /** Regra do primeiro cais livre (plano provisório quando o servidor não responde). A zona vem de `v.zona`. */
    function alocar(viagens, esquema = esquemasPorOmissao().Regiao, ordemSaida = ORDEM_SAIDA) {
        const existe = v => categoriaDe(esquema, v.zona) !== null;
        const eventos = [];
        for (const v of viagens) {
            if (!existe(v) || v.FimSegundos <= v.InicioSegundos) continue;
            eventos.push({ tempo: v.InicioSegundos, fim: false, viagem: v });
            eventos.push({ tempo: v.FimSegundos, fim: true, viagem: v });
        }
        eventos.sort((a, b) => a.tempo - b.tempo || Number(b.fim) - Number(a.fim) || a.viagem.Id.localeCompare(b.viagem.Id));
        const ocupados = new Set();
        const alocacoes = new Map();
        const permitidos = new Map();
        for (const evento of eventos) {
            const v = evento.viagem;
            if (evento.fim) {
                const cais = alocacoes.get(v.Id);
                if (cais != null) ocupados.delete(cais);
            } else {
                if (!permitidos.has(v.zona)) {
                    const o = opcoes(esquema, v.zona, ordemSaida);
                    permitidos.set(v.zona, [...o.primarios, ...o.reserva, ...o.outros]);
                }
                const cais = permitidos.get(v.zona).find(id => !ocupados.has(id)) ?? null;
                alocacoes.set(v.Id, cais);
                if (cais != null) ocupados.add(cais);
            }
        }
        return alocacoes;
    }

    return {
        zonas, caisComuns, TOTAL_CAIS, ORDEM_SAIDA,
        esquemasPorOmissao, categoriaDe, caisDaCategoria, opcoes, categoriaPorPermanencia, pedido, alocar
    };
})();

if (typeof module !== 'undefined') module.exports = SimulacaoPlaneador;
