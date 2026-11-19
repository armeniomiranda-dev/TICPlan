// Calendário anual (12 meses) para escolher os dias de uma circulação.
// appTic.calendarioAno(elemento, { ano, dias: ['2026-12-01', …], min, max, readOnly, input, contador })
//  - clique num dia: marca/desmarca; clique no nome do mês: marca/desmarca o mês todo.
//  - input (hidden): recebe as datas marcadas, separadas por vírgula (yyyy-MM-dd).
//  - devolve { adicionar(datas), retirar(datas), limpar(), datas() }.
(function () {
    const MESES = ['Janeiro', 'Fevereiro', 'Março', 'Abril', 'Maio', 'Junho', 'Julho', 'Agosto', 'Setembro', 'Outubro', 'Novembro', 'Dezembro'];
    const SEMANA = ['S', 'T', 'Q', 'Q', 'S', 'S', 'D'];
    const iso = (a, m, d) => `${a}-${String(m + 1).padStart(2, '0')}-${String(d).padStart(2, '0')}`;

    window.appTic = window.appTic || {};
    window.appTic.calendarioAno = function (el, opcoes) {
        const ano = opcoes.ano;
        const min = opcoes.min || iso(ano, 0, 1);
        const max = opcoes.max || iso(ano, 11, 31);
        const marcados = new Set((opcoes.dias || []).filter(d => d >= min && d <= max));
        const celulas = new Map();

        el.classList.add('cal-ano');
        el.innerHTML = '';
        for (let m = 0; m < 12; m++) {
            const bloco = document.createElement('div');
            bloco.className = 'cal-mes';
            const titulo = document.createElement('div');
            titulo.className = 'cal-titulo';
            titulo.textContent = MESES[m];
            bloco.appendChild(titulo);
            const grelha = document.createElement('div');
            grelha.className = 'cal-grelha';
            SEMANA.forEach(s => { const c = document.createElement('div'); c.className = 'cal-sem'; c.textContent = s; grelha.appendChild(c); });
            const primeiro = (new Date(ano, m, 1).getDay() + 6) % 7; // segunda = 0
            for (let i = 0; i < primeiro; i++) grelha.appendChild(document.createElement('div'));
            const diasMes = new Date(ano, m + 1, 0).getDate();
            const doMes = [];
            for (let d = 1; d <= diasMes; d++) {
                const data = iso(ano, m, d);
                const c = document.createElement('button');
                c.type = 'button';
                c.className = 'cal-dia';
                c.textContent = d;
                c.title = data.split('-').reverse().join('/');
                const ativo = data >= min && data <= max;
                if (!ativo || opcoes.readOnly) c.disabled = true;
                if (!ativo) c.classList.add('fora');
                else doMes.push(data);
                c.addEventListener('click', () => { alternar([data], !marcados.has(data)); });
                celulas.set(data, c);
                grelha.appendChild(c);
            }
            if (!opcoes.readOnly && doMes.length) {
                titulo.classList.add('clicavel');
                titulo.title = 'Marcar/desmarcar o mês';
                titulo.addEventListener('click', () => alternar(doMes, !doMes.every(d => marcados.has(d))));
            }
            bloco.appendChild(grelha);
            el.appendChild(bloco);
        }

        function alternar(datas, marcar) {
            datas.filter(d => d >= min && d <= max).forEach(d => marcar ? marcados.add(d) : marcados.delete(d));
            atualizar();
        }

        function atualizar() {
            celulas.forEach((c, d) => c.classList.toggle('marcado', marcados.has(d)));
            const lista = [...marcados].sort();
            if (opcoes.input) opcoes.input.value = lista.join(',');
            if (opcoes.contador) opcoes.contador.textContent = `${lista.length} dia(s)`;
        }

        atualizar();
        return {
            adicionar: datas => alternar(datas, true),
            retirar: datas => alternar(datas, false),
            limpar: () => { marcados.clear(); atualizar(); },
            datas: () => [...marcados].sort()
        };
    };

    // Datas (yyyy-MM-dd) entre de e ate (inclusive) nos dias da semana indicados (0 = domingo … 6 = sábado).
    window.appTic.datasEntre = function (de, ate, diasSemana) {
        const res = [];
        if (!de || !ate || de > ate) return res;
        const d = new Date(de + 'T00:00:00');
        const fim = new Date(ate + 'T00:00:00');
        for (; d <= fim; d.setDate(d.getDate() + 1))
            if (!diasSemana || diasSemana.length === 0 || diasSemana.includes(d.getDay()))
                res.push(iso(d.getFullYear(), d.getMonth(), d.getDate()));
        return res;
    };
})();
