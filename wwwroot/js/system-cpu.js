let cpuSelected = '';
let cpuTiles = [];

function cpuGroups(rows) {
    return rows.map(row => ({ name: row[0], value: Number(row[1]), count: row[2], unreadable: row[3] }))
        .filter(item => Number.isFinite(item.value) && item.value > 0)
        .sort((a, b) => b.value - a.value);
}

function renderCpuUsage(rows) {
    const summary = currentSections.find(s => s.title === 'CPU SUMMARY')?.pairs || [];
    const field = key => summary.find(pair => pair.k === key)?.v || 'Unavailable';
    const groups = cpuGroups(rows);
    cpuTiles = memoryLayout(groups);
    const total = groups.reduce((sum, group) => sum + group.value, 0);
    const unreadable = rows.reduce((sum, row) => sum + (Number(row[3]) || 0), 0);
    const tiles = cpuTiles.map((item, index) => {
        const label = item.name + ' · CPU ' + item.value.toFixed(2) + '% · ' +
            (item.value / total * 100).toFixed(1) + '% of shown workload · ' + item.count + ' processes';
        return `<g class="memory-tile cpu-tile${cpuSelected === item.name ? ' selected' : ''}"
            role="button" tabindex="0" aria-label="${esc(label)}" onclick="selectCpuTile(${index})"
            onkeydown="if(event.key==='Enter'||event.key===' '){event.preventDefault();selectCpuTile(${index})}">
            <title>${esc(label)}</title>
            <rect x="${item.x}" y="${item.y}" width="${item.width}" height="${item.height}"
                fill="${memoryColor(item.name)}"/>
            ${item.width > 65 && item.height > 32 ? `<svg x="${item.x + 5}" y="${item.y + 5}"
                width="${Math.max(0, item.width - 10)}" height="${Math.max(0, item.height - 10)}" overflow="hidden">
                <text x="0" y="13">${esc(item.name)}</text>
                ${item.height > 50 ? `<text x="0" y="30">CPU ${item.value.toFixed(2)}%</text>` : ''}
                </svg>` : ''}</g>`;
    }).join('');
    return `<div class="memory-overview">
        <div class="memory-map-toolbar"><strong>CPU by process group</strong>
            <span>System load: ${esc(field('Load'))} · Logical CPUs: ${esc(field('Logical CPUs'))}
                · Sample: ${esc(field('Sample seconds'))} s</span></div>
        <div class="memory-details">Shown process CPU: <strong>${total.toFixed(2)}%</strong>
            · Unreadable processes: <strong>${unreadable}</strong></div>
        ${groups.length ? `<svg class="memory-treemap cpu-treemap" viewBox="0 0 1000 420" role="group"
            aria-label="CPU workload by process group; select a block for details">${tiles}</svg>`
            : '<div class="no-data">No measured CPU activity matches the filter. Idle processes remain in the table.</div>'}
        <div id="cpuSelection" class="memory-selection">${renderCpuSelection()}</div>
        <p class="memory-explanation">CPU percentages are measured over the sample interval and normalized
            to total logical CPU capacity (100% = all logical CPUs busy). Block area shows each group's share
            of the displayed workload; idle capacity is excluded. Filters apply to the map.
            Lifetime CPU_SEC is not current usage. Process totals can differ from system load due to sampling,
            kernel work and unreadable or short-lived processes. Refresh collects a new sample.</p>
        </div>`;
}

function renderCpuSelection() {
    const group = cpuTiles.find(tile => tile.name === cpuSelected);
    if (!group) return 'Click a block to inspect CPU usage by PID. Hover for usage and workload share.';
    const section = currentSections.find(s => s.title.startsWith('ALL PROCESSES'));
    const cpuIndex = section?.headers.indexOf('CPU_PCT');
    const members = (section?.rows?.filter(row => row[section.headers.indexOf('NAME')] === group.name) || [])
        .sort((a, b) => (Number(b[cpuIndex]) || 0) - (Number(a[cpuIndex]) || 0));
    return `<strong>${esc(group.name)}</strong> · CPU: ${group.value.toFixed(2)}%
        · Instances: ${esc(group.count)}
        ${Number(group.unreadable) ? '<p>Some processes could not be sampled; this total is incomplete.</p>' : ''}
        <div class="memory-members">${members.map(row => {
            const pid = Number(row[section.headers.indexOf('PID')]);
            const cpu = Number.parseFloat(row[cpuIndex]);
            return Number.isInteger(pid) && pid > 0 ? `<button class="btn"
                onclick="selectProcess(${pid}, '${esc(jsStr(group.name))}')">
                PID ${pid} · CPU ${Number.isFinite(cpu) ? cpu.toFixed(2) + '%' : 'Unavailable'}</button>` : '';
        }).join('')}</div><p>Select a PID to open its command line and process actions in the sidebar.</p>`;
}

function selectCpuTile(index) {
    cpuSelected = cpuTiles[index]?.name || '';
    document.getElementById('cpuSelection').innerHTML = renderCpuSelection();
    document.querySelectorAll('.cpu-tile').forEach((element, i) =>
        element.classList.toggle('selected', cpuTiles[i]?.name === cpuSelected));
}
