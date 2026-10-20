// Run: node --test tests/client/pacientes-form.test.cjs
// DOM doubles exercise the patient script without npm dependencies. Browser QA
// remains responsible for the real Bootstrap/jQuery integration and layout.
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const script = fs.readFileSync(path.resolve(__dirname,
    '../../src/MSDentalSys.Web/wwwroot/js/pacientes-form.js'), 'utf8');

function patientHarness(options = {}) {
    const nodes = {};
    const deferred = [];
    const pendingTabs = [];
    const validCalls = [];
    const shownTabs = [];
    let activeElement = null;
    let summaryCalls = 0;

    function matches(element, selector) {
        return selector.split(',').some(part => {
            part = part.trim();
            if (part === ':hidden') {
                return element.type === 'hidden' || Boolean(element.closest('[hidden]')) ||
                    Boolean(element.closest('.tab-pane:not(.active)'));
            }
            if (part === ':visible') return !matches(element, ':hidden');
            if (part === ':disabled') return element.disabled;
            if (part === ':input') return ['INPUT', 'SELECT', 'TEXTAREA', 'BUTTON'].includes(element.tagName);
            const not = [...part.matchAll(/:not\(([^)]+)\)/g)];
            if (not.some(match => matches(element, match[1]))) return false;
            part = part.replace(/:not\([^)]+\)/g, '');
            const attributes = [...part.matchAll(/\[([^\]=]+)(?:=["']?([^\]"']*)["']?)?\]/g)];
            if (attributes.some(([, name, value]) => {
                const actual = element.getAttribute(name);
                return value === undefined ? actual === null : actual !== value;
            })) return false;
            part = part.replace(/\[[^\]]+\]/g, '');
            const id = part.match(/#([\w-]+)/)?.[1];
            if (id && element.id !== id) return false;
            const classes = [...part.matchAll(/\.([\w-]+)/g)].map(match => match[1]);
            if (classes.some(name => !element.classList.contains(name))) return false;
            const tag = part.match(/^[a-z]+/i)?.[0];
            return !tag || element.tagName === tag.toUpperCase();
        });
    }

    function element(tag, id, parent, attributes = {}) {
        const classes = new Set((attributes.class ?? '').split(/\s+/).filter(Boolean));
        let ownText = '';
        const node = {
            tagName: tag.toUpperCase(), id: id ?? '', name: attributes.name ?? '',
            type: attributes.type ?? (tag === 'input' ? 'text' : ''), value: '',
            hidden: false, disabled: false, required: false, checked: false,
            attributes: { ...attributes }, dataset: {}, children: [], style: {},
            parentElement: parent ?? null, listeners: {}, jqueryListeners: [],
            selectionStart: 0, selectionEnd: 0, validationMessage: '', focusCount: 0,
            get textContent() {
                return ownText + this.children.map(child => child.textContent).join('');
            },
            set textContent(value) {
                ownText = String(value);
                this.children.forEach(child => { child.parentElement = null; });
                this.children = [];
            },
            classList: {
                add(...names) { names.forEach(name => classes.add(name)); },
                remove(...names) { names.forEach(name => classes.delete(name)); },
                contains(name) { return classes.has(name); },
                toggle(name, force) {
                    const include = force ?? !classes.has(name);
                    if (include) classes.add(name); else classes.delete(name);
                    return include;
                }
            },
            getAttribute(name) {
                if (name === 'id') return this.id || null;
                if (name === 'name') return this.name || null;
                if (name === 'type') return this.type || null;
                if (name === 'hidden') return this.hidden ? '' : null;
                if (name === 'disabled') return this.disabled ? '' : null;
                if (name === 'class') return [...classes].join(' ');
                return this.attributes[name] ?? null;
            },
            setAttribute(name, value) { this.attributes[name] = String(value); },
            removeAttribute(name) { delete this.attributes[name]; },
            matches(selector) { return matches(this, selector); },
            closest(selector) {
                for (let current = this; current; current = current.parentElement) {
                    if (matches(current, selector)) return current;
                }
                return null;
            },
            querySelectorAll(selector) {
                return this.children.flatMap(child => [child, ...child.querySelectorAll('*')])
                    .filter(child => matches(child, selector));
            },
            querySelector(selector) { return this.querySelectorAll(selector)[0] ?? null; },
            contains(candidate) {
                for (let current = candidate; current; current = current.parentElement) {
                    if (current === this) return true;
                }
                return false;
            },
            addEventListener(type, listener, settings) {
                (this.listeners[type] ??= []).push({ listener, once: settings?.once });
            },
            removeEventListener(type, listener) {
                this.listeners[type] = (this.listeners[type] ?? []).filter(entry => entry.listener !== listener);
            },
            trigger(type, values = {}) {
                const event = { type, target: this, currentTarget: this,
                    preventDefault() { this.defaultPrevented = true; }, ...values };
                for (let current = this; current; current = type === 'blur' ? null : current.parentElement) {
                    event.currentTarget = current;
                    for (const entry of [...(current.listeners[type] ?? [])]) {
                        entry.listener(event);
                        if (entry.once) current.removeEventListener(type, entry.listener);
                    }
                }
                return event;
            },
            emitJQuery(type, ...args) {
                for (const entry of [...this.jqueryListeners]) {
                    if (entry.type.split('.')[0] !== type.split('.')[0]) continue;
                    entry.listener.call(this, { type, target: this }, ...args);
                    if (entry.once) this.jqueryListeners.splice(this.jqueryListeners.indexOf(entry), 1);
                }
            },
            focus() { activeElement = this; this.focusCount++; },
            setCustomValidity(message) { this.validationMessage = message; },
            setSelectionRange(start, end) { this.selectionStart = start; this.selectionEnd = end; }
        };
        for (const [name, value] of Object.entries(attributes)) {
            if (name.startsWith('data-')) {
                const key = name.slice(5).replace(/-([a-z])/g, (_, letter) => letter.toUpperCase());
                node.dataset[key] = value;
            }
        }
        if (id) nodes[id] = node;
        parent?.children.push(node);
        return node;
    }

    const body = element('body');
    const form = element('form', 'paciente-form', body);
    form.action = options.action ?? 'Create';
    const summary = element('div', 'patient-summary', form, { class: 'text-danger', 'data-valmsg-summary': 'true' });
    const nav = element('div', 'patient-tabs', form);
    const panels = {};
    const tabs = {};
    for (const group of ['datos', 'seguro', 'antecedentes']) {
        const panelId = `paciente-${group}-panel`;
        tabs[group] = element('button', `paciente-${group}-tab`, nav,
            { type: 'button', 'data-bs-toggle': 'tab', 'data-bs-target': `#${panelId}`,
                'aria-controls': panelId, class: `nav-link${group === 'datos' ? ' active' : ''}` });
        panels[group] = element('div', panelId, form,
            { class: `tab-pane${group === 'datos' ? ' active show' : ''}`,
                'aria-labelledby': tabs[group].id });
    }
    const insurance = element('div', 'seguro-selector', panels.seguro);
    const pregnancy = element('div', 'embarazo-field', panels.antecedentes);
    const fields = {};
    const messages = {};
    const groups = {
        datos: ['Nombre', 'Apellido', 'Cedula', 'FechaNacimiento', 'Sexo', 'Telefono', 'Correo', 'Direccion'],
        seguro: ['SeguroId', 'ContactoEmergencia', 'TelefonoEmergencia'],
        antecedentes: ['Alergias', 'EnfermedadesSistemicas', 'MedicamentosActuales', 'CirugiasPrevias',
            'HabitosRelevantes', 'Embarazo', 'Observaciones']
    };
    for (const [group, names] of Object.entries(groups)) {
        for (const name of names) {
            const parent = name === 'SeguroId' ? insurance : name === 'Embarazo' ? pregnancy : panels[group];
            const tag = ['Sexo', 'SeguroId', 'Embarazo'].includes(name) ? 'select' : 'input';
            fields[name] = element(tag, name, parent, { name });
            fields[name].form = form;
            messages[name] = element('span', null, parent,
                { 'data-valmsg-for': name, 'data-valmsg-replace': 'true', class: 'field-validation-valid' });
        }
    }
    fields.Nombre.value = 'Ana';
    fields.Apellido.value = 'Perez';
    fields.Cedula.value = options.cedula ?? '001-1234567-8';
    fields.Cedula.dataset.today = '2030-02-08';
    fields.Cedula.setAttribute('aria-describedby', 'cedula-help');
    fields.FechaNacimiento.value = options.birth ?? '2000-02-08';
    fields.Sexo.value = options.sexo ?? 'Femenino';
    fields.Embarazo.value = options.embarazo ?? '';
    fields.SeguroId.value = options.seguroId ?? '';
    for (const [name, value] of Object.entries(options.values ?? {})) fields[name].value = value;
    fields.Cedula.setSelectionRange(fields.Cedula.value.length, fields.Cedula.value.length);
    const yes = element('input', 'tiene-seguro-si', panels.seguro, { type: 'radio', name: 'TieneSeguro' });
    const no = element('input', 'tiene-seguro-no', panels.seguro, { type: 'radio', name: 'TieneSeguro' });
    yes.checked = options.tieneSeguro ?? false;
    no.checked = !yes.checked;
    element('span', 'cedula-required', panels.datos);
    element('small', 'cedula-help', panels.datos);
    const hiddenId = element('input', 'PacienteId', form, { name: 'PacienteId', type: 'hidden' });
    hiddenId.value = options.action === 'Edit' ? '27' : '';
    const antiforgery = element('input', null, form, { name: '__RequestVerificationToken', type: 'hidden' });
    form.elements = [...Object.values(fields), yes, no, hiddenId, antiforgery];
    form.elements.namedItem = name => form.elements.find(field => field.name === name) ?? null;
    for (const [name, message] of Object.entries(options.serverErrors ?? {})) {
        fields[name].classList.add('input-validation-error');
        messages[name].classList.remove('field-validation-valid');
        messages[name].classList.add('field-validation-error');
        messages[name].textContent = message;
    }

    const rules = { Alergias: { maxlength: 300 }, Observaciones: { maxlength: 500 } };
    const methods = {};
    const errorCollection = () => ({
        elements: [],
        show() {
            this.elements.forEach(node => { node.style.display = ''; });
            return this;
        }
    });
    const validator = {
        settings: {
            ignore: ':hidden', focusInvalid: true, errorClass: 'input-validation-error', rules, messages: {},
            onfocusout(field) { this.element(field); },
            onkeyup(field) { this.element(field); },
            onclick(field) { this.element(field); }
        },
        errorList: [], errorMap: {}, submitted: {}, invalid: {}, successList: [],
        toShow: errorCollection(),
        element(field) {
            const ignored = typeof this.settings.ignore === 'function'
                ? this.settings.ignore.call(field, 0, field) : matches(field, this.settings.ignore);
            if (ignored) return true;
            let error = '';
            const fieldRules = rules[field.name] ?? {};
            // jQuery Validate omits success callbacks when a sole optional rule
            // returns dependency-mismatch (for example maxlength on empty text).
            const ruleNames = Object.keys(fieldRules).filter(name => name !== 'messages');
            const dependencyMismatch = !field.value && !field.required
                && ruleNames.length === 1 && ruleNames[0] === 'maxlength';
            for (const [name, parameter] of Object.entries(fieldRules)) {
                if (name === 'messages') continue;
                const enabled = typeof parameter?.depends === 'function' ? parameter.depends.call(field, field) : parameter;
                if (!enabled) continue;
                const valid = name === 'required' ? field.value !== '' :
                    name === 'maxlength' ? !field.value || field.value.length <= parameter :
                    methods[name] ? methods[name].method.call(this, field.value, field, parameter) : true;
                if (!valid) {
                    const configured = fieldRules.messages?.[name] ?? this.settings.messages[field.name]?.[name] ?? methods[name]?.message ?? 'Required';
                    error = typeof configured === 'function' ? configured(parameter, field) : configured;
                    break;
                }
            }
            if (field.required && !field.value && !error) error = 'Required';
            this.currentElement = field;
            this.errorList = error ? [{ element: field, message: error }] : [];
            this.errorMap = error ? { [field.name]: error } : {};
            this.successList = !error && !dependencyMismatch ? [field] : [];
            this.toShow = errorCollection();
            if (this.settings.showErrors) this.settings.showErrors.call(this, this.errorMap, this.errorList);
            else this.defaultShowErrors();
            return !error;
        },
        optional(field) { return !field.value && !field.required; },
        showLabel(field, message) {
            const container = messages[field.name];
            if (!container) return;
            // Model the unobtrusive span and its association, rather than writing
            // directly to the outer asp-validation-for container.
            let label = container.querySelector(`#${field.id}-error`);
            if (!label) {
                container.textContent = '';
                label = element('span', `${field.id}-error`, container);
                const describedBy = new Set((field.getAttribute('aria-describedby') ?? '')
                    .split(/\s+/).filter(Boolean));
                describedBy.add(label.id);
                field.setAttribute('aria-describedby', [...describedBy].join(' '));
            }
            label.textContent = message ?? '';
            container.classList.toggle('field-validation-error', Boolean(message));
            container.classList.toggle('field-validation-valid', !message);
            if (!message) container.textContent = '';
            this.toShow.elements.push(label);
        },
        defaultShowErrors() {
            const field = this.currentElement;
            const error = this.errorList.find(item => item.element === field)?.message ?? '';
            if (field) {
                if (error) this.showLabel(field, error);
                else if (this.successList.includes(field)) this.showLabel(field);
                field.classList.toggle('input-validation-error', Boolean(error));
            }
            this.toShow.show();
        }
    };
    const foreignValidator = { settings: { ignore: ':hidden', focusInvalid: true } };
    const initialForeignSettings = { ...foreignValidator.settings };
    const foreignForm = element('form', 'foreign-form', body);

    function $(value) {
        if (typeof value === 'function') { value(); return; }
        const selected = typeof value === 'string' ? body.querySelectorAll(value) :
            Array.isArray(value) ? value : value ? [value] : [];
        const wrapper = {
            length: selected.length, ...selected,
            data(name) {
                return name === 'validator' ? selected[0] === form ? validator :
                    selected[0] === foreignForm ? foreignValidator : undefined : undefined;
            },
            validate() { return selected[0] === form ? validator : foreignValidator; },
            rules(operation, additions) {
                const name = selected[0].name;
                if (operation === 'add') rules[name] = { ...rules[name], ...additions,
                    messages: { ...rules[name]?.messages, ...additions.messages } };
                if (operation === 'remove') delete rules[name][additions];
                return rules[name] ?? {};
            },
            valid() { validCalls.push(selected[0].name); return validator.element(selected[0]); },
            on(type, listener) {
                for (const node of selected) node.jqueryListeners.push({ type, listener });
                return this;
            },
            one(type, listener) {
                for (const node of selected) node.jqueryListeners.push({ type, listener, once: true });
                return this;
            },
            is(selector) { return selected.some(node => matches(node, selector)); },
            find(selector) { return $(selected.flatMap(node => node.querySelectorAll(selector))); },
            closest(selector) { return $(selected.map(node => node.closest(selector)).filter(Boolean)); },
            filter(predicate) {
                return $(selected.filter((node, index) => typeof predicate === 'function'
                    ? predicate.call(node, index, node) : matches(node, predicate)));
            },
            first() { return $(selected[0]); },
            each(callback) { selected.forEach((node, index) => callback.call(node, index, node)); return this; },
            get(index) { return index === undefined ? selected : selected[index]; },
            toArray() { return selected; },
            attr(name, value) {
                if (value === undefined) return selected[0]?.getAttribute(name);
                selected.forEach(node => node.setAttribute(name, value)); return this;
            }
        };
        return wrapper;
    }
    $.validator = {
        methods, defaults: { ignore: ':hidden', focusInvalid: true },
        addMethod(name, method, message) { methods[name] = { method, message }; }
    };
    $(form).on('invalid-form.validate', () => { summaryCalls++; });
    const bootstrap = { Tab: { getOrCreateInstance(tab) {
        return { show() {
            shownTabs.push(tab.id);
            for (const other of Object.values(tabs)) other.classList.remove('active');
            for (const panel of Object.values(panels)) panel.classList.remove('active', 'show');
            tab.classList.add('active');
            nodes[tab.getAttribute('aria-controls')].classList.add('active', 'show');
            pendingTabs.push(() => {
                tab.trigger('shown.bs.tab');
                tab.emitJQuery('shown.bs.tab');
            });
        } };
    } } };
    const window = { jQuery: $, bootstrap, requestAnimationFrame(callback) { deferred.push(callback); } };
    const document = {
        getElementById: id => nodes[id] ?? null,
        querySelector: selector => body.querySelector(selector),
        querySelectorAll: selector => body.querySelectorAll(selector),
        get activeElement() { return activeElement; }
    };
    vm.runInNewContext(script, { window, document, bootstrap,
        requestAnimationFrame: window.requestAnimationFrame,
        setTimeout(callback) { deferred.push(callback); } });

    return {
        $, form, fields, messages, nodes, panels, tabs, validator, foreignValidator,
        initialForeignSettings, validCalls, shownTabs, yes, no, hiddenId, antiforgery, summary,
        get activeElement() { return activeElement; },
        get summaryCalls() { return summaryCalls; },
        flushFrames() { while (deferred.length) deferred.shift()(); },
        finishTab() { pendingTabs.shift()?.(); },
        activateTab(group) { bootstrap.Tab.getOrCreateInstance(tabs[group]).show(); },
        invalidate(names) {
            validator.errorList = names.map(name => ({ element: fields[name], message: `Error ${name}` }));
            form.emitJQuery('invalid-form', validator);
        }
    };
}

