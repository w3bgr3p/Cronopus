(function () {
    'use strict';
    const $ = id => document.getElementById(id);
    let node = '', generation = 0, projects = [], selected = null, revision = '', cursor = 0;
    let busy = false, polling = false, initializing = true, supported = true, pending = null;
    let resourceSequence = 0;
    let picking = false;
    const resourceForm = new BasResourceForm($('resourceFields'), $('parameters'), $('resourceStatus'));
    async function loadResources() {
        const sequence = ++resourceSequence, epoch = generation, path = $('projectPath').value.trim();
        resourceForm.clear(path ? 'Reading XML resources…' : 'Select an XML project to load its resources.');
        if (!path) return;
        try {
            const data = await read('resources?projectPath=' + encodeURIComponent(path));
            if (sequence !== resourceSequence || epoch !== generation || path !== $('projectPath').value.trim()) return;
            resourceForm.load(data); buttons();
        } catch (ex) {
            if (sequence === resourceSequence && epoch === generation) resourceForm.clear('Cannot read resources: ' + ex.message);
        }
    }
    try { pending = JSON.parse(localStorage.getItem('bas-pending') || 'null'); } catch (_) { }
    const active = run => ['starting', 'running', 'stopping'].includes(run.status);
    function error(message) { $('error').textContent = message || ''; $('error').hidden = !message; }
    function pendingState(value) {
        pending = value;
        if (value) localStorage.setItem('bas-pending', JSON.stringify(value));
        else localStorage.removeItem('bas-pending');
        $('dismiss').hidden = true;
        $('recover').hidden = !pending;
    }
    function base() { return node ? '/bridge-local/' + encodeURIComponent(node) + '/api/bas/' : '/bridge-local/bas/'; }
    async function read(path) {
        const epoch = generation;
        const response = await fetch(base() + path, { headers: { 'X-Bridge-Local': '1' } });
        const data = await response.json();
        if (epoch !== generation) throw new DOMException('Node changed', 'AbortError');
        if (!response.ok) throw new Error(data.error || 'HTTP ' + response.status);
        return data;
    }
    async function mutate(path, method, body) {
        if (pending) throw new Error('Check the previous command before sending another command.');
        const command = { id: crypto.randomUUID(), node, path, time: Date.now() };
        const epoch = generation;
        pendingState(command);
        let response;
        try {
            response = await fetch(base() + path, { method, headers: { 'Content-Type': 'application/json',
                'X-Bridge-Local': '1', 'X-Request-ID': command.id }, body: JSON.stringify(body || {}) });
            const data = await response.json();
            if (epoch !== generation) throw new DOMException('Node changed', 'AbortError');
            if (response.status === 202 || response.status >= 500) throw new Error('Command outcome needs checking. Use Check command; do not repeat it.');
            pendingState(null);
            if (!response.ok) throw new Error(data.error || 'HTTP ' + response.status);
            return data;
        } catch (ex) {
            if (pending && pending.id === command.id) $('status').textContent = 'Command outcome unknown';
            throw ex;
        }
    }
    function buttons() {
        const project = projects.find(p => p.id === selected);
        const loaded = project && project.state !== 'idle';
        ['name', 'projectPath', 'parameters'].forEach(id => $(id).disabled = busy || !!loaded);
        resourceForm.setDisabled(busy || !!loaded);
        $('loadResources').disabled = busy || !$('projectPath').value.trim();
        $('pickProject').disabled = busy || picking || !!loaded || !!pending;
        $('save').disabled = busy || !!loaded || !!pending;
        $('delete').disabled = busy || !project || project.active > 0 || project.state === 'deleting' || !!pending;
        $('run').disabled = busy || !project || !supported || !!pending || ['failed', 'unloading', 'deleting'].includes(project.state) || project.active >= project.maxThreads;
        $('stop').disabled = busy || !project || project.active === 0 || !!pending;
        $('unload').disabled = busy || !loaded || !!pending;
        $('node').disabled = busy;
        $('new').disabled = busy;
        $('recover').hidden = !pending;
        $('recover').disabled = busy;
        $('hint').textContent = !supported ? 'BAS engines require a Windows node.' : loaded
            ? 'Release runtime before editing. Run reads the saved XML again. One active execution per project.'
            : 'Run saved XML directly. Threads and limits come from the project. Resources override its defaults; no compilation or Premium required.';
    }
    function fill(project) {
        window.dispatchEvent(new Event('bridge-node-changed'));
        selected = project?.id || null; revision = project?.revision || ''; cursor = 0;
        $('projectTitle').textContent = project?.name || 'New project';
        for (const key of ['name', 'projectPath']) $(key).value = project?.[key] || '';
        $('parameters').value = project?.parameters || '{}';
        loadResources();
        $('log').textContent = ''; $('runs').replaceChildren();
        renderProjects(); buttons();
    }
    function renderProjects() {
        $('projects').replaceChildren();
        for (const project of projects) {
            const button = document.createElement('button'); button.className = 'project' + (project.id === selected ? ' selected' : '');
            button.textContent = project.name;
            const detail = document.createElement('small'); detail.textContent = `${project.projectPath} · ${project.state}`;
            button.append(detail); button.onclick = () => { fill(project); refreshDetail().catch(showError); };
            $('projects').append(button);
        }
        if (!projects.length) { const hint = document.createElement('p'); hint.className = 'empty'; hint.textContent = 'No BAS projects on this node.'; $('projects').append(hint); }
    }
    function showError(ex) { if (ex.name !== 'AbortError') error(ex.message); }
    async function refreshDetail() {
        if (!selected) return;
        const id = selected, epoch = generation;
        const [runs, events] = await Promise.all([read(`projects/${id}/runs`), read(`projects/${id}/events?after=${cursor}`)]);
        if (id !== selected || epoch !== generation) return;
        $('runs').replaceChildren();
        for (const run of runs) {
            const row = document.createElement('tr');
            const values = [new Date(run.startedAt).toLocaleTimeString(), run.threadId, run.status,
                run.error || (run.result === null ? '' : run.result.report || JSON.stringify(run.result))];
            values.forEach((value, index) => { const cell = document.createElement('td'); cell.textContent = value;
                if (index === 2) cell.className = run.status; if (index === 3) cell.className = 'result'; row.append(cell); });
            const action = document.createElement('td');
            if (active(run)) { const stop = document.createElement('button'); stop.textContent = 'Stop'; stop.disabled = busy || !!pending;
                stop.onclick = () => perform(() => mutate(`projects/${id}/stop`, 'POST', { runId: run.id })); action.append(stop); }
            row.append(action); $('runs').append(row);
        }
        $('runStats').textContent = `${runs.filter(active).length} active · ${runs.filter(r => r.status === 'succeeded').length} succeeded · ${runs.filter(r => r.status === 'failed').length} failed`;
        const log = $('log'), atBottom = log.scrollTop + log.clientHeight >= log.scrollHeight - 24;
        if (!log.children.length) log.dataset.size = '0';
        for (const event of events) { cursor = Math.max(cursor, event.cursor); BasLog.append(log, event); }
        if (atBottom) log.scrollTop = log.scrollHeight;
    }
    async function refresh() {
        const data = await read('projects'); supported = data.supported; projects = data.projects;
        renderProjects();
        const project = projects.find(p => p.id === selected);
        if (selected && !project) fill(null);
        $('engineState').textContent = project ? `${project.state} · ${project.active}/${project.maxThreads}` : 'idle';
        if (project?.error) error(project.error);
        buttons(); await refreshDetail();
        $('status').textContent = pending ? 'Command outcome needs checking' : 'Connected · ' + new Date().toLocaleTimeString();
    }
    async function perform(action) {
        if (busy) return;
        busy = true; error(''); buttons();
        try { await action(); await refresh(); }
        catch (ex) { showError(ex); }
        finally { busy = false; buttons(); }
    }
    $('editor').onsubmit = event => { event.preventDefault(); perform(async () => {
        const params = JSON.parse($('parameters').value);
        if (!params || typeof params !== 'object' || Array.isArray(params)) throw new Error('Parameters must be a JSON object');
        const input = { id: selected || '', revision, name: $('name').value, projectPath: $('projectPath').value,
            parameters: JSON.stringify(params), maxThreads: 1 };
        const saved = await mutate('projects', 'POST', input); fill(saved);
    }); };
    $('run').onclick = () => perform(() => mutate(`projects/${selected}/run`, 'POST', { count: 1 }));
    $('stop').onclick = () => perform(() => mutate(`projects/${selected}/stop`, 'POST'));
    $('unload').onclick = () => perform(async () => {
        await mutate(`projects/${selected}/unload`, 'POST'); cursor = 0; $('log').textContent = '';
    });
    $('delete').onclick = () => perform(async () => { await mutate(`projects/${selected}`, 'DELETE', { revision }); fill(null); });
    $('new').onclick = () => { fill(null); error(''); };
    $('pickProject').onclick = async () => {
        const epoch = generation, sequence = resourceSequence, selectedNode = node;
        const current = () => epoch === generation && sequence === resourceSequence;
        picking = true; error(''); buttons();
        try {
            let path;
            if (selectedNode) {
                const connection = { id: selectedNode, name: $('node').selectedOptions[0].textContent };
                const bridge = { context: () => ({ nodeId: selectedNode }), connection: () => connection,
                    isCurrent: current, api: (route, options) => fetch('/bridge-local/' + encodeURIComponent(selectedNode) + '/api/' + route,
                        { ...options, headers: { ...options?.headers, 'X-Bridge-Local': '1' } }) };
                path = await TaskerFiles.open({ mode: 'file', extension: 'xml', readOnly: true, start: $('projectPath').value, bridge });
            } else {
                const response = await fetch('/tasker/pick?mode=file&ext=bas&start=' + encodeURIComponent($('projectPath').value));
                const result = await response.json();
                if (!response.ok || !result.ok) throw new Error(result.error || 'Cannot open the file picker');
                path = result.path;
            }
            if (!path || !current()) return;
            if (!path.toLowerCase().endsWith('.xml')) throw new Error('Choose a BAS XML project');
            if ($('projectPath').value !== path) {
                $('projectPath').value = path; $('projectPath').onchange();
                if (!$('name').value.trim()) $('name').value = path.split(/[\\/]/).pop().replace(/\.xml$/i, '');
            }
        } catch (ex) { if (current()) showError(ex); }
        finally { picking = false; buttons(); }
    };
    $('loadResources').onclick = loadResources;
    $('projectPath').oninput = () => {
        resourceSequence++; resourceForm.clear('Read resources from the selected XML.'); buttons();
    };
    $('projectPath').onchange = () => { $('parameters').value = '{}'; loadResources(); };
    $('refresh').onclick = () => perform(refresh);
    $('clearLog').onclick = () => { $('log').textContent = ''; };
    $('node').onchange = () => {
        node = $('node').value; generation++; projects = []; fill(null); error('');
        localStorage.setItem('bas-node', node); perform(refresh);
    };
    $('recover').onclick = () => perform(async () => {
        if (!pending) return;
        if (pending.node !== node) {
            if (![...$('node').options].some(option => option.value === pending.node))
                throw new Error('Restore the saved connection for this pending command.');
            node = pending.node; $('node').value = node; generation++; projects = []; fill(null);
            localStorage.setItem('bas-node', node);
        }
        const path = node ? '/bridge-local/' + encodeURIComponent(node) + '/api/operations/' : '/bridge-local/bas/operations/';
        const response = await fetch(path + encodeURIComponent(pending.id), { headers: { 'X-Bridge-Local': '1' } });
        if (response.status === 404) {
            $('dismiss').hidden = false;
            throw new Error('No command record is available. Inspect the project runs, then dismiss this pending command if resolved. It will not be sent again.');
        }
        if (!response.ok) throw new Error('Command record unavailable. Keep this command pending until its node is reachable.');
        const operation = await response.json();
        if (!['succeeded', 'failed'].includes(operation.state)) throw new Error('Command is ' + operation.state + '. Check again after reconnecting.');
        if (operation.status < 400 && pending.path.endsWith('/unload')) { cursor = 0; $('log').textContent = ''; }
        pendingState(null);
        if (operation.status >= 400) throw new Error(JSON.parse(operation.result).error || 'Command failed');
    });
    async function initialize() {
        const response = await fetch('/bridge-local/connections', { headers: { 'X-Bridge-Local': '1' } });
        if (!response.ok) throw new Error('Cannot load saved connections');
        for (const connection of await response.json()) { const option = document.createElement('option'); option.value = connection.id; option.textContent = connection.name; $('node').append(option); }
        const savedNode = localStorage.getItem('bas-node') || '';
        if ([...$('node').options].some(option => option.value === savedNode)) { node = savedNode; generation++; }
        $('node').value = node; fill(null); await refresh();
    }
    $('dismiss').onclick = () => {
        if (pending?.path.endsWith('/unload')) { cursor = 0; $('log').textContent = ''; }
        pendingState(null); error('Pending command dismissed. No command was sent.'); buttons();
    };
    initialize().catch(showError).finally(() => { initializing = false; });
    setInterval(async () => {
        if (initializing || polling || busy || document.hidden) return;
        polling = true; try { await refresh(); } catch (ex) { showError(ex); $('status').textContent = 'Disconnected'; }
        finally { polling = false; }
    }, 2000);
})();
