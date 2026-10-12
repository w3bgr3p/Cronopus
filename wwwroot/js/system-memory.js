// System commit is independent of resident RAM. Values come from the live snapshot.
function memoryAllocationValues() {
    const pairs = currentSections.find(s => s.title === 'SYSTEM MEMORY SUMMARY')?.pairs || [];
    const value = key => {
        const raw = pairs.find(p => p.k === key)?.v;
        return raw === undefined ? null : Number.parseFloat(raw);
    };
    return { total: value('Total'), used: value('Used'), free: value('Free'),
        committed: value('Commit used bytes'), limit: value('Commit limit bytes'),
        peak: value('Commit peak bytes'), paged: value('Paged pool bytes'),
        nonpaged: value('Nonpaged pool bytes') };
}

function memorySize(bytes) {
    if (!Number.isFinite(bytes)) return 'Unavailable';
    return bytes < 1073741824
        ? (bytes / 1048576).toFixed(1) + ' MiB'
        : (bytes / 1073741824).toFixed(2) + ' GiB';
}

function memoryPressure(m) {
    if (!Number.isFinite(m.committed) || !(m.limit > 0)) return 'unavailable';
    const ratio = m.committed / m.limit;
    return ratio >= 0.97 ? 'critical' : ratio >= 0.9 ? 'warning' : 'normal';
}

let memoryMetric = 'PRIVATE_MB';
let memorySelected = '';
let memoryTiles = [];

// Balanced recursive partition: each rectangle's area is proportional to its value.
function memoryLayout(items, x = 0, y = 0, width = 1000, height = 420) {
    if (!items.length) return [];
    if (items.length === 1) return [{ ...items[0], x, y, width, height }];
    const total = items.reduce((sum, item) => sum + item.value, 0);
    let cut = 1, first = items[0].value;
    while (cut < items.length - 1 && Math.abs(first + items[cut].value - total / 2) < Math.abs(first - total / 2))
        first += items[cut++].value;
    const ratio = first / total;
    return width >= height
        ? [...memoryLayout(items.slice(0, cut), x, y, width * ratio, height),
           ...memoryLayout(items.slice(cut), x + width * ratio, y, width * (1 - ratio), height)]
        : [...memoryLayout(items.slice(0, cut), x, y, width, height * ratio),
           ...memoryLayout(items.slice(cut), x, y + height * ratio, width, height * (1 - ratio))];
}

function memoryColor(name) {
    let hash = 0;
    for (const char of name) hash = ((hash * 31) + char.charCodeAt(0)) | 0;
    return 'hsl(' + ((hash >>> 0) % 360) + ' 48% 34%)';
}

function renderMemoryTreemap(rows) {
    const items = rows.map(row => ({ name: row[0], value: Number(row[memoryMetric === 'PRIVATE_MB' ? 1 : 2]),
        privateMb: row[1], ramMb: row[2], count: row[3], unreadable: row[4] }))
        .filter(item => Number.isFinite(item.value) && item.value > 0).sort((a, b) => b.value - a.value);
    memoryTiles = memoryLayout(items);
    const total = items.reduce((sum, item) => sum + item.value, 0);
    const tiles = memoryTiles.map((item, index) => {
        const label = item.name + ' · ' + memorySize(item.value * 1048576) +
            ' · ' + (item.value / total * 100).toFixed(1) + '% · ' + item.count + ' processes';
        return `<g role="button" tabindex="0" aria-label="${esc(label)}"
            onclick="selectMemoryTile(${index})"
            onkeydown="if(event.key==='Enter'||event.key===' '){event.preventDefault();selectMemoryTile(${index})}"
            class="memory-tile${item.name === memorySelected ? ' selected' : ''}">
            <title>${esc(label)}</title>
            <rect x="${item.x}" y="${item.y}" width="${item.width}" height="${item.height}"
                fill="${memoryColor(item.name)}"/>
            ${item.width > 65 && item.height > 32 ? `<svg x="${item.x + 5}" y="${item.y + 5}"
                width="${Math.max(0, item.width - 10)}" height="${Math.max(0, item.height - 10)}" overflow="hidden">
                <text x="0" y="13">${esc(item.name)}</text>
                ${item.height > 50 ? `<text x="0" y="30">${memorySize(item.value * 1048576)} · ${(item.value / total * 100).toFixed(1)}%</text>` : ''}
                </svg>` : ''}</g>`;
    }).join('');
    return `<div class="memory-map-toolbar"><label>Block size:
        <select onchange="setMemoryMetric(this.value)">
            <option value="PRIVATE_MB" ${memoryMetric === 'PRIVATE_MB' ? 'selected' : ''}>Private allocation</option>
            <option value="RAM_MB" ${memoryMetric === 'RAM_MB' ? 'selected' : ''}>Resident RAM</option>
        </select></label><span>Shown process total: ${memorySize(total * 1048576)}</span></div>
        ${items.length ? `<svg class="memory-treemap" viewBox="0 0 1000 420" role="group"
            aria-label="Memory by process group; select a block for details">${tiles}</svg>`
            : '<div class="no-data">No measured processes match the filter.</div>'}
        <div id="memorySelection" class="memory-selection">${renderMemorySelection()}</div>`;
}

