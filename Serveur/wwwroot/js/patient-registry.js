(() => {
    document.querySelectorAll('#registry-mapping fieldset').forEach(group => {
        const table = group.querySelector('.registry-table');
        const update = () => group.querySelectorAll('.registry-column').forEach(select => {
            Array.from(select.options).forEach(option => {
                option.hidden = !!option.dataset.table && option.dataset.table !== table.value;
                option.disabled = option.hidden;
            });
            if (select.selectedOptions[0]?.disabled) select.value = '';
        });
        table.addEventListener('change', update); update();
    });
    const progress = document.getElementById('registry-progress');
    if (progress?.dataset.running === 'true') {
        const poll = async () => {
            try {
                const response = await fetch('?handler=Progress', { cache: 'no-store', credentials: 'same-origin' });
                if (!response.ok || !response.headers.get('content-type')?.includes('application/json')) throw new Error();
                const status = await response.json(); progress.textContent = status.message;
                if (!status.running) { location.replace(location.pathname); return; }
                setTimeout(poll, 2000);
            } catch { progress.textContent = 'Suivi interrompu. Actualisez la page pour consulter l’état de l’import sur le serveur.'; }
        };
        setTimeout(poll, 1500);
    }
    document.getElementById('registry-details')?.scrollIntoView({ block: 'start' });
})();
