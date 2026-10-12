const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const vm = require('node:vm');

function el() {
    const classes = new Set();
    return {
        innerHTML: '', textContent: '', value: '', style: {},
        classList: {
            add(c) { classes.add(c); },
            remove(c) { classes.delete(c); },
            toggle(c, on) { if (on) classes.add(c); else classes.delete(c); },
            contains(c) { return classes.has(c); },
        },
        querySelectorAll() { return []; },
        appendChild() {},
        focus() {},
    };
}

const MODAL_IDS = ['payloadOverlay', 'payloadTitle', 'payloadImport', 'payloadTabValues', 'payloadTabSchema',
    'valuesBody', 'schemaBody', 'payloadPreview', 'importPayloadInput', 'detailActions'];

function loadTasker({ inputs = [] } = {}) {
    const elements = Object.fromEntries(MODAL_IDS.map((id) => [id, el()]));
    const requests = [];
    const context = {
        console,
        document: {
            addEventListener() {},
            getElementById(id) { return elements[id] || null; },
            querySelectorAll(selector) { return selector === '#valuesBody [data-vkey]' ? inputs : []; },
        },
        window: { addEventListener() {} },
        navigator: { clipboard: { writeText: async () => {} } },
        fetch: async (url, opts) => {
            requests.push({ url, opts });
            const body = url.startsWith('/tasker/payload?')
                ? { schema: '[{"key":"url","type":"text"}]', values: '{"url":"old"}' }
                : { ok: true, command: 'python a.py --url old', skipped: [] };
            return { ok: true, json: async () => body };
        },
        setInterval() { return 1; },
        clearInterval() {},
        setTimeout,
        clearTimeout,
        URLSearchParams,
    };
    vm.createContext(context);
    const sourcePath = path.join(__dirname, '..', 'wwwroot', 'js', 'tasker.js');
    vm.runInContext(fs.readFileSync(sourcePath, 'utf8'), context, { filename: sourcePath });
    return { context, elements, requests };
}

