const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const test = require('node:test');

function context() {
    const ctx = vm.createContext({
        currentSections: [
            { title: 'CPU SUMMARY', pairs: [{ k: 'Load', v: '30%' }, { k: 'Logical CPUs', v: '8' }, { k: 'Sample seconds', v: '0.5' }] },
            { title: 'ALL PROCESSES', headers: ['PID', 'NAME', 'CPU_PCT'], rows: [
                ['5', 'python', '2.500'], ['9', 'python', '12.500'], ['11', 'python', 'n/a']
            ] }
        ],
        esc: value => String(value).replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('"', '&quot;'),
        jsStr: value => String(value).replaceAll("'", "\\'")
    });
    for (const file of ['system-memory.js', 'system-cpu.js'])
        vm.runInContext(fs.readFileSync('wwwroot/js/' + file, 'utf8'), ctx);
    return ctx;
}

test('CPU treemap ranks sampled load and excludes idle or unreadable activity', () => {
    const ctx = context();
    const rows = [['chrome', '5', '4', '0'], ['python', '15', '3', '1'],
        ['idle', '0', '1', '0'], ['unknown', 'n/a', '1', '1']];
    const html = ctx.renderCpuUsage(rows);
    assert.equal(vm.runInContext('cpuTiles[0].name', ctx), 'python');
    assert.equal(vm.runInContext('cpuTiles.length', ctx), 2);
    assert.match(html, /20.00%/);
    assert.match(html, /75.0% of shown workload/);
    assert.match(html, /System load: 30%/);
    assert.ok(Math.abs(vm.runInContext('cpuTiles[0].width * cpuTiles[0].height / 420000', ctx) - 0.75) < 1e-9);
});

test('group details sort PIDs by current CPU and preserve unavailable measurements', () => {
    const ctx = context();
    ctx.renderCpuUsage([['python', '15', '3', '1']]);
    vm.runInContext("cpuSelected = 'python'", ctx);
    const html = ctx.renderCpuSelection();
    assert.ok(html.indexOf('PID 9') < html.indexOf('PID 5'));
    assert.match(html, /PID 11 · CPU Unavailable/);
    assert.match(html, /total is incomplete/);
});

test('empty CPU filter has an explicit state and never invents load', () => {
    const ctx = context();
    const html = ctx.renderCpuUsage([]);
    assert.match(html, /No measured CPU activity matches/);
    assert.doesNotMatch(html, /NaN|Infinity|class="memory-treemap/);
});