function renderMemorySelection() {
    const item = memoryTiles.find(tile => tile.name === memorySelected);
    if (!item) return 'Click a block to inspect its process group. Hover for name, size and share.';
    const section = currentSections.find(s => s.title.startsWith('ALL PROCESSES'));
    const memberMetric = section?.headers.indexOf(memoryMetric === 'PRIVATE_MB' ? 'PRIVATE_MB' : 'MEM_MB');
    const rows = (section?.rows?.filter(row => row[section.headers.indexOf('NAME')] === item.name) || [])
        .sort((a, b) => (Number(b[memberMetric]) || 0) - (Number(a[memberMetric]) || 0));
    const name = esc(item.name);
    return `<strong>${name}</strong> · Private: ${memorySize(Number(item.privateMb) * 1048576)}
        · RAM: ${memorySize(Number(item.ramMb) * 1048576)} · Instances: ${esc(item.count)}
        ${Number(item.unreadable) ? '<p>Private totals are incomplete: some processes could not be read.</p>' : ''}
        <div class="memory-members">${rows.map(row => {
            const pid = Number(row[section.headers.indexOf('PID')]);
            const privateMb = row[section.headers.indexOf('PRIVATE_MB')];
            return Number.isInteger(pid) && pid > 0 ? `<button class="btn" onclick="selectProcess(${pid}, '${esc(jsStr(item.name))}')">
                PID ${pid} · Private ${privateMb === undefined || privateMb === 'n/a' ? 'Unavailable' : memorySize(Number(privateMb) * 1048576)}
                · RAM ${memorySize(Number(row[section.headers.indexOf('MEM_MB')]) * 1048576)}</button>` : '';
        }).join('')}</div><p>Select a PID to open its path, command line and process actions in the sidebar.</p>`;
}

function selectMemoryTile(index) {
    memorySelected = memoryTiles[index]?.name || '';
    document.getElementById('memorySelection').innerHTML = renderMemorySelection();
    document.querySelectorAll('.memory-tile:not(.cpu-tile)').forEach((el, i) =>
        el.classList.toggle('selected', memoryTiles[i]?.name === memorySelected));
}

function setMemoryMetric(metric) {
    memoryMetric = metric === 'RAM_MB' ? 'RAM_MB' : 'PRIVATE_MB';
    const index = currentSections.findIndex(s => s.title === 'MEMORY ALLOCATION');
    const panel = document.getElementById('panel_' + index);
    panel.innerHTML = renderTableHtml(currentSections[index], index, panel.querySelector('.section-search input')?.value || '');
}

function renderMemoryAllocation(rows) {
    const m = memoryAllocationValues();
    const pressure = memoryPressure(m);
    const status = pressure === 'critical'
        ? 'Commit limit almost exhausted. New allocations can fail even with free RAM.'
        : pressure === 'warning' ? 'Commit usage is high. The remaining allocation headroom is small.'
        : pressure === 'unavailable' ? 'System commit measurement unavailable on this host.'
        : 'Allocation headroom is available.';
    return `<div class="memory-overview">
        <div class="memory-status ${pressure}" role="status">${status}</div>
        <div class="memory-details">RAM in use: <strong>${memorySize(m.used === null ? null : m.used * 1073741824)}</strong>
            · Available RAM: <strong>${memorySize(m.free === null ? null : m.free * 1073741824)}</strong>
            · Commit: <strong>${memorySize(m.committed)} / ${memorySize(m.limit)}</strong>
            · Unpromised headroom: <strong>${m.limit > 0 && Number.isFinite(m.committed) ? memorySize(Math.max(0, m.limit - m.committed)) : 'Unavailable'}</strong></div>
        ${renderMemoryTreemap(rows || currentSections.find(s => s.title === 'MEMORY ALLOCATION')?.rows || [])}
        <p class="memory-explanation">RAM shows data currently in physical memory. Commit shows memory Windows
            has promised to applications, backed by RAM and paging files. Available RAM is already part of
            that backing capacity; it does not mean the same amount can still be promised.</p>
        <div class="memory-details">Peak commit since boot: <strong>${memorySize(m.peak)}</strong>
            · Paged kernel pool: <strong>${memorySize(m.paged)}</strong>
            · Nonpaged kernel pool: <strong>${memorySize(m.nonpaged)}</strong></div>
        <h2 class="memory-table-title">Allocation by process name</h2>
        <p class="memory-explanation">PRIVATE_MB is private allocated memory; RAM_MB is resident memory.
            Block area and percentages use the shown process total, not the system limit; filters apply to the map.
            All instances are summed. Resident totals can count shared pages more than once and exceed physical RAM in use.
            Process private totals do not cover all system commit
            (shared allocations and system memory also contribute). UNREADABLE counts processes
            whose private memory could not be read; those groups have incomplete totals.
            Values reflect the last Refresh.</p>
        </div>`;
}

function populateCommitStat() {
    const m = memoryAllocationValues();
    const el = document.getElementById('sCommit');
    const pressure = memoryPressure(m);
    el.textContent = pressure === 'unavailable' ? 'Unavailable'
        : memorySize(m.committed) + ' / ' + memorySize(m.limit) +
            ' (' + (m.committed / m.limit * 100).toFixed(1) + '%)';
    el.className = 'stat-val memory-commit ' + pressure;
    el.title = pressure === 'unavailable' ? 'System commit measurement unavailable'
        : 'Unpromised headroom: ' + memorySize(Math.max(0, m.limit - m.committed));
}