test('task actions show one Payload button instead of five', () => {
    const { context, elements } = loadTasker();

    context.renderDetailActions({ id: 't1', name: 'n', executor: 'exe', script_path: 'a.exe' });

    const html = elements.detailActions.innerHTML;
    assert.match(html, /openPayloadModal\('t1'/);
    assert.doesNotMatch(html, /openSchemaModal|openValuesModal|openImportPayload|exportPayload\(|clearPayload\(/);
});

test('switching to Schema keeps values typed on the Values tab', async () => {
    const inputs = [{ dataset: { vkey: 'url' }, type: 'text', value: 'typed' }];
    const { context, elements } = loadTasker({ inputs });

    await context.openPayloadModal('t1', 'n');
    context.pmSwitch('schema');

    assert.equal(context.pmValues.url, 'typed');
    assert.equal(elements.valuesBody.style.display, 'none');
    assert.equal(elements.schemaBody.style.display, '');
    assert.ok(elements.payloadTabSchema.classList.contains('active'));
});

test('Save sends schema and values in one request and closes the modal', async () => {
    const inputs = [{ dataset: { vkey: 'url' }, type: 'text', value: 'typed' }];
    const { context, elements, requests } = loadTasker({ inputs });

    await context.openPayloadModal('t1', 'n');
    await context.savePayload();

    const save = requests.find((r) => r.url === '/tasker/payload' && r.opts && r.opts.method === 'POST');
    assert.ok(save, 'no POST /tasker/payload');
    const body = JSON.parse(save.opts.body);
    assert.equal(body.id, 't1');
    assert.equal(JSON.parse(body.schema)[0].key, 'url');
    assert.equal(JSON.parse(body.values).url, 'typed');
    assert.ok(!elements.payloadOverlay.classList.contains('open'));
});

test('Save updates the task payload immediately while preserving unsaved Settings', async () => {
    const { context } = loadTasker({ inputs: [{ dataset: { vkey: 'url' }, type: 'text', value: 'new' }] });
    context.schedules = [{ id: 't1', payload_values: '{"url":"old"}' }];
    context.selectedId = 't1';
    context.taskDraft = { id: 't1', name: 'Unsaved name', payload_values: '{"url":"old"}' };
    context.formDirty = true;
    await context.openPayloadModal('t1', 'n');
    await context.savePayload();
    assert.equal(JSON.parse(context.schedules[0].payload_values).url, 'new');
    assert.equal(JSON.parse(context.taskDraft.payload_values).url, 'new');
    assert.equal(context.taskDraft.name, 'Unsaved name');
    assert.equal(context.formDirty, true);
});

test('Save preserves all selected multiselect values', async () => {
    const { context, requests } = loadTasker({ inputs: [{ dataset: { vkey: 'tags' }, type: 'select-multiple',
        multiple: true, value: 'a', selectedOptions: [{ value: 'a' }, { value: 'b' }] }] });
    await context.openPayloadModal('t1', 'n');
    await context.savePayload();
    const body = JSON.parse(requests.find(r => r.url === '/tasker/payload' && r.opts).opts.body);
    assert.equal(JSON.parse(body.values).tags, 'a,b');
});

test('failed persistence keeps Payload open and leaves the saved task unchanged', async () => {
    const { context, elements } = loadTasker();
    const errors = [];
    context.Dialog = { error(message) { errors.push(message); } };
    context.schedules = [{ id: 't1', payload_values: '{"url":"old"}' }];
    await context.openPayloadModal('t1', 'n');
    context.fetch = async () => ({ ok: false, json: async () => ({ ok: false, error: 'Database is read-only' }) });
    context.pmValues.url = 'new';
    await context.savePayload();
    assert.ok(elements.payloadOverlay.classList.contains('open'));
    assert.equal(JSON.parse(context.schedules[0].payload_values).url, 'old');
    assert.deepEqual(errors, ['Database is read-only']);
});

test('Execution command requests saved payload and ignores stale responses after switching tasks', async () => {
    const { context, elements, requests } = loadTasker();
    elements.executionCommand = el();
    context.selectedId = 't1';
    context.activeTab = 'execution';
    await context.refreshExecutionCommand('t1');
    assert.equal(elements.executionCommand.textContent, 'python a.py --url old');
    const body = JSON.parse(requests.find(r => r.url === '/tasker/args-preview').opts.body);
    assert.deepEqual(body, { id: 't1' });
    let finish;
    context.fetch = () => new Promise(resolve => { finish = resolve; });
    const pending = context.refreshExecutionCommand('t1');
    context.selectedId = 't2';
    elements.executionCommand = el();
    finish({ json: async () => ({ ok: true, command: 'stale' }) });
    await pending;
    assert.equal(elements.executionCommand.textContent, '');
});

test('Import fills the form and does not save by itself', async () => {
    const { context, elements, requests } = loadTasker();

    await context.openPayloadModal('t1', 'n');
    elements.importPayloadInput.value = JSON.stringify({
        schema: JSON.stringify([{ key: 'proxy', type: 'text' }]),
        values: JSON.stringify({ proxy: 'p' }),
    });
    context.applyPayloadImport();

    assert.equal(context.pmSchema[0].key, 'proxy');
    assert.equal(context.pmValues.proxy, 'p');
    assert.ok(!requests.some((r) => r.url === '/tasker/payload' && r.opts && r.opts.method === 'POST'));
});

test('script parameters become schema fields that round-trip to the same flags', () => {
    const { context } = loadTasker();
    const params = [
        { names: ['--proxy-url'], help: 'Proxy', default: null },
        { names: ['--dry'], action: 'store_true', default: false },
        { names: ['--mode'], choices: ['fast', 'slow'], default: 'fast' },
        { names: ['--acc_id'], default: 7 },
        { names: ['target'], positional: true },
        { names: ['-n'] },
        { names: [], exprs: { names: ['name'] } },
        { names: ['--x'], subcommand: 'run' },
        { names: ['--no-cache'], action: 'store_false' },
        { names: ['--dryRun'] },
        { names: ['--url'] },
        { names: ['-h', '--help'] },
    ];

    const r = context.schemaFromScriptParams([{ key: 'url', type: 'text' }], { url: 'keep' }, params);

    assert.deepEqual([...r.added], ['--proxy-url', '--dry', '--mode', '--acc_id']);
    assert.deepEqual([...r.schema.map((f) => f.key)], ['url', 'proxyUrl', 'dry', 'mode', 'acc_id']);
    assert.deepEqual([...r.schema.map((f) => f.type)], ['text', 'text', 'boolean', 'select', 'text']);
    assert.equal(r.schema[3].options, 'fast,slow');
    assert.equal(r.schema[1].label, 'Proxy');
    assert.equal(r.values.url, 'keep');
    assert.equal(r.values.dry, 'false');
    assert.equal(r.values.mode, 'fast');
    assert.equal(r.values.acc_id, '7');
    assert.equal(r.values.proxyUrl, undefined);
    assert.equal(r.skipped.length, 7);
    // Без значения по умолчанию: пусто значит «не задано», флаг не передаётся.
    assert.equal(r.schema[1].skipEmpty, true);
    assert.ok(!r.schema[2].skipEmpty, 'boolean needs no skipEmpty');
    assert.ok(!r.schema[3].skipEmpty, 'field with a default keeps passing it');
});

test('an append parameter with an empty list default is skipped when empty', () => {
    const { context } = loadTasker();

    const r = context.schemaFromScriptParams([], {}, [
        { names: ['--url'], action: 'append', default: [] },
        { names: ['--tag'], action: 'append', default: ['a', 'b'] },
    ]);

    assert.equal(r.schema[0].skipEmpty, true);
    assert.equal(r.values.url, '');
    assert.ok(!r.schema[1].skipEmpty);
    assert.equal(r.values.tag, 'a,b');
});

test('schema rows offer Skip empty for value fields only', () => {
    const { context, elements } = loadTasker();
    context.pmSchema = [
        { key: 'out', type: 'text', skipEmpty: true },
        { key: 'dry', type: 'boolean' },
        { key: '', type: 'section', label: 'S' },
        { key: 'mode', type: 'select', options: 'a,b' },
    ];

    context.renderConstructor();

    const rows = elements.schemaBody.innerHTML.split('class="schema-field-row"').slice(1);
    assert.equal(rows.length, 4);
    assert.match(rows[0], /type="checkbox"[^>]*checked[^>]*pmSchemaUpdate\(0,'skipEmpty',this\.checked\)/);
    assert.doesNotMatch(rows[1], /skipEmpty/);
    assert.doesNotMatch(rows[2], /skipEmpty/);
    assert.match(rows[3], /pmSchemaUpdate\(3,'skipEmpty',this\.checked\)/);
    assert.doesNotMatch(rows[3], /checked onchange/);
});

test('From script fills the schema from script-params without saving', async () => {
    const { context, requests } = loadTasker();
    const alerts = [];
    context.Dialog = { alert(msg) { alerts.push(msg); }, error(msg) { alerts.push('ERR ' + msg); } };
    context.fetch = async (url, opts) => {
        requests.push({ url, opts });
        const body = url.startsWith('/tasker/script-params?')
            ? { ok: true, params: [{ names: ['--url'] }, { names: ['--headless'], action: 'store_true' }], uses_sys_argv: false }
            : { ok: true, command: '' };
        return { ok: true, json: async () => body };
    };
    context.pmScheduleId = 't1';
    context.pmSchema = [];
    context.pmValues = {};

    await context.importSchemaFromScript();

    assert.ok(requests.some((r) => r.url === '/tasker/script-params?id=t1'));
    assert.deepEqual([...context.pmSchema.map((f) => f.key)], ['url', 'headless']);
    assert.ok(!requests.some((r) => r.url === '/tasker/payload' && r.opts && r.opts.method === 'POST'));
    assert.match(alerts[0], /Added: --url --headless/);
});

test('From script shows the server error verbatim', async () => {
    const { context } = loadTasker();
    const alerts = [];
    context.Dialog = { alert(msg) { alerts.push(msg); }, error(msg) { alerts.push('ERR ' + msg); } };
    context.fetch = async () => ({ ok: true, json: async () => ({ ok: false, step: 'executor', error: 'executor is node, not python' }) });
    context.pmScheduleId = 't1';
    context.pmSchema = [];

    await context.importSchemaFromScript();

    assert.deepEqual(alerts, ['ERR [executor] executor is node, not python']);
    assert.equal(context.pmSchema.length, 0);
});