for (const action of ['Create', 'Edit']) {
    test(`${action}: inicia sin validar ni modificar las otras instancias`, () => {
        const harness = patientHarness({ action });
        assert.deepEqual(harness.validCalls, []);
        assert.equal(harness.validator.settings.focusInvalid, false);
        assert.deepEqual(harness.foreignValidator.settings, harness.initialForeignSettings);
        assert.deepEqual(harness.$.validator.defaults, { ignore: ':hidden', focusInvalid: true });
        harness.flushFrames();
        assert.equal(harness.activeElement, null);
    });

    test(`${action}: conserva el error de cédula duplicada al cargar, navegar y perder foco`, () => {
        const message = 'Ya existe un paciente con esa cédula.';
        const harness = patientHarness({ action, cedula: '00112345678', serverErrors: { Cedula: message } });
        const { fields, messages, validator } = harness;
        assert.equal(fields.Cedula.value, '001-1234567-8');
        assert.equal(messages.Cedula.textContent, message);
        assert.deepEqual(harness.validCalls, []);
        harness.flushFrames();
        assert.equal(harness.activeElement, fields.Cedula);
        harness.activateTab('seguro');
        harness.finishTab();
        fields.Cedula.trigger('blur');
        for (const name of ['onfocusout', 'onkeyup', 'onclick']) {
            validator.settings[name].call(validator, fields.Cedula, { keyCode: 9 });
        }
        harness.activateTab('datos');
        harness.finishTab();
        assert.deepEqual(harness.validCalls, []);
        assert.equal(messages.Cedula.textContent, message);
        assert.equal(harness.$(fields.Cedula).valid(), true);
        assert.equal(messages.Cedula.textContent, message);
    });

    test(`${action}: una edición real libera el error servidor y mantiene el formato de cédula`, () => {
        const harness = patientHarness({ action,
            serverErrors: { Cedula: 'Ya existe un paciente con esa cédula.' } });
        const { fields, messages } = harness;
        fields.Cedula.value = '00212345679';
        fields.Cedula.setSelectionRange(11, 11);
        fields.Cedula.trigger('input');
        assert.equal(fields.Cedula.value, '002-1234567-9');
        assert.equal(messages.Cedula.textContent, '');
        assert.equal(fields.Cedula.validationMessage, '');
        assert.ok(harness.validCalls.length > 0);
        // Volver al valor previo después de editar vuelve a permitir la revisión
        // del servidor; no debe resucitar un error capturado de una respuesta vieja.
        fields.Cedula.value = '001-1234567-8';
        fields.Cedula.trigger('input');
        assert.equal(messages.Cedula.textContent, '');
    });

    test(`${action}: activa la pestaña del primer error servidor según el orden del formulario`, () => {
        const harness = patientHarness({ action, tieneSeguro: true,
            serverErrors: { Observaciones: 'Texto demasiado largo.', SeguroId: 'Selecciona un seguro.' } });
        assert.deepEqual(harness.shownTabs, ['paciente-seguro-tab']);
        assert.equal(harness.activeElement, null);
        harness.flushFrames();
        assert.equal(harness.activeElement, null);
        harness.finishTab();
        harness.flushFrames();
        assert.equal(harness.activeElement, harness.fields.SeguroId);
        assert.equal(harness.messages.SeguroId.textContent, 'Selecciona un seguro.');
        assert.deepEqual(harness.validCalls, []);
    });
}

