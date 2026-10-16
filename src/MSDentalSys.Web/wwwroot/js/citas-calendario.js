(() => {
    'use strict';

    const form = document.getElementById('citas-filtros');
    if (!form) return;

    const viewLinks = document.querySelectorAll('[data-calendar-view]');
    const updateViewLinks = () => {
        // Switching views uses the MVC Index, including filters changed without a POST.
        viewLinks.forEach(link => {
            const url = new URL(form.action, window.location.href);
            url.search = new URLSearchParams(new FormData(form)).toString();
            url.searchParams.set('vista', link.dataset.calendarView);
            link.href = url.href;
        });
    };
    form.addEventListener('change', updateViewLinks);
    form.addEventListener('input', updateViewLinks);
    updateViewLinks();

    const element = document.getElementById('citas-calendario');
    if (!element) return;

    const errorBox = document.getElementById('calendario-error');
    const errorText = document.getElementById('calendario-error-texto');
    const retryButton = document.getElementById('calendario-reintentar');
    const status = document.getElementById('calendario-estado');
    const dateFilter = document.getElementById('fecha');
    let pendingRequest;
    let appliedDate = dateFilter.value;

    const showError = message => {
        errorText.textContent = message;
        errorBox.hidden = false;
        element.classList.add('calendar-query-failed');
        status.textContent = '';
    };

    if (!window.FullCalendar?.Calendar) {
        showError('No se pudo iniciar el calendario. Recarga la página o utiliza la vista Lista.');
        retryButton.hidden = true;
        return;
    }

    // The application stores civil DateTime values. Never send toISOString() or an offset.
    const civilDateTime = date => {
        const two = value => String(value).padStart(2, '0');
        return `${String(date.getFullYear()).padStart(4, '0')}-${two(date.getMonth() + 1)}-${two(date.getDate())}`
            + `T${two(date.getHours())}:${two(date.getMinutes())}:${two(date.getSeconds())}`;
    };

    const stateClass = {
        Pendiente: 'calendar-state-pending',
        Confirmada: 'calendar-state-confirmed',
        Atendida: 'calendar-state-attended',
        Cancelada: 'calendar-state-cancelled',
        'No asistió': 'calendar-state-absent'
    };
    const stateText = event => event.extendedProps.estado === 'Cancelada'
        ? 'Cancelada · No ocupa agenda'
        : event.extendedProps.estado;
    const dateFormatter = new Intl.DateTimeFormat('es-DO', {
        dateStyle: 'short', timeStyle: 'short', hour12: false
    });

    const calendar = new FullCalendar.Calendar(element, {
        locale: 'es',
        timeZone: 'local',
        initialView: 'dayGridMonth',
        initialDate: element.dataset.fecha || undefined,
        headerToolbar: {
            left: 'prev,next today',
            center: 'title',
            right: 'dayGridMonth,timeGridWeek'
        },
        buttonText: { today: 'Hoy', month: 'Mes', week: 'Semana' },
        height: 'auto',
        firstDay: 1,
        allDaySlot: false,
        nowIndicator: true,
        dayMaxEvents: 3,
        eventDisplay: 'block',
        displayEventEnd: false,
        eventTimeFormat: { hour: '2-digit', minute: '2-digit', hour12: false },
        slotLabelFormat: { hour: '2-digit', minute: '2-digit', hour12: false },
        editable: false,
        eventStartEditable: false,
        eventDurationEditable: false,
        selectable: false,
        droppable: false,
        // Only a rendering fallback. Null ends stay null in the event and server contract.
        defaultTimedEventDuration: '00:15:00',
        forceEventDuration: false,
        events: async (range, success, failure) => {
            pendingRequest?.abort();
            const request = new AbortController();
            pendingRequest = request;
            errorBox.hidden = true;
            element.classList.remove('calendar-query-failed');
            element.setAttribute('aria-busy', 'true');
            status.textContent = 'Cargando citas…';

            const url = new URL(element.dataset.endpoint, window.location.href);
            url.searchParams.set('start', civilDateTime(range.start));
            url.searchParams.set('end', civilDateTime(range.end));
            ['estado', 'pacienteId', 'odontologoId'].forEach(name => {
                const value = form.elements.namedItem(name)?.value;
                if (value) url.searchParams.set(name, value);
            });

            try {
                const response = await fetch(url, {
                    signal: request.signal,
                    credentials: 'same-origin',
                    headers: { Accept: 'application/json' }
                });
                if (!response.ok || response.redirected
                    || !response.headers.get('content-type')?.includes('application/json')) {
                    throw new Error(`Calendar HTTP ${response.status}`);
                }
                const events = await response.json();
                if (!Array.isArray(events)) throw new Error('Invalid calendar response');
                if (pendingRequest !== request) {
                    success([]); // Settle an obsolete fetch; FullCalendar ignores its fetch ID.
                    return;
                }
                // Keep event anchors on this application, including middle-click/open-in-new-tab.
                const safeEvents = events.map(event => {
                    let detailsUrl;
                    try {
                        const target = new URL(event.url, window.location.href);
                        if (event.url && target.origin === window.location.origin
                            && ['http:', 'https:'].includes(target.protocol)) detailsUrl = target.href;
                    } catch { /* An invalid URL must never become a link. */ }
                    return { ...event, url: detailsUrl };
                });
                success(safeEvents);
                status.textContent = events.length
                    ? `${events.length} cita${events.length === 1 ? '' : 's'} en el período y los filtros seleccionados.`
                    : 'No hay citas registradas para este período y estos filtros. Esto no garantiza disponibilidad.';
            } catch (error) {
                if (error.name === 'AbortError' || pendingRequest !== request) {
                    success([]);
                    return;
                }
                showError('No se pudieron consultar las citas. La agenda visible puede estar desactualizada; reintenta o utiliza Lista.');
                failure(error);
            } finally {
                if (pendingRequest === request) {
                    pendingRequest = null;
                    element.setAttribute('aria-busy', 'false');
                }
            }
        },
        eventClassNames: ({ event }) => {
            const classes = [stateClass[event.extendedProps.estado] || 'calendar-state-pending'];
            if (!event.extendedProps.duracionConocida) classes.push('calendar-duration-unknown');
            return classes;
        },
        eventContent: ({ event, timeText }) => {
            const content = document.createElement('div');
            content.className = 'calendar-event-content';
            const addLine = (text, className) => {
                const line = document.createElement('span');
                line.className = className;
                line.textContent = text;
                content.appendChild(line);
            };
            if (!event.extendedProps.duracionConocida) {
                addLine('Duración no registrada', 'calendar-event-duration');
            }
            addLine(`${timeText} ${event.title}`.trim(), 'calendar-event-title');
            addLine(event.extendedProps.odontologoNombre, 'calendar-event-dentist');
            addLine(stateText(event), 'calendar-event-state');
            return { domNodes: [content] };
        },
        eventDidMount: ({ event, el }) => {
            const details = [event.title, event.extendedProps.odontologoNombre, stateText(event)];
            if (event.start) details.push(`Inicio: ${dateFormatter.format(event.start)}`);
            if (event.extendedProps.duracionConocida && event.end) {
                details.push(`Fin: ${dateFormatter.format(event.end)}`);
            } else {
                details.push('Duración no registrada; bloque visual de 15 minutos');
            }
            el.title = details.join(' · ');
            el.setAttribute('aria-label', `${el.title}. Abrir detalles de la cita.`);
        },
        eventClick: ({ event, jsEvent }) => {
            jsEvent.preventDefault();
            if (!event.url) return;
            const url = new URL(event.url, window.location.href);
            if (url.origin === window.location.origin) window.location.assign(url.href);
        }
    });

    const applyDate = () => {
        const selectedDate = dateFilter.value;
        // Filtering must preserve a month/week reached with the navigation buttons.
        if (selectedDate === appliedDate || !dateFilter.validity.valid) return;
        appliedDate = selectedDate;
        if (selectedDate && dateFilter.validity.valid) {
            calendar.gotoDate(selectedDate);
        } else if (!selectedDate) {
            calendar.today();
        }
    };

    ['estado', 'pacienteId', 'odontologoId'].forEach(name => {
        form.elements.namedItem(name)?.addEventListener('change', () => calendar.refetchEvents());
    });
    dateFilter.addEventListener('change', applyDate);
    form.addEventListener('submit', event => {
        event.preventDefault();
        applyDate();
        calendar.refetchEvents();
        updateViewLinks();
    });
    retryButton.addEventListener('click', () => calendar.refetchEvents());
    window.addEventListener('pagehide', () => pendingRequest?.abort());
    calendar.render();
})();
