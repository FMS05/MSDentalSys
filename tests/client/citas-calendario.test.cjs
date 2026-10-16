// Run: node --test tests/client/citas-calendario.test.cjs
// No npm dependencies; the small DOM double exercises application behavior.
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const script = fs.readFileSync(path.resolve(__dirname,
    '../../src/MSDentalSys.Web/wwwroot/js/citas-calendario.js'), 'utf8');
const range = { start: new Date(2030, 1, 1), end: new Date(2030, 2, 1) };
const jsonResponse = events => ({
    ok: true,
    headers: new Map([['content-type', 'application/json']]),
    json: async () => events
});

function fakeElement(value = '') {
    return {
        value, hidden: false, textContent: '', dataset: {}, attributes: {}, children: [],
        listeners: {}, validity: { valid: true },
        classList: {
            values: new Set(),
            add(value) { this.values.add(value); },
            remove(value) { this.values.delete(value); },
            contains(value) { return this.values.has(value); }
        },
        addEventListener(type, listener) { (this.listeners[type] ??= []).push(listener); },
        trigger(type, event = {}) {
            for (const listener of this.listeners[type] ?? []) listener(event);
        },
        appendChild(child) { this.children.push(child); },
        setAttribute(name, value) { this.attributes[name] = value; }
    };
}

function calendarHarness({ dentist = true, calendarPresent = true, loaded = true,
    date = '2030-02-08', fetchImpl = async () => jsonResponse([]) } = {}) {
    const fields = {
        fecha: fakeElement(date), estado: fakeElement('Confirmada'), pacienteId: fakeElement('12'),
        PacienteNombre: fakeElement('Ana'), vista: fakeElement('calendario')
    };
    if (dentist) fields.odontologoId = fakeElement('dentist-1');
    const form = fakeElement();
    form.action = 'https://clinic.example/Citas';
    form.elements = { namedItem: name => fields[name] };
    const nodes = { 'citas-filtros': form, ...fields };
    for (const id of ['calendario-error', 'calendario-error-texto', 'calendario-reintentar', 'calendario-estado']) {
        nodes[id] = fakeElement();
    }
    if (calendarPresent) {
        nodes['citas-calendario'] = fakeElement();
        nodes['citas-calendario'].dataset = { endpoint: '/Citas/Calendario', fecha: date };
    }
    const links = ['calendario', 'lista'].map(view => {
        const link = fakeElement();
        link.dataset.calendarView = view;
        return link;
    });
    const window = fakeElement();
    window.location = {
        href: 'https://clinic.example/Citas', origin: 'https://clinic.example',
        assign(url) { window.assigned = url; }
    };
    let calendar;
    class Calendar {
        constructor(container, options) {
            this.options = options;
            this.refetchCount = 0;
            this.gotoCalls = [];
            this.todayCalls = 0;
            calendar = this;
        }
        render() { this.rendered = true; }
        gotoDate(date) { this.gotoCalls.push(date); }
        today() { this.todayCalls++; }
        refetchEvents() { this.refetchCount++; }
    }
    if (loaded) window.FullCalendar = { Calendar };
    vm.runInNewContext(script, {
        window, FullCalendar: window.FullCalendar, URL, URLSearchParams, AbortController, Intl,
        document: {
            getElementById: id => nodes[id], querySelectorAll: () => links,
            createElement: () => fakeElement()
        },
        FormData: class {
            constructor() {
                this.entries = Object.entries(fields).map(([name, field]) => [name, field.value]);
            }
            [Symbol.iterator]() { return this.entries[Symbol.iterator](); }
        },
        fetch: fetchImpl
    });
    return { nodes, fields, links, window, form, calendar };
}

test('consulta el intervalo civil sin offset y los filtros elegidos', async () => {
    let requestUrl;
    const { calendar, nodes } = calendarHarness({ fetchImpl: async url => {
        requestUrl = url;
        return jsonResponse([]);
    } });
    await calendar.options.events(range, () => {}, assert.fail);
    assert.equal(requestUrl.searchParams.get('start'), '2030-02-01T00:00:00');
    assert.equal(requestUrl.searchParams.get('end'), '2030-03-01T00:00:00');
    assert.equal(requestUrl.searchParams.get('odontologoId'), 'dentist-1');
    assert.equal(requestUrl.searchParams.get('pacienteId'), '12');
    assert.equal(requestUrl.searchParams.get('estado'), 'Confirmada');
    assert.equal(nodes['citas-calendario'].attributes['aria-busy'], 'false');
    assert.match(nodes['calendario-estado'].textContent, /no garantiza disponibilidad/);
});

test('omite odontólogo cuando el formulario no permite elegirlo', async () => {
    let requestUrl;
    const { calendar } = calendarHarness({ dentist: false, fetchImpl: async url => {
        requestUrl = url;
        return jsonResponse([]);
    } });
    await calendar.options.events(range, () => {}, assert.fail);
    assert.equal(requestUrl.searchParams.has('odontologoId'), false);
});

test('alternar entre Lista y Calendario conserva filtros sin montar el calendario', () => {
    const { links, fields, form } = calendarHarness({ calendarPresent: false });
    const listUrl = new URL(links[1].href);
    assert.equal(listUrl.searchParams.get('vista'), 'lista');
    assert.equal(listUrl.searchParams.get('fecha'), '2030-02-08');
    assert.equal(listUrl.searchParams.get('pacienteId'), '12');
    fields.estado.value = 'Cancelada';
    form.trigger('change');
    assert.equal(new URL(links[0].href).searchParams.get('estado'), 'Cancelada');
});