test('valida las pestañas inactivas y excluye campos ocultos por condición, hidden reales y disabled', () => {
    const harness = patientHarness({ tieneSeguro: true, sexo: 'Femenino' });
    const { validator, fields, nodes } = harness;
    const ignored = field => validator.settings.ignore(0, field);
    assert.equal(ignored(fields.ContactoEmergencia), false);
    assert.equal(ignored(fields.Observaciones), false);
    assert.equal(ignored(fields.SeguroId), false);
    assert.equal(ignored(fields.Embarazo), false);
    assert.equal(harness.$(fields.SeguroId).valid(), false);
    assert.equal(ignored(harness.hiddenId), true);
    assert.equal(ignored(harness.antiforgery), true);
    fields.Correo.disabled = true;
    assert.equal(ignored(fields.Correo), true);
    nodes['seguro-selector'].hidden = true;
    nodes['embarazo-field'].hidden = true;
    assert.equal(ignored(fields.SeguroId), true);
    assert.equal(ignored(fields.Embarazo), true);
    assert.equal(harness.$(fields.SeguroId).valid(), true);
    nodes['seguro-selector'].hidden = false;
    nodes['seguro-selector'].classList.add('d-none');
    assert.equal(ignored(fields.SeguroId), true);
});

test('el primer error cliente visual activa su pestaña, espera shown y conserva el summary de unobtrusive', () => {
    const harness = patientHarness();
    harness.invalidate(['Observaciones', 'ContactoEmergencia']);
    assert.equal(harness.summaryCalls, 1);
    assert.deepEqual(harness.shownTabs, ['paciente-seguro-tab']);
    harness.flushFrames();
    assert.equal(harness.activeElement, null);
    harness.finishTab();
    assert.equal(harness.activeElement, null);
    harness.flushFrames();
    assert.equal(harness.activeElement, harness.fields.ContactoEmergencia);
    assert.equal(harness.fields.Observaciones.focusCount, 0);
});

