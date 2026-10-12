const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const test = require('node:test');

function context(values) {
    const stat = {};
    const ctx = vm.createContext({
        currentSections: [{ title: 'SYSTEM MEMORY SUMMARY',
            pairs: Object.entries(values).map(([k, v]) => ({ k, v: String(v) })) }],
        document: { getElementById: () => stat },
        esc: text => String(text).replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('"', '&quot;')
    });
    vm.runInContext(fs.readFileSync('wwwroot/js/system-memory.js', 'utf8'), ctx);
    return { ctx, stat };
}

test('commit exhaustion warns even when most physical RAM is available', () => {
    const { ctx, stat } = context({ Total: 64, Used: 24, Free: 40,
        'Commit used bytes': 71.9 * 1073741824, 'Commit limit bytes': 72 * 1073741824 });
    assert.equal(ctx.memoryPressure(ctx.memoryAllocationValues()), 'critical');
    const html = ctx.renderMemoryAllocation();
    assert.match(html, /New allocations can fail even with free RAM/);
    assert.match(html, /40.00 GiB/);
    assert.match(html, /Unpromised headroom/);
    ctx.populateCommitStat();
    assert.match(stat.className, /critical/);
    assert.match(stat.title, /102.4 MiB/);
});

test('high RAM usage alone does not imply system commit exhaustion', () => {
    const { ctx } = context({ Total: 64, Used: 63, Free: 1,
        'Commit used bytes': 63 * 1073741824, 'Commit limit bytes': 96 * 1073741824 });
    assert.equal(ctx.memoryPressure(ctx.memoryAllocationValues()), 'normal');
});

test('missing commit measurements are unavailable rather than zero or inferred from RAM', () => {
    const { ctx, stat } = context({ Total: 64, Used: 20, Free: 44 });
    assert.equal(ctx.memoryPressure(ctx.memoryAllocationValues()), 'unavailable');
    ctx.populateCommitStat();
    assert.equal(stat.textContent, 'Unavailable');
    assert.match(ctx.renderMemoryAllocation(), /measurement unavailable/);
    assert.doesNotMatch(ctx.renderMemoryAllocation(), /NaN|Infinity/);
});

test('commit warning transitions at 90 and 97 percent', () => {
    const { ctx } = context({});
    for (const [used, expected] of [[89.9, 'normal'], [90, 'warning'], [96.9, 'warning'], [97, 'critical']])
        assert.equal(ctx.memoryPressure({ committed: used, limit: 100 }), expected);
});

test('treemap area matches allocation share without overlap', () => {
    const { ctx } = context({});
    const tiles = ctx.memoryLayout([{ name: 'a', value: 70 }, { name: 'b', value: 20 }, { name: 'c', value: 10 }]);
    for (const tile of tiles) {
        assert.ok(Math.abs(tile.width * tile.height / 420000 - tile.value / 100) < 1e-10);
        assert.ok(tile.x >= 0 && tile.y >= 0 && tile.x + tile.width <= 1000.00001 && tile.y + tile.height <= 420.00001);
    }
    for (let i = 0; i < tiles.length; i++) for (let j = i + 1; j < tiles.length; j++) {
        const a = tiles[i], b = tiles[j];
        const overlap = Math.max(0, Math.min(a.x + a.width, b.x + b.width) - Math.max(a.x, b.x)) *
            Math.max(0, Math.min(a.y + a.height, b.y + b.height) - Math.max(a.y, b.y));
        assert.ok(overlap < 1e-8);
    }
});

test('treemap changes ranking between private allocation and RAM and omits zero values', () => {
    const { ctx } = context({});
    const rows = [['chrome', '100', '20', '2', '0'], ['python', '25', '200', '1', '0'], ['empty', '0', '0', '1', '0']];
    ctx.renderMemoryTreemap(rows);
    assert.equal(vm.runInContext('memoryTiles[0].name', ctx), 'chrome');
    vm.runInContext("memoryMetric = 'RAM_MB'", ctx);
    ctx.renderMemoryTreemap(rows);
    assert.equal(vm.runInContext('memoryTiles[0].name', ctx), 'python');
    assert.equal(vm.runInContext('memoryTiles.length', ctx), 2);
    assert.match(ctx.renderMemoryTreemap([]), /No measured processes/);
});
