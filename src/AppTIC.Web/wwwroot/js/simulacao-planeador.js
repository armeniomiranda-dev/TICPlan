'use strict';

const SimulacaoPlaneador = (() => {
    const zonas = { Lisboa: [1, 2, 3, 4], Internacional: [5, 6, 7, 8], Norte: [9, 10], Comum: [11, 12, 13, 14, 15, 16, 17] };
    const caisComuns = [11, 12, 13, 14, 15, 16, 17];

    function alocar(viagens) {
        const eventos = [];
        for (const v of viagens) {
            if (!Object.hasOwn(zonas, v.zona) || v.FimSegundos <= v.InicioSegundos) continue;
            eventos.push({ tempo: v.InicioSegundos, fim: false, viagem: v });
            eventos.push({ tempo: v.FimSegundos, fim: true, viagem: v });
        }
        eventos.sort((a, b) => a.tempo - b.tempo || Number(b.fim) - Number(a.fim) || a.viagem.Id.localeCompare(b.viagem.Id));
        const ocupados = new Set();
        const alocacoes = new Map();
        for (const evento of eventos) {
            const v = evento.viagem;
            if (evento.fim) {
                const cais = alocacoes.get(v.Id);
                if (cais != null) ocupados.delete(cais);
            } else {
                const primarios = zonas[v.zona];
                const reserva = caisComuns;
                const cais = primarios.find(id => !ocupados.has(id))
                    ?? reserva.find(id => !ocupados.has(id)) ?? null;
                alocacoes.set(v.Id, cais);
                if (cais != null) ocupados.add(cais);
            }
        }
        return alocacoes;
    }

    return { zonas, caisComuns, alocar };
})();

if (typeof module !== 'undefined') module.exports = SimulacaoPlaneador;