test('un error cliente en la pestaña activa enfoca sin esperar un evento shown inexistente', () => {
    const harness = patientHarness();
    harness.invalidate(['Correo', 'Nombre']);
    harness.flushFrames();
    assert.deepEqual(harness.shownTabs, []);
    assert.equal(harness.activeElement, harness.fields.Nombre);
});

test('los errores en controles condicionales ocultos no capturan el foco', () => {
    const harness = patientHarness({ sexo: 'Masculino', tieneSeguro: false,
        serverErrors: { SeguroId: 'Error seguro.', Embarazo: 'Error embarazo.', Observaciones: 'Error texto.' } });
    assert.deepEqual(harness.shownTabs, ['paciente-antecedentes-tab']);
    harness.finishTab();
    harness.flushFrames();
    assert.equal(harness.activeElement, harness.fields.Observaciones);
    harness.invalidate(['SeguroId', 'Embarazo', 'Nombre']);
    harness.finishTab();
    harness.flushFrames();
    assert.equal(harness.activeElement, harness.fields.Nombre);
});

test('TieneSeguro conserva la selección al mostrar, exige SeguroId y lo limpia al elegir No', () => {
    const harness = patientHarness({ tieneSeguro: true, seguroId: '4' });
    const { fields, nodes, yes, no } = harness;
    assert.equal(nodes['seguro-selector'].hidden, false);
    assert.equal(fields.SeguroId.required, true);
    assert.equal(fields.SeguroId.value, '4');
    yes.checked = false;
    no.checked = true;
    no.trigger('change');
    assert.equal(nodes['seguro-selector'].hidden, true);
    assert.equal(fields.SeguroId.required, false);
    assert.equal(fields.SeguroId.value, '');
    yes.checked = true;
    no.checked = false;
    yes.trigger('change');
    assert.equal(nodes['seguro-selector'].hidden, false);
    assert.equal(fields.SeguroId.required, true);
    assert.equal(harness.validator.settings.ignore(0, fields.SeguroId), false);
    assert.equal(harness.$(fields.SeguroId).valid(), false);
});

