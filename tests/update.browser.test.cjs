const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const { spawn } = require('node:child_process');
let playwright;
try { playwright = require('playwright'); }
catch { playwright = require(path.join(require('node:os').homedir(), '.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright')); }
const { chromium } = playwright;
const build = path.resolve(__dirname, '../artifacts/update-build');
const base = 'http://localhost:39467';
const headers = { 'Content-Type': 'application/json', 'X-z3nDash-Update': '1' };
const secretPath = path.join(build, 'appsettings.secrets.json');
fs.writeFileSync(secretPath, JSON.stringify({
    DbConfig: { Type: 'sqlite', SqlitePath: path.join(build, 'update-test.sqlite') },
    LogsConfig: { DashboardPort: '39467', LogsFolder: path.join(build, 'logs'),
        TempFolder: path.join(build, 'temp'), ReportsFolder: path.join(build, 'reports') },
    WatchdogConfig: { Enabled: false }, ClipboardConfig: { Enabled: false }
}));
const app = spawn(path.join(build, 'z3nDash.exe'), ['--headless'], {
    cwd: build, windowsHide: true, env: { ...process.env, Z3NDASH_BRIDGE_STATE_DIR: path.join(build, 'test-bridge') }
});
let output = '';
app.stdout.on('data', data => { output += data; });
app.stderr.on('data', data => { output += data; });
let browser;
let activatedPid;
(async () => {
    for (let i = 0; i < 100; i++) {
        if (app.exitCode !== null) throw new Error('Isolated app exited: ' + output);
        try { if ((await fetch(base + '/app-update/status')).ok) break; } catch {}
        if (i === 99) throw new Error('Startup timed out: ' + output);
        await new Promise(resolve => setTimeout(resolve, 200));
    }
    const post = (route, body, extraHeaders = headers) => fetch(base + '/app-update/' + route, {
        method: 'POST', headers: extraHeaders, body: JSON.stringify(body)
    });
    let response = await post('settings', { automatic: false });
    assert.equal(response.status, 200);
    assert.equal((await response.json()).automatic, false);
    response = await post('check', {}, { 'Content-Type': 'application/json' });
    assert.equal(response.status, 403);
    response = await post('check', {}, { ...headers, Origin: 'https://example.invalid' });
    assert.equal(response.status, 403);
    response = await post('check', {});
    assert.equal(response.status, 202);
    let status;
    for (let i = 0; i < 100; i++) {
        status = await (await fetch(base + '/app-update/status')).json();
        if (status.phase !== 'checking') break;
        await new Promise(resolve => setTimeout(resolve, 300));
    }
    assert.ok(['current', 'available'].includes(status.phase), JSON.stringify(status));
    assert.match(status.latestVersion, /^\d+\.\d+\.\d+/);
    assert.equal(app.exitCode, null);
    console.log('PASS live GitHub check, saved setting, cross-origin guards and running process preserved');
    browser = await chromium.launch({ headless: true });
    const page = await browser.newPage();
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    await page.goto(base + '/config.html');
    await page.locator('.cfg-rail-item[data-sec="about"]').click();
    await page.locator('#updateCurrent').filter({ hasText: /1\.1\.2/ }).waitFor();
    for (const [theme, width] of [['graphite', 1280], ['graphite-light', 1280], ['graphite', 440]]) {
        await page.setViewportSize({ width, height: 1000 });
        await page.evaluate(theme => document.documentElement.dataset.theme = theme, theme);
        assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true);
        await page.screenshot({ path: path.join(build, `about-${theme}-${width}.png`), fullPage: true });
    }
    // Exercise the ready message independently from the older public release.
    await page.route('**/app-update/status', route => route.fulfill({ json: {
        ...status, phase: 'ready', pendingVersion: '9.0.0', restartRequired: true
    } }));
    await page.locator('#updateRestart').waitFor({ state: 'visible' });
    assert.match(await page.locator('#updateRestart').innerText(), /Restart the program when convenient/);
    assert.equal(await page.locator('#updateInstall').isDisabled(), true);
    assert.equal(app.exitCode, null);
    assert.deepEqual(errors, []);
    console.log('PASS About tab in dark/light/mobile layouts; restart notice does not restart the application');
    await browser.close(); browser = null;
    const crypto = require('node:crypto');
    const installId = crypto.createHash('sha256').update(build.replace(/[\\/]+$/, '').toUpperCase()).digest('hex').toUpperCase().slice(0, 16);
    const updates = path.join(process.env.LOCALAPPDATA, 'z3nDash/updates', installId);
    const payload = path.join(updates, 'startup-test/payload');
    fs.mkdirSync(payload, { recursive: true });
    for (const file of ['z3nDash.exe', 'z3nDash.dll']) fs.copyFileSync(path.join(build, file), path.join(payload, file));
    fs.writeFileSync(path.join(payload, 'activation-proof.txt'), 'prepared version applied');
    fs.writeFileSync(path.join(updates, 'pending.json'), JSON.stringify({ Version: '99.0.0', Directory: payload }));
    const extra = spawn(path.join(build, 'z3nDash.exe'), ['--headless'], {
        cwd: build, windowsHide: true, stdio: 'ignore', env: { ...process.env, Z3NDASH_BRIDGE_STATE_DIR: path.join(build, 'test-bridge') }
    });
    for (let i = 0; i < 150 && !fs.existsSync(path.join(updates, 'apply-error.txt')); i++)
        await new Promise(resolve => setTimeout(resolve, 100));
    assert.equal(app.exitCode, null);
    assert.equal(fs.existsSync(path.join(build, 'activation-proof.txt')), false);
    assert.match(fs.readFileSync(path.join(updates, 'apply-error.txt'), 'utf8'), /Another z3nDash or MCP client/);
    assert.equal((await (await fetch(base + '/mcp/status')).json()).process_id, app.pid);
    assert.ok(extra.exitCode !== null, 'Extra startup should exit before scheduling');
    console.log('PASS startup while dashboard is active postpones update without interrupting it or starting another scheduler');
    await new Promise(resolve => { app.once('exit', resolve); app.kill(); });
    spawn(path.join(build, 'z3nDash.exe'), ['--headless'], {
        cwd: build, windowsHide: true, stdio: 'ignore', env: { ...process.env, Z3NDASH_BRIDGE_STATE_DIR: path.join(build, 'test-bridge') }
    }).unref();
    for (let i = 0; i < 200; i++) {
        try {
            const runtime = await (await fetch(base + '/mcp/status')).json();
            if (runtime.process_id !== app.pid) { activatedPid = runtime.process_id; break; }
        } catch {}
        await new Promise(resolve => setTimeout(resolve, 100));
    }
    assert.ok(activatedPid, 'Activation did not launch the application');
    assert.equal(fs.readFileSync(path.join(build, 'activation-proof.txt'), 'utf8'), 'prepared version applied');
    assert.equal(fs.existsSync(path.join(updates, 'pending.json')), false);
    assert.equal(fs.existsSync(path.join(updates, 'apply-error.txt')), false);
    assert.equal((await (await fetch(base + '/app-update/status')).json()).restartRequired, false);
    fs.unlinkSync(path.join(build, 'activation-proof.txt'));
    console.log('PASS real manual startup invokes embedded helper, replaces binary files and starts the dashboard with no pending update');
})().catch(error => { console.error(error); process.exitCode = 1; }).finally(async () => {
    if (browser) await browser.close();
    app.kill(); // Only the isolated test process; no production processes are touched.
    if (activatedPid) process.kill(activatedPid);
});
