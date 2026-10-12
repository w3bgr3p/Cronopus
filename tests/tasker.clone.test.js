const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const vm = require('node:vm');

test('cloning selects the new task and opens its Settings tab', async () => {
    const tabs = ['execution', 'settings', 'schedule', 'traffic'].map((name) => ({
        dataset: { tab: name },
        active: name === 'execution',
        classList: {
            toggle(_className, active) { this.owner.active = active; },
            owner: null,
        },
    }));
    tabs.forEach((tab) => { tab.classList.owner = tab; });

    const source = {
        id: 'source-1', name: 'Example', executor: 'python', script_path: 'task.py',
        args: '', enabled: 'true', cron: '', on_overlap: 'skip', max_threads: '1',
        timeout_seconds: '0', use_venv: 'false', schedule_mode: 'off', schedule_json: '',
    };
    const clone = { ...source, id: 'clone-2', name: 'Example (2)', enabled: 'false' };
    const calls = [];
    const context = {
        console,
        document: {
            addEventListener() {},
            querySelectorAll(selector) { return selector === '.dtab' ? tabs : []; },
        },
        window: { addEventListener() {} },
        fetch: async (url, options) => {
            calls.push({ url, options });
            // The page also starts a resource request during initialization.
            // Match endpoints so background requests cannot consume clone responses.
            if (url === '/tasker/resources')
                return { ok: true, json: async () => ({ Tasks: {} }) };
            if (url === '/tasker/save')
                return { ok: true, json: async () => ({ ok: true, id: clone.id }) };
            if (url === '/tasker/payload?id=' + source.id)
                return { ok: true, json: async () => ({ schema: '', values: '' }) };
            throw new Error('Unexpected request: ' + url);
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

    context.schedules = [source];
    context.loadList = async () => { context.schedules = [source, clone]; };
    context.renderList = () => {};
    context.showDetailHeader = () => {};
    context.renderDetail = () => {};
    context.showBottomPanels = () => {};
    context.stopProcStatsPoll = () => {};
    context._updateAiContext = () => {};

    await context.duplicateSchedule(source.id);

    const save = calls.find(call => call.url === '/tasker/save');
    assert.equal(save.options.method, 'POST');
    assert.equal(JSON.parse(save.options.body).name, clone.name);
    assert.equal(JSON.parse(save.options.body).enabled, 'false');
    assert.ok(calls.some(call => call.url === '/tasker/payload?id=' + source.id));
    assert.equal(context.selectedId, clone.id);
    assert.equal(context.activeTab, 'settings');
    assert.equal(tabs.find((tab) => tab.dataset.tab === 'settings').active, true);
});