test('Embarazo se muestra para Femenino y se limpia al cambiar Sexo', () => {
    const harness = patientHarness({ sexo: 'Femenino', embarazo: 'true' });
    const { fields, nodes } = harness;
    assert.equal(nodes['embarazo-field'].hidden, false);
    assert.equal(fields.Embarazo.value, 'true');
    fields.Sexo.value = 'Masculino';
    fields.Sexo.trigger('change');
    assert.equal(nodes['embarazo-field'].hidden, true);
    assert.equal(fields.Embarazo.value, '');
    fields.Sexo.value = 'Femenino';
    fields.Sexo.trigger('change');
    assert.equal(nodes['embarazo-field'].hidden, false);
    assert.equal(fields.Embarazo.value, '');
});

test('la cédula pasa a obligatoria en el cumpleaños 18 usando la fecha civil del servidor', () => {
    const harness = patientHarness({ cedula: '', birth: '2012-02-09' });
    const { fields, nodes } = harness;
    assert.equal(fields.Cedula.required, false);
    assert.equal(nodes['cedula-required'].hidden, true);
    assert.equal(fields.Cedula.validationMessage, '');
    fields.FechaNacimiento.value = '2012-02-08';
    fields.FechaNacimiento.trigger('input');
    assert.equal(fields.Cedula.required, true);
    assert.equal(fields.Cedula.getAttribute('aria-required'), 'true');
    assert.equal(nodes['cedula-required'].hidden, false);
    assert.match(fields.Cedula.validationMessage, /obligatoria/);
    assert.equal(harness.$(fields.Cedula).valid(), false);
});

