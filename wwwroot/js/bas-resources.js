/* Resource inputs discovered from native BAS XML. JSON contains overrides only. */
window.BasResourceForm = class {
    constructor(container, json, status) {
        this.container = container; this.json = json; this.status = status;
        this.resources = []; this.disabled = false;
        this.advanced = document.getElementById('advancedResources');
        this.advanced.onchange = () => {
            try { this.updateVisibility(this.values()); }
            catch (error) { this.status.textContent = error.message; }
        };
        json.addEventListener('change', () => this.render());
    }
    clear(message) {
        this.resources = []; this.container.replaceChildren(); this.status.textContent = message;
        this.advanced.checked = false; document.getElementById('advancedResourcesLabel').hidden = true;
    }
    values() {
        const values = JSON.parse(this.json.value);
        if (!values || typeof values !== 'object' || Array.isArray(values)) throw new Error('Parameters must be a JSON object');
        return values;
    }
    setDisabled(value) {
        this.disabled = value;
        this.container.querySelectorAll('input, textarea, select, button').forEach(input => {
            input.disabled = value || input.dataset.readonly === 'true' || !!input.closest('.resource-field')?.hidden;
        });
    }
    load(data) {
        this.resources = data.resources; this.engineVersion = data.engineVersion; this.render();
    }
    updateVisibility(values) {
        let count = 0;
        for (const row of this.container.children) {
            const resource = this.resources.find(item => item.name === row.dataset.resourceRow);
            const dependency = resource.conditionResource;
            const value = Object.hasOwn(values, dependency) ? values[dependency] : this.resources.find(item => item.name === dependency)?.defaultValue;
            row.hidden = (!!resource.advanced && !this.advanced.checked) || (!!dependency && String(value) !== resource.conditionValue);
            if (!row.hidden) count++;
        }
        this.status.textContent = `${count} inputs · BAS ${this.engineVersion || ''} · Unchanged fields use XML defaults.`;
        this.setDisabled(this.disabled);
    }
    render() {
        let values;
        try { values = this.values(); }
        catch (error) { this.status.textContent = error.message; return; }
        this.container.replaceChildren();
        const known = new Set(this.resources.map(resource => resource.name));
        const unknown = Object.keys(values).filter(name => !known.has(name));
        this.status.textContent = `${this.resources.length} resources · BAS ${this.engineVersion || ''}`
            + (unknown.length ? ' · Unknown overrides: ' + unknown.join(', ') : ' · Edit fields to override XML defaults.');
        const visible = this.resources.filter(resource => resource.visible !== false);
        document.getElementById('advancedResourcesLabel').hidden = !visible.some(resource => resource.advanced);
        for (const resource of visible) {
            const row = document.createElement('div'); row.className = 'resource-field';
            row.dataset.resourceRow = resource.name;
            const label = document.createElement('label'); label.textContent = resource.label || resource.name;
            label.title = resource.hint || resource.name;
            const secret = /password|token|secret/i.test(resource.name);
            const booleanOptions = resource.type === 'Select' && resource.options?.length === 2
                && resource.options.some(value => value.toLowerCase() === 'true') && resource.options.some(value => value.toLowerCase() === 'false');
            const multiline = !secret && (resource.multiline || resource.type === 'RandomString' || /[\r\n]/.test(String(values[resource.name] ?? resource.defaultValue)));
            const input = document.createElement(resource.type === 'Select' && !booleanOptions ? 'select' : multiline ? 'textarea' : 'input');
            if (input.tagName === 'SELECT') {
                for (const value of resource.options || []) {
                    const option = document.createElement('option'); option.value = value; option.textContent = value; input.append(option);
                }
            }
            input.dataset.resource = resource.name;
            input.dataset.readonly = String(!resource.editable);
            if (input.tagName === 'INPUT') {
                input.type = booleanOptions ? 'checkbox' : resource.type === 'FixedInteger' ? 'number' : secret ? 'password' : 'text';
                if (input.type === 'number') {
                    input.step = '1'; input.required = true;
                    if (resource.min !== null) input.min = resource.min;
                    if (resource.max !== null) input.max = resource.max;
                }
            }
            let booleanState;
            if (booleanOptions) {
                input.setAttribute('role', 'switch');
                const controls = document.createElement('span'); controls.className = 'resource-boolean';
                const toggle = document.createElement('span'); toggle.className = 'resource-switch';
                const slider = document.createElement('span'); slider.className = 'slider'; slider.setAttribute('aria-hidden', 'true');
                booleanState = document.createElement('span'); booleanState.setAttribute('aria-hidden', 'true');
                toggle.append(input, slider); controls.append(toggle, booleanState); label.append(controls);
            } else label.append(input);
            const meta = document.createElement('div'); meta.className = 'resource-meta';
            meta.textContent = resource.hint ? resource.hint.replace(/<br\s*\/?>/gi, '\n').replace(/<[^>]*>/g, '')
                : !resource.editable ? 'Read-only in BAS; XML settings are preserved.' : '';
            input.required = !booleanOptions && (input.required || !!resource.required);
            const footer = document.createElement('div'); footer.className = 'resource-footer';
            const mode = document.createElement('span'); mode.className = 'resource-mode';
            const reset = document.createElement('button'); reset.type = 'button'; reset.textContent = 'Use XML default';
            reset.dataset.readonly = String(!resource.editable);
            const update = () => {
                const overridden = Object.hasOwn(values, resource.name);
                row.classList.toggle('overridden', overridden);
                input.value = overridden ? (values[resource.name] ?? '') : resource.defaultValue;
                if (booleanOptions) {
                    input.checked = String(input.value).toLowerCase() === 'true';
                    booleanState.textContent = input.checked ? 'On' : 'Off';
                }
                mode.textContent = overridden ? 'Override' : 'XML default'; reset.hidden = !overridden;
            };
            input.oninput = () => {
                try { values = this.values(); } catch (error) { this.status.textContent = error.message; return; }
                const value = booleanOptions ? resource.options.find(value => value.toLowerCase() === String(input.checked))
                    : resource.type === 'FixedInteger' && /^-?\d+$/.test(input.value) ? Number(input.value) : input.value;
                Object.defineProperty(values, resource.name, { value, writable: true, enumerable: true, configurable: true });
                this.json.value = JSON.stringify(values, null, 2);
                row.classList.add('overridden'); mode.textContent = 'Override'; reset.hidden = false;
                if (booleanOptions) booleanState.textContent = input.checked ? 'On' : 'Off';
                this.updateVisibility(values);
            };
            reset.onclick = () => {
                try { values = this.values(); } catch (error) { this.status.textContent = error.message; return; }
                delete values[resource.name]; this.json.value = JSON.stringify(values, null, 2); update(); this.updateVisibility(values);
            };
            update(); footer.append(mode, reset); row.append(label, meta, footer); this.container.append(row);
        }
        this.setDisabled(this.disabled);
        this.updateVisibility(values);
        if (unknown.length) this.status.textContent += ' Unknown overrides: ' + unknown.join(', ');
    }
};
