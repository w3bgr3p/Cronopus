const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const vm = require('node:vm');

function element(run = '') {
    const classes = new Set();
    const e = { children: [], style: { setProperty() {} }, textContent: '',
        classList: { add(c) { classes.add(c); }, remove(c) { classes.delete(c); },
            toggle(c, on) { if (on) classes.add(c); else classes.delete(c); },
            contains(c) { return classes.has(c); } },
        setAttribute(k, v) { this[k] = v; }, getAttribute(k) { return this[k] || ''; },
        appendChild(c) { this.children.push(c); c.parent = this; },
        removeChild(c) { this.children.splice(this.children.indexOf(c), 1); },
        querySelectorAll() { return this.children; }, querySelector() { return null; },
        insertAdjacentHTML(_, d) { const line = element(d.run); line.textContent = d.line; this.appendChild(line); },
    };
    e['data-run'] = run;
    Object.defineProperties(e, {
        innerHTML: { set() { this.children = []; } },
        firstElementChild: { get() { return this.children[0]; } },
        lastElementChild: { get() { return this.children.at(-1); } },
        outerHTML: { set(d) { const line = element(d.run); line.textContent = d.line;
            line.parent = this.parent; this.parent.children[this.parent.children.indexOf(this)] = line; } },
    });
    return e;
}

function load() {
    const box = element(), chips = element();
    const c = { console, document: { addEventListener() {}, createElement: () => element(),
        getElementById: id => ({ outputBox: box, runChips: chips })[id] || null },
        window: { addEventListener() {} }, setInterval() {}, clearInterval() {},
        setTimeout() {}, clearTimeout() {},
        LogLine: { build: d => d, plain: s => s, hue: () => 1 },
        EventSource: function() { this.close = () => {}; this.addEventListener = () => {}; },
        fetch: async () => ({ json: async () => ({ output: 'saved history' }) }),
    };
    vm.createContext(c);
    vm.runInContext(fs.readFileSync(path.join(__dirname, '../wwwroot/js/tasker.js'), 'utf8'), c);
    return { c, box, chips };
}

test('incoming buffer clear preserves the selected thread and hides other threads', () => {
    const { c, box, chips } = load();
    c._noteRun('chosen'); c.toggleRunFilter('chosen');
    c._putOutputLine(box, { clear: true, run: 'other', line: 'new log' });
    assert.equal(c._runFilter, 'chosen');
    assert.ok(box.lastElementChild.classList.contains('ll-muted'));
    assert.ok(chips.children.find(x => x.getAttribute('data-run') === 'chosen').classList.contains('on'));
    c._putOutputLine(box, { run: 'chosen', line: 'chosen log' });
    assert.ok(!box.lastElementChild.classList.contains('ll-muted'));
});

test('chip limit preserves selected oldest thread until the user deselects it', () => {
    const { c, chips } = load();
    c._noteRun('chosen'); c.toggleRunFilter('chosen');
    for (let i = 0; i < 30; i++) c._noteRun('run' + i);
    assert.equal(chips.children.length, c.RUN_CHIP_LIMIT);
    assert.equal(c._runFilter, 'chosen');
    assert.ok(chips.children.some(x => x.getAttribute('data-run') === 'chosen'));
    c.toggleRunFilter('chosen');
    assert.equal(c._runFilter, null);
});

test('replacement progress line remains hidden for an unselected thread', () => {
    const { c, box } = load();
    c.toggleRunFilter('chosen');
    c._putOutputLine(box, { run: 'other', line: 'progress' });
    c._putOutputLine(box, { run: 'other', line: 'updated', replace_last: true });
    assert.ok(box.lastElementChild.classList.contains('ll-muted'));
});

test('same-task subscription refresh keeps selection, switching tasks resets it', () => {
    const { c } = load();
    c.startSseOutput('task1');
    c._noteRun('chosen'); c.toggleRunFilter('chosen');
    c.startSseOutput('task1');
    assert.equal(c._runFilter, 'chosen');
    c.startSseOutput('task2');
    assert.equal(c._runFilter, null);
});

test('a late saved-history response does not replace filtered streaming output', async () => {
    const { c, box } = load();
    c.selectedId = 'task1';
    c.loadOutput('task1');
    c.toggleRunFilter('chosen');
    c._putOutputLine(box, { run: 'chosen', line: 'live log' });
    await new Promise(setImmediate);
    assert.equal(c._runFilter, 'chosen');
    assert.equal(box.lastElementChild.textContent, 'live log');
});