test('corregir la fecha a menor de edad libera el error servidor de cédula vacía obligatoria', () => {
    const harness = patientHarness({ cedula: '', birth: '2000-02-08',
        serverErrors: { Cedula: 'La cédula es obligatoria para pacientes de 18 años o más.' } });
    harness.fields.FechaNacimiento.value = '2020-01-01';
    harness.fields.FechaNacimiento.trigger('change');
    assert.equal(harness.fields.Cedula.required, false);
    assert.equal(harness.fields.Cedula.validationMessage, '');
    assert.equal(harness.messages.Cedula.textContent, '');
});

test('cambiar FechaNacimiento no elimina el error servidor de una cédula duplicada', () => {
    const message = 'Ya existe un paciente con esa cédula.';
    const harness = patientHarness({ serverErrors: { Cedula: message } });
    harness.fields.FechaNacimiento.value = '2020-01-01';
    harness.fields.FechaNacimiento.trigger('change');
    assert.equal(harness.fields.Cedula.required, false);
    assert.equal(harness.messages.Cedula.textContent, message);
    assert.deepEqual(harness.validCalls, []);
});

test('se conserva la máscara, el rechazo de caracteres y la validación de cédula incompleta', () => {
    const harness = patientHarness({ cedula: '' });
    const field = harness.fields.Cedula;
    const paste = field.trigger('paste', { clipboardData: { getData: () => '00112345678' } });
    assert.equal(paste.defaultPrevented, true);
    assert.equal(field.value, '001-1234567-8');
    assert.equal(field.validationMessage, '');
    const invalid = field.trigger('beforeinput', { inputType: 'insertText', data: 'x' });
    assert.equal(invalid.defaultPrevented, true);
    assert.match(field.validationMessage, /11 dígitos/);
    field.value = '001123';
    field.setSelectionRange(6, 6);
    field.trigger('input');
    assert.equal(field.value, '001-123');
    assert.match(field.validationMessage, /11 dígitos/);
    assert.equal(harness.$(field).valid(), false);
});

