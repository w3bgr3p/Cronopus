const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const vm = require('node:vm');

function load() {
    const requests = [], timers = new Map();
    let elements = {}, nextTimer = 0;
    const body = {};
    Object.defineProperty(body, 'innerHTML', { set(html) {
        elements = {};
        for (const [, id] of html.matchAll(/id="([^"]+)"/g))
            elements[id] = { innerHTML: '', textContent: '', style: {} };
    } });
    const c = { console, document: { addEventListener() {},
        getElementById: id => id === 'detailBody' ? body : elements[id] || null },
        window: { addEventListener() {} },
        setInterval(fn) { const id = ++nextTimer; timers.set(id, fn); return id; },
        clearInterval(id) { timers.delete(id); }, setTimeout() {}, clearTimeout() {},
        fetch(url) { return new Promise(resolve => requests.push({ url,
            reply(data) { resolve({ json: async () => data }); } })); },
    };
    vm.createContext(c);
    vm.runInContext(fs.readFileSync(path.join(__dirname, '../wwwroot/js/tasker.js'), 'utf8'), c);
    c.triggerLabel = () => 'daily'; c.taskMemText = () => ''; c.scheduleHeld = () => false;
    c.refreshExecutionCommand = () => {};
    function render(id, threads = 4) {
        c.selectedId = id; c.activeTab = 'execution';
        c.renderExecution({ id, status: 'running', max_threads: String(threads) });
    }
    return { c, requests, timers, render, el: id => elements[id] };
}
const flush = () => new Promise(setImmediate);
const instances = n => Array.from({ length: n }, (_, i) => ({ runId: 'run' + i, uptimeSec: '1', memoryMB: '50' }));

test('late instances from another task cannot overwrite the four current threads', async () => {
    const h = load();
    h.render('other'); const old = h.requests.find(r => r.url.includes('/instances'));
    h.render('current');
    h.requests.find(r => r.url.endsWith('id=current') && r.url.includes('/instances')).reply(instances(4));
    await flush(); old.reply(instances(69)); await flush();
    assert.equal(h.el('instancesCount').textContent, '4');
    assert.equal((h.el('instancesList').innerHTML.match(/inst-row/g) || []).length, 4);
    assert.equal(h.c._instCache.other, undefined);
});

test('responses from an earlier render of the same task cannot replace current instances', async () => {
    const h = load(); h.render('current');
    const old = h.requests.find(r => r.url.includes('/instances'));
    h.render('current');
    h.requests.filter(r => r.url.includes('/instances')).at(-1).reply(instances(4));
    await flush(); old.reply(instances(2)); await flush();
    assert.equal(h.el('instancesCount').textContent, '4');
});

test('late process stats cannot change the new task or stop its polling', async () => {
    const h = load(); h.render('other', 1);
    const old = h.requests.find(r => r.url.includes('/process-stats'));
    h.render('current', 1); const timer = h.c._procStatsPoll;
    old.reply({ running: false }); await flush();
    assert.equal(h.c._procStatsPoll, timer);
    assert.ok(h.timers.has(timer));
    assert.equal(h.el('procPid').textContent, '');
});

test('late queue response cannot populate another task queue', async () => {
    const h = load(); h.render('other');
    const old = h.requests.find(r => r.url.includes('/queue'));
    h.render('current');
    h.requests.filter(r => r.url.includes('/queue')).at(-1).reply([]);
    await flush(); old.reply([{ status: 'pending', origin: 'manual' }]); await flush();
    assert.equal(h.el('queueCard').style.display, 'none');
    assert.equal(h.el('queueList').textContent, '');
});
