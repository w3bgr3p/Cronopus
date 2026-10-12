const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const test = require('node:test');

function panel(fetch) {
    const elements = new Map();
    function element() {
        const classes = new Set();
        return { value: '', textContent: '', innerHTML: '', hidden: false, style: {}, dataset: {}, children: [], events: {},
            classList: { add: v => classes.add(v), remove: v => classes.delete(v),
                toggle: (v, on) => on ? classes.add(v) : classes.delete(v), contains: v => classes.has(v) },
            addEventListener(name, fn) { this.events[name] = fn; }, appendChild(child) { this.children.push(child); },
            querySelectorAll() { return []; } };
    }
    const document = {
        getElementById(id) { if (!elements.has(id)) elements.set(id, element()); return elements.get(id); },
        querySelector() { return this.getElementById('statusMessage'); },
        querySelectorAll() { return this.getElementById('tableList').children; },
        createElement: element, createDocumentFragment: element, addEventListener() {}
    };
    const ctx = vm.createContext({ document, fetch, URLSearchParams, console, setTimeout,
        AiPanel: { setContext(fn) { this.context = fn; } } });
    const html = fs.readFileSync('wwwroot/sqlite.html', 'utf8');
    const script = [...html.matchAll(/<script>([\s\S]*?)<\/script>/g)][0][1];
    vm.runInContext(script.replace(/\n\s*restoreFile\(\);/, ''), ctx);
    return { ctx, document, run: code => vm.runInContext(code, ctx) };
}
const response = data => ({ ok: true, json: async () => data });
const tables = { tables: [{ schema: 'viewer.test', name: 'odd"table' }], database: 'postgres', host: 'localhost', port: 5432 };

test('PostgreSQL connects using POST body and quotes schema/table separately', async () => {
    const calls = [];
    const p = panel(async (url, init) => { calls.push({ url, init }); return response(tables); });
    p.run('switchSource("postgres")');
    p.document.getElementById('pgConnection').value = 'Host=localhost;Password=private-value;Database=postgres';
    await p.run('connectPostgres()');
    assert.equal(calls[0].url, '/postgres-viewer/tables');
    assert.equal(JSON.parse(calls[0].init.body).connectionString, 'Host=localhost;Password=private-value;Database=postgres');
    assert.equal(p.document.getElementById('tableList').children[0].textContent, 'viewer.test.odd"table');
    assert.doesNotMatch(p.ctx.AiPanel.context(), /private-value|Password=/);
    p.run('execQuery = async sql => { globalThis.selectedSql = sql; }');
    p.document.getElementById('tableList').children[0].onclick();
    assert.equal(p.ctx.selectedSql, 'SELECT * FROM "viewer.test"."odd""table" LIMIT 1000');
});

test('late PostgreSQL connection cannot replace SQLite after switching sources', async () => {
    let resolve;
    const p = panel(() => new Promise(r => { resolve = r; }));
    p.run('switchSource("postgres")');
    p.document.getElementById('pgConnection').value = 'Host=localhost';
    const pending = p.run('connectPostgres()');
    p.run('switchSource("sqlite")');
    resolve(response(tables));
    await pending;
    assert.equal(p.run('postgresConnection'), '');
    assert.equal(p.document.getElementById('fileLabel').textContent, 'No file selected');
    assert.equal(p.document.getElementById('pgConnection').value, '');
});

test('SQLite file storage finishing after a source switch cannot reload PostgreSQL', async () => {
    const calls = [];
    const p = panel(async url => { calls.push(url); return response(tables); });
    let finishStorage;
    p.ctx.storeFile = () => new Promise(resolve => { finishStorage = resolve; });
    p.run('sqliteFileState = storeFile');
    const picker = p.document.getElementById('filePicker');
    picker.files = [{ name: 'sample.db' }];
    const pending = picker.events.change.call(picker);
    p.run('switchSource("postgres")');
    finishStorage();
    await pending;
    assert.deepEqual(calls, []);
    assert.equal(p.document.getElementById('fileLabel').textContent, 'Not connected');
});