test('el texto servidor prevalece si otra regla local falla al volver a validar el mismo valor', () => {
    const message = 'Cédula rechazada por el servidor.';
    const harness = patientHarness({ cedula: '001', serverErrors: { Cedula: message } });
    assert.equal(harness.$(harness.fields.Cedula).valid(), false);
    assert.equal(harness.messages.Cedula.textContent, message);
});

test('pegar una cédula nueva libera el error aunque paste impida el evento input nativo', () => {
    const harness = patientHarness({ serverErrors: { Cedula: 'Ya existe un paciente con esa cédula.' } });
    const field = harness.fields.Cedula;
    field.setSelectionRange(0, field.value.length);
    const paste = field.trigger('paste', { clipboardData: { getData: () => '00212345679' } });
    assert.equal(paste.defaultPrevented, true);
    assert.equal(field.value, '002-1234567-9');
    assert.equal(harness.messages.Cedula.textContent, '');
    field.setSelectionRange(0, field.value.length);
    field.trigger('paste', { clipboardData: { getData: () => '00112345678' } });
    assert.equal(field.value, '001-1234567-8');
    assert.equal(harness.messages.Cedula.textContent, '');
});

test('restaurar un error servidor conserva un span accesible y la ayuda de cédula', () => {
    for (const cedula of ['001-1234567-8', '001']) {
        const message = 'Cédula rechazada por el servidor.';
        const harness = patientHarness({ cedula, serverErrors: { Cedula: message } });
        const field = harness.fields.Cedula;
        assert.deepEqual(harness.validCalls, []);
        for (let attempt = 0; attempt < 2; attempt++) {
            assert.equal(harness.$(field).valid(), cedula === '001-1234567-8');
            const label = harness.form.querySelector('#Cedula-error');
            assert.ok(label, 'aria-describedby debe apuntar a un error conectado al formulario');
            assert.equal(label.parentElement, harness.messages.Cedula);
            assert.equal(label.textContent, message);
            assert.equal(label.style.display, '');
            assert.equal(harness.form.querySelectorAll('#Cedula-error').length, 1);
            assert.deepEqual(field.getAttribute('aria-describedby').split(/\s+/),
                ['cedula-help', 'Cedula-error']);
            assert.equal(harness.messages.Cedula.classList.contains('field-validation-error'), true);
            assert.equal(field.classList.contains('input-validation-error'), true);
        }
    }
});