test('Filtrar mantiene el período navegado y cambiar Fecha sí navega', () => {
    const { calendar, fields, form } = calendarHarness();
    let prevented = false;
    form.trigger('submit', { preventDefault() { prevented = true; } });
    assert.ok(prevented);
    assert.equal(calendar.gotoCalls.length, 0);
    assert.equal(calendar.refetchCount, 1);
    fields.fecha.value = '2030-03-03';
    fields.fecha.trigger('change');
    assert.equal(calendar.gotoCalls[0], '2030-03-03');
    fields.fecha.value = '';
    fields.fecha.trigger('change');
    assert.equal(calendar.todayCalls, 1);
});

test('cambiar estado, paciente u odontólogo recarga las citas', () => {
    const { fields, calendar } = calendarHarness();
    for (const name of ['estado', 'pacienteId', 'odontologoId']) fields[name].trigger('change');
    assert.equal(calendar.refetchCount, 3);
});

test('duración histórica nula conserva end null y explica el bloque visual', async () => {
    const input = {
        id: 1, title: 'Ana', start: '2030-02-01T10:00:00', end: null,
        duracionConocida: false, url: '/Citas/Details/1'
    };
    const { calendar } = calendarHarness({ fetchImpl: async () => jsonResponse([input]) });
    let received;
    await calendar.options.events(range, events => { received = events; }, assert.fail);
    assert.equal(received[0].end, null);
    assert.equal(calendar.options.defaultTimedEventDuration, '00:15:00');
    assert.equal(calendar.options.forceEventDuration, false);
    const event = {
        title: input.title, start: new Date(2030, 1, 1, 10), end: null,
        extendedProps: { duracionConocida: false, estado: 'Pendiente' }
    };
    const content = calendar.options.eventContent({ event, timeText: '10:00' }).domNodes[0];
    assert.equal(content.children[0].textContent, 'Duración no registrada');
    const mountedElement = fakeElement();
    calendar.options.eventDidMount({ event, el: mountedElement });
    assert.match(mountedElement.title, /bloque visual de 15 minutos/);
    assert.doesNotMatch(mountedElement.title, /Fin:/);
});

test('nombres con marcado se insertan como texto, no HTML', () => {
    const { calendar } = calendarHarness();
    const payload = '<img src=x onerror=alert(1)>';
    const content = calendar.options.eventContent({
        event: {
            title: payload,
            extendedProps: { odontologoNombre: payload, estado: 'Confirmada', duracionConocida: true }
        }, timeText: '10:00'
    }).domNodes[0];
    assert.equal(content.children[0].textContent, '10:00 ' + payload);
    assert.equal(content.children[1].textContent, payload);
    assert.ok(content.children.every(child => !Object.hasOwn(child, 'innerHTML')));
});

test('URLs externas o javascript no llegan a los enlaces renderizados', async () => {
    const { calendar } = calendarHarness({ fetchImpl: async () => jsonResponse([
        { url: 'javascript:alert(1)' }, { url: 'https://evil.example/' }, { url: '/Citas/Details/5' }
    ]) });
    let received;
    await calendar.options.events(range, events => { received = events; }, assert.fail);
    assert.equal(received[0].url, undefined);
    assert.equal(received[1].url, undefined);
    assert.equal(received[2].url, 'https://clinic.example/Citas/Details/5');
});

test('seleccionar una cita navega a Details', () => {
    const { calendar, window } = calendarHarness();
    let prevented = false;
    calendar.options.eventClick({
        event: { url: '/Citas/Details/5' },
        jsEvent: { preventDefault() { prevented = true; } }
    });
    assert.ok(prevented);
    assert.equal(window.assigned, 'https://clinic.example/Citas/Details/5');
});

test('una sesión expirada muestra error y permite reintentar', async () => {
    const { calendar, nodes } = calendarHarness({ fetchImpl: async () => ({
        ok: true, redirected: true,
        headers: new Map([['content-type', 'text/html']]), json: assert.fail
    }) });
    let failed = false;
    await calendar.options.events(range, assert.fail, () => { failed = true; });
    assert.ok(failed);
    assert.equal(nodes['calendario-error'].hidden, false);
    assert.ok(nodes['citas-calendario'].classList.contains('calendar-query-failed'));
    nodes['calendario-reintentar'].trigger('click');
    assert.equal(calendar.refetchCount, 1);
});

test('sin FullCalendar conserva formulario y ofrece la vista Lista', () => {
    const { nodes, form } = calendarHarness({ loaded: false });
    assert.equal(nodes['calendario-error'].hidden, false);
    assert.equal(nodes['calendario-reintentar'].hidden, true);
    assert.match(nodes['calendario-error-texto'].textContent, /Lista/);
    assert.equal(form.listeners.submit, undefined);
});

test('una respuesta obsoleta se aborta y no reemplaza los resultados recientes', async () => {
    const calls = [];
    const { calendar } = calendarHarness({ fetchImpl: (url, options) => new Promise(resolve => {
        calls.push({ resolve, signal: options.signal });
    }) });
    let oldResults;
    let newResults;
    const first = calendar.options.events(range, events => { oldResults = events; }, assert.fail);
    const second = calendar.options.events(range, events => { newResults = events; }, assert.fail);
    assert.equal(calls[0].signal.aborted, true);
    calls[1].resolve(jsonResponse([{ id: 2, url: '/Citas/Details/2' }]));
    await second;
    calls[0].resolve(jsonResponse([{ id: 1 }]));
    await first;
    assert.equal(oldResults.length, 0);
    assert.equal(newResults[0].id, 2);
});
