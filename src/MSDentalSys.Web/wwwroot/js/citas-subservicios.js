(() => {
    const servicio = document.getElementById('ServicioOdontologicoId');
    const subservicio = document.getElementById('SubservicioOdontologicoId');
    const status = document.getElementById('subservicios-status');
    if (!servicio || !subservicio || !status) return;
    let pending;

    servicio.addEventListener('change', async () => {
        pending?.abort();
        const request = new AbortController();
        pending = request;
        const serviceId = servicio.value;
        subservicio.replaceChildren(new Option('Seleccione un subservicio', ''));
        subservicio.disabled = true;
        status.textContent = '';
        if (!serviceId) return;
        status.textContent = 'Cargando subservicios…';
        const isCurrent = () => pending === request && servicio.value === serviceId;
        try {
            const response = await fetch(`${subservicio.dataset.endpoint}?servicioOdontologicoId=${encodeURIComponent(serviceId)}`, {
                signal: request.signal
            });
            if (!response.ok) throw new Error('No se pudieron cargar los subservicios.');
            const items = await response.json();
            if (!isCurrent()) return;
            items.forEach(item => subservicio.add(new Option(
                `${item.nombre} — ${item.duracionEstimadaMinutos} min`, item.subservicioOdontologicoId)));
            subservicio.disabled = items.length === 0;
            status.textContent = items.length ? '' : 'Este servicio no tiene subservicios activos.';
        } catch (error) {
            if (error.name === 'AbortError' || !isCurrent()) return;
            status.textContent = 'No se pudieron cargar los subservicios. Vuelve a seleccionar el servicio.';
        }
    });
})();