for (const action of ['Create', 'Edit']) {
    for (const name of ['Alergias', 'Observaciones']) {
        test(`${action}: vaciar ${name} opcional elimina su error servidor sin callback success`, () => {
            const harness = patientHarness({ action, values: { [name]: 'x'.repeat(501) },
                serverErrors: { [name]: 'El texto supera la longitud permitida.' } });
            assert.equal(harness.messages[name].classList.contains('field-validation-error'), true);
            harness.fields[name].value = '';
            harness.fields[name].trigger('input');
            assert.deepEqual(harness.validator.successList, []);
            assert.equal(harness.messages[name].textContent, '');
            assert.equal(harness.messages[name].classList.contains('field-validation-error'), false);
            assert.equal(harness.messages[name].classList.contains('field-validation-valid'), true);
            assert.equal(harness.fields[name].classList.contains('input-validation-error'), false);
            harness.activateTab('datos');
            harness.finishTab();
            harness.activateTab('antecedentes');
            harness.finishTab();
            assert.equal(harness.messages[name].textContent, '');
        });
    }
}

test('Edit conserva todos los valores y selecciones al recorrer las tres pestañas', () => {
    const harness = patientHarness({
        action: 'Edit', tieneSeguro: true, seguroId: '4', sexo: 'Femenino', embarazo: 'true',
        values: {
            Nombre: 'Ana', Apellido: 'Pérez', Cedula: '001-1234567-8',
            FechaNacimiento: '1990-02-10', Telefono: '809-555-0100', Correo: 'ana@example.com',
            Direccion: 'Calle Principal 10', ContactoEmergencia: 'Luis',
            TelefonoEmergencia: '809-555-0101', Alergias: 'Penicilina',
            EnfermedadesSistemicas: 'Asma', MedicamentosActuales: 'Medicamento habitual',
            CirugiasPrevias: 'Cirugía previa', HabitosRelevantes: 'Sin hábitos de riesgo',
            Observaciones: 'Seguimiento habitual'
        }
    });
    const initial = harness.form.elements.map(field => ({
        name: field.name, value: field.value, checked: field.checked
    }));
    for (const tab of ['seguro', 'antecedentes', 'datos', 'antecedentes', 'seguro', 'datos']) {
        harness.activateTab(tab);
        harness.finishTab();
        harness.flushFrames();
        assert.deepEqual(harness.form.elements.map(field => ({
            name: field.name, value: field.value, checked: field.checked
        })), initial);
    }
    assert.equal(harness.hiddenId.value, '27');
    assert.equal(harness.fields.SeguroId.value, '4');
    assert.equal(harness.fields.Embarazo.value, 'true');
    assert.deepEqual(harness.validCalls, []);
});
