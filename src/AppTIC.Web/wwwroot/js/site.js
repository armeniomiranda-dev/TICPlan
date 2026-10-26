// Utilitários partilhados AppTIC.
window.appTic = {
    token: () => document.querySelector('meta[name="request-verification-token"]').content,

    // Submete um POST (com token anti-forgery) após confirmação. Usado pelos botões "Eliminar" nas grelhas.
    postConfirm: function (url, message) {
        if (message && !confirm(message)) return;
        const form = document.createElement('form');
        form.method = 'post';
        form.action = url;
        const input = document.createElement('input');
        input.type = 'hidden';
        input.name = '__RequestVerificationToken';
        input.value = appTic.token();
        form.appendChild(input);
        document.body.appendChild(form);
        form.submit();
    },

    postJson: async function (url, body) {
        const res = await fetch(url, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': appTic.token() },
            body: JSON.stringify(body)
        });
        if (!res.ok) throw new Error(await res.text() || res.statusText);
        return res.json();
    },

    // Exportação Excel nas grelhas Syncfusion (toolbar "ExcelExport").
    gridToolbarClick: function (args) {
        if (args.item.id.endsWith('_excelexport')) this.excelExport();
    }
};

// Textos das grelhas Syncfusion em português (substitui os textos por omissão en-US, sem exigir dados CLDR).
if (window.ej) {
    ej.base.L10n.load({
        'en-US': {
            grid: {
                EmptyRecord: 'Sem registos', Search: 'Pesquisar', Excelexport: 'Exportar Excel',
                FilterButton: 'Filtrar', ClearButton: 'Limpar', SelectAll: 'Selecionar tudo', Blanks: 'Vazios',
                SortAscending: 'Ordenar ascendente', SortDescending: 'Ordenar descendente', ClearFilter: 'Limpar filtro',
                TextFilter: 'Filtros de texto', NumberFilter: 'Filtros numéricos', DateFilter: 'Filtros de data',
                OKButton: 'OK', CancelButton: 'Cancelar', NoResult: 'Sem resultados', Equal: 'Igual', NotEqual: 'Diferente',
                Contains: 'Contém', StartsWith: 'Começa por', EndsWith: 'Termina em', LessThan: 'Menor que',
                GreaterThan: 'Maior que', CustomFilter: 'Filtro personalizado'
            },
            pager: {
                currentPageInfo: '{0} de {1} páginas', totalItemsInfo: '({0} registos)', firstPageTooltip: 'Primeira página',
                lastPageTooltip: 'Última página', nextPageTooltip: 'Página seguinte', previousPageTooltip: 'Página anterior',
                pagerDropDown: 'Registos por página', totalItemInfo: '({0} registo)'
            },
            dropdowns: { noRecordsTemplate: 'Sem registos', actionFailureTemplate: 'Erro ao carregar' },
            datepicker: { today: 'Hoje', placeholder: 'dd/mm/aaaa' },
            timepicker: { placeholder: 'hh:mm' }
        }
    });
}
