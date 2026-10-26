const test = require('node:test'), assert = require('node:assert/strict');
const fs = require('node:fs'), vm = require('node:vm');
function setup() {
    const elements = { scheduleActionStatus: { style: {} }, scheduleToggle: {} }, errors = [], calls = [];
    const c = { console, window: { addEventListener() {} },
        document: { addEventListener() {}, getElementById: id => elements[id] },
        setInterval() {}, clearInterval() {}, setTimeout, clearTimeout, URLSearchParams,
        fetch: async () => ({ ok: true, json: async () => ({ Tasks: {} }) }) };
    vm.createContext(c); vm.runInContext(fs.readFileSync('wwwroot/js/tasker.js', 'utf8'), c);
    c.Dialog = { error: msg => errors.push(msg) };
    c.schedules = ['a', 'b', 'c'].map(id => ({ id, name: id, enabled: 'true', schedule_mode: 'daily', _revision: 'r1' }));
    c.selectedId = 'a'; c.selectedIds = new Set(['a', 'b', 'c']);
    c.loadList = async () => {};
    c.taskerFetch = async (_url, opts) => { calls.push(JSON.parse(opts.body)); return new Response('{"ok":true}'); };
    return { c, elements, errors, calls };
}
test('pause shows progress immediately and prevents duplicate submissions', async () => {
    const { c, elements, calls } = setup(); let release;
    c.taskerFetch = async (_url, opts) => { calls.push(JSON.parse(opts.body)); await new Promise(r => release = r); return new Response('{"ok":true}'); };
    const operation = c.toggleEnabledSelected();
    assert.match(elements.scheduleActionStatus.textContent, /1\/3.*a/);
    assert.equal(elements.scheduleToggle.disabled, true);
    await c.toggleEnabledSelected(); assert.equal(calls.length, 1);
    release(); await new Promise(setImmediate); release(); await new Promise(setImmediate); release();
    await operation;
    assert.equal(elements.scheduleToggle.disabled, false);
    assert.match(elements.scheduleActionStatus.textContent, /3\/3.*confirmed/);
    assert.match(elements.scheduleActionStatus.textContent, /Interrupt/);
    assert.deepEqual(calls[0], { id: 'a', enabled: 'false', _revision: 'r1' });
});
test('remote timeout stops the batch and survives failed list refresh', async () => {
    const { c, elements, errors, calls } = setup();
    c.window.TaskerBridge = { remote: () => true, context: () => ({}), isCurrent: () => true };
    c.taskerFetch = async (_url, opts) => { calls.push(JSON.parse(opts.body)); return new Response('{"error":"Server did not respond in time"}', { status: 504 }); };
    c.loadList = async () => { throw new Error('refresh timeout'); };
    await c.toggleEnabledSelected();
    assert.equal(calls.length, 1);
    assert.match(elements.scheduleActionStatus.textContent, /Server did not respond/);
    assert.match(elements.scheduleActionStatus.textContent, /2 not sent/);
    assert.match(elements.scheduleActionStatus.textContent, /Check commands/);
    assert.equal(errors.length, 1);
});
test('single pause checks errors and sends only changed fields', async () => {
    const { c, calls, errors } = setup();
    c.taskerFetch = async (_url, opts) => { calls.push(JSON.parse(opts.body)); return new Response('{"error":"Revision conflict"}', { status: 409 }); };
    await c.toggleEnabled('a', 'true');
    assert.deepEqual(calls, [{ id: 'a', enabled: 'false', _revision: 'r1' }]);
    assert.match(errors[0], /Revision conflict/);
});

test('unknown outcome is not success and does not send the rest of the batch', async () => {
    const { c, elements, calls } = setup();
    c.window.TaskerBridge = { remote: () => true, context: () => ({}), isCurrent: () => true };
    c.taskerFetch = async (_url, opts) => { calls.push(JSON.parse(opts.body)); return new Response('{"ok":false,"error":"Command result is unknown"}', { status: 202 }); };
    await c.toggleEnabledSelected();
    assert.equal(calls.length, 1);
    assert.match(elements.scheduleActionStatus.textContent, /0\/3 confirmed/);
    assert.match(elements.scheduleActionStatus.textContent, /Command result is unknown/);
});
test('node switch suppresses late progress and further mutations', async () => {
    const { c, elements, calls, errors } = setup(); let node = 'A', release;
    c.window.TaskerBridge = { remote: () => true, context: () => ({ node }), isCurrent: ctx => ctx.node === node };
    c.taskerFetch = async (_url, opts) => { calls.push(JSON.parse(opts.body)); await new Promise(r => release = r); return new Response('{"ok":true}'); };
    const operation = c.toggleEnabledSelected();
    node = 'B'; elements.scheduleActionStatus.textContent = 'New node';
    release(); await operation;
    assert.equal(calls.length, 1); assert.equal(errors.length, 0);
    assert.equal(elements.scheduleActionStatus.textContent, 'New node');
});
test('a rejected task does not hide confirmation of the remaining tasks', async () => {
    const { c, elements, calls } = setup();
    c.taskerFetch = async (_url, opts) => {
        calls.push(JSON.parse(opts.body));
        return calls.length === 1 ? new Response('{"error":"Revision conflict"}', { status: 409 }) : new Response('{"ok":true}');
    };
    await c.toggleEnabledSelected();
    assert.equal(calls.length, 3);
    assert.match(elements.scheduleActionStatus.textContent, /2\/3 confirmed/);
    assert.match(elements.scheduleActionStatus.textContent, /a: Revision conflict/);
});
