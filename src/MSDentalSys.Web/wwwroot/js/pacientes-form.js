window.jQuery(() => {
    const $ = window.jQuery;
    const form = document.getElementById('paciente-form');
    if (!form) return;

    const field = id => form.querySelector(`#${id}`);
    const cedula = field('Cedula');
    const nacimiento = field('FechaNacimiento');
    const marker = field('cedula-required');
    const help = field('cedula-help');
    const validator = $.validator ? $(form).validate() : null;
    const serverErrors = new Map();
    const unchangedServerError = element => {
        const error = serverErrors.get(element);
        return error && element.value === error.value ? error : null;
    };
    const releaseServerError = element => {
        const error = serverErrors.get(element);
        if (!error) return;
        serverErrors.delete(element);
        // Un campo opcional vacío puede omitir el callback success de jQuery.
        // Limpiar aquí el mensaje capturado antes de validar el dato modificado.
        error.container.textContent = '';
        error.container.classList.remove('field-validation-error');
        error.container.classList.add('field-validation-valid');
        element.classList.remove(validator?.settings.errorClass || 'input-validation-error');
    };

    // Una pestaña inactiva no equivale a un campo oculto por una condición.
    // Esta opción pertenece únicamente a la instancia del formulario de Pacientes.
    const ignored = element => element.type === 'hidden' || element.disabled
        || !!element.closest('[hidden], .d-none')
        || (!element.closest('.tab-pane') && $(element).is(':hidden'));
    const firstField = elements => Array.from(form.elements)
        .find(element => elements.has(element) && !ignored(element));
    const revealAndFocus = element => {
        if (!element) return;
        const pane = element.closest('.tab-pane');
        const focus = () => window.requestAnimationFrame(() => {
            if (!ignored(element) && (!pane || pane.classList.contains('active'))) element.focus();
        });
        if (!pane || pane.classList.contains('active')) {
            focus();
            return;
        }
        const tab = form.querySelector(`[data-bs-target="#${pane.id}"]`);
        if (tab && window.bootstrap?.Tab) {
            tab.addEventListener('shown.bs.tab', focus, { once: true });
            window.bootstrap.Tab.getOrCreateInstance(tab).show();
        }
    };

    if (validator) {
        validator.settings.ignore = (_, element) => ignored(element);
        validator.settings.focusInvalid = false;
        $(form).on('invalid-form.pacientes', (_, currentValidator) => {
            revealAndFocus(firstField(new Set(currentValidator.errorList.map(error => error.element))));
        });

        // Conservar la presentación del error sin convertirlo en una nueva
        // regla cliente: un reenvío debe poder volver a consultar al servidor.
        const showErrors = validator.settings.showErrors;
        validator.settings.showErrors = function (errorMap, errorList) {
            if (showErrors) showErrors.call(this, errorMap, errorList);
            else this.defaultShowErrors();
            serverErrors.forEach((error, element) => {
                if (!unchangedServerError(element)) return;
                // Usar el renderizado de unobtrusive conserva el nodo de error
                // y su asociación aria-describedby al restaurar ModelState.
                this.showLabel(element, error.message);
                this.toShow.show();
                error.container.classList.remove('field-validation-valid');
                error.container.classList.add('field-validation-error');
                element.classList.add(this.settings.errorClass);
            });
        };
        ['onfocusout', 'onkeyup', 'onclick'].forEach(eventName => {
            const handler = validator.settings[eventName];
            validator.settings[eventName] = function (element, event) {
                if (eventName === 'onfocusout' && event?.relatedTarget?.closest('[data-bs-toggle="tab"]')) return;
                if (!unchangedServerError(element) && handler) handler.call(this, element, event);
            };
        });
    }
    let rejected = false;
    let previous = cedula.value;
    const format = digits => digits.slice(0, 3) + (digits.length >= 3 ? '-' : '')
        + digits.slice(3, 10) + (digits.length >= 10 ? '-' : '') + digits.slice(10);
    const digitsOf = value => value.replace(/-/g, '');
    const complete = value => /^[0-9]{3}-[0-9]{7}-[0-9]$/.test(value);
    const message = 'Ingresa una cédula completa de 11 dígitos con formato XXX-XXXXXXX-X.';
    const validate = () => {
        const invalid = rejected || (cedula.value !== '' && !complete(cedula.value));
        cedula.setCustomValidity(invalid ? message : cedula.required && !cedula.value
            ? 'La cédula es obligatoria para pacientes de 18 años o más.' : '');
    };
    const refreshValidation = () => {
        validate();
        if (!unchangedServerError(cedula)) {
            releaseServerError(cedula);
            if (validator) $(cedula).valid();
        }
    };
    const adult = () => {
        if (!/^\d{4}-\d{2}-\d{2}$/.test(nacimiento.value)) return false;
        // Comparamos componentes sin interpretar la fecha como UTC; el día procede del servidor.
        const birth = nacimiento.value.split('-').map(Number);
        const today = cedula.dataset.today.split('-').map(Number);
        let age = today[0] - birth[0];
        if (birth[1] > today[1] || (birth[1] === today[1] && birth[2] > today[2])) age--;
        return age >= 18;
    };
    const updateRequired = (revalidate = false) => {
        cedula.required = adult();
        cedula.setAttribute('aria-required', String(cedula.required));
        marker.hidden = !cedula.required;
        help.textContent = cedula.required ? 'Obligatoria para pacientes de 18 años o más.'
            : 'Opcional. Obligatoria a partir de los 18 años.';
        if (revalidate) refreshValidation();
        else validate();
    };
    if ($?.validator) {
        $.validator.addMethod('cedulaCompleta', value => !rejected && (value === '' || complete(value)), message);
        $(cedula).rules('add', {
            required: { depends: adult },
            cedulaCompleta: true,
            messages: { required: 'La cédula es obligatoria para pacientes de 18 años o más.' }
        });
    }
    const reject = () => {
        rejected = true;
        refreshValidation();
    };
    const apply = (value, digitPosition) => {
        rejected = false;
        cedula.value = format(digitsOf(value));
        previous = cedula.value;
        let position = digitPosition + (digitPosition >= 3 ? 1 : 0) + (digitPosition >= 10 ? 1 : 0);
        position = Math.min(position, cedula.value.length);
        cedula.setSelectionRange(position, position);
        refreshValidation();
    };
    cedula.addEventListener('beforeinput', event => {
        if (!event.inputType.startsWith('insert') || event.data == null) return;
        const candidate = cedula.value.slice(0, cedula.selectionStart) + event.data
            + cedula.value.slice(cedula.selectionEnd);
        if (!/^[0-9-]*$/.test(event.data) || digitsOf(candidate).length > 11) {
            event.preventDefault();
            reject();
        }
    });
    cedula.addEventListener('paste', event => {
        event.preventDefault();
        const text = event.clipboardData.getData('text').trim();
        const start = cedula.selectionStart;
        const candidate = cedula.value.slice(0, start) + text + cedula.value.slice(cedula.selectionEnd);
        if (!/^(?:[0-9]+|[0-9]{3}-[0-9]{7}-[0-9])$/.test(text) || digitsOf(candidate).length > 11) {
            reject();
            return;
        }
        apply(candidate, digitsOf(cedula.value.slice(0, start) + text).length);
    });
    cedula.addEventListener('keydown', event => {
        const start = cedula.selectionStart;
        if (start !== cedula.selectionEnd) return;
        // Al borrar junto a un separador, eliminamos el dígito contiguo para evitar un cursor atrapado.
        if (event.key === 'Backspace' && cedula.value[start - 1] === '-') {
            event.preventDefault();
            const position = digitsOf(cedula.value.slice(0, start)).length;
            const digits = digitsOf(cedula.value);
            apply(digits.slice(0, position - 1) + digits.slice(position), position - 1);
        } else if (event.key === 'Delete' && cedula.value[start] === '-') {
            event.preventDefault();
            const position = digitsOf(cedula.value.slice(0, start)).length;
            const digits = digitsOf(cedula.value);
            apply(digits.slice(0, position) + digits.slice(position + 1), position);
        }
    });
    cedula.addEventListener('input', () => {
        const value = cedula.value;
        if (!/^[0-9-]*$/.test(value) || digitsOf(value).length > 11) {
            cedula.value = previous;
            reject();
            return;
        }
        apply(value, digitsOf(value.slice(0, cedula.selectionStart)).length);
    });
    let previousBirthDate = nacimiento.value;
    const birthDateChanged = () => {
        if (nacimiento.value === previousBirthDate) return;
        previousBirthDate = nacimiento.value;
        // Una cédula vacía puede dejar de ser obligatoria al corregir la edad.
        // Una cédula duplicada conserva su error hasta que cambie la cédula.
        if (cedula.value === '') releaseServerError(cedula);
        updateRequired(true);
    };
    nacimiento.addEventListener('input', birthDateChanged);
    nacimiento.addEventListener('change', birthDateChanged);
    if (/^(?:[0-9]{11}|[0-9]{3}-[0-9]{7}-[0-9])$/.test(cedula.value)) {
        cedula.value = format(digitsOf(cedula.value));
        previous = cedula.value;
    }
    updateRequired();

    const selector = field('seguro-selector');
    const seguro = field('SeguroId');
    const radios = form.querySelectorAll('input[name="TieneSeguro"]');
    const sexo = field('Sexo');
    const embarazoField = field('embarazo-field');
    const embarazo = field('Embarazo');

    const updateSeguroVisibility = () => {
        const tieneSeguro = field('tiene-seguro-si')?.checked === true;
        if (selector) selector.hidden = !tieneSeguro;
        if (seguro) {
            seguro.required = tieneSeguro;
            if (!tieneSeguro) seguro.value = '';
        }
    };

    radios.forEach(radio => radio.addEventListener('change', updateSeguroVisibility));
    updateSeguroVisibility();

    const updateEmbarazoVisibility = () => {
        const esFemenino = sexo?.value === 'Femenino';
        if (embarazoField) embarazoField.hidden = !esFemenino;
        if (embarazo && !esFemenino) embarazo.value = '';
    };

    sexo?.addEventListener('change', updateEmbarazoVisibility);
    updateEmbarazoVisibility();

    // Capturar después del formato y las condiciones iniciales, sin validar:
    // normalizar la presentación no cuenta como una edición del usuario.
    form.querySelectorAll('.field-validation-error[data-valmsg-for]').forEach(messageElement => {
        const element = Array.from(form.elements)
            .find(control => control.name === messageElement.dataset.valmsgFor);
        if (!element || !messageElement.textContent.trim()) return;
        const message = messageElement.textContent.trim();
        serverErrors.set(element, { value: element.value, message, container: messageElement });
    });

    const releaseChangedServerError = event => {
        const element = event.target;
        if (serverErrors.has(element) && !unchangedServerError(element)) {
            releaseServerError(element);
            if (validator) $(element).valid();
        }
    };
    form.addEventListener('input', releaseChangedServerError);
    form.addEventListener('change', releaseChangedServerError);
    revealAndFocus(firstField(new Set(serverErrors.keys())));
});
