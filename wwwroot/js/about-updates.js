/* Update preparation never exits or reloads the running dashboard. */
const AboutUpdates = (() => {
    let timer = null;
    let active = false;
    let loading = false;
    const el = id => document.getElementById(id);
    function render(data) {
        el('aboutVersion').textContent = data.currentVersion;
        el('updateCurrent').textContent = data.currentVersion;
        el('updateLatest').textContent = data.latestVersion || 'Not checked';
        el('updateChecked').textContent = data.checkedAt ? new Date(data.checkedAt).toLocaleString() : '—';
        el('updateAutomatic').checked = data.automatic;
        el('updateAutomatic').disabled = !data.supported;
        const busy = ['checking', 'downloading', 'verifying'].includes(data.phase);
        const messages = {
            idle: 'Updates have not been checked yet.', checking: 'Checking for updates…',
            current: 'You are using the latest version.', available: 'A new version is available.',
            downloading: 'Downloading update…', verifying: 'Verifying and installing the update separately…',
            ready: 'Update prepared successfully.', error: 'Update could not be prepared. Your running version is unchanged.'
        };
        el('updateStatus').textContent = data.supported ? (messages[data.phase] || data.phase) : 'Automatic updates are available for Windows builds.';
        const progress = el('updateProgress');
        progress.hidden = data.phase !== 'downloading';
        progress.max = data.totalBytes || 1;
        progress.value = data.downloadedBytes || 0;
        const restart = el('updateRestart');
        restart.hidden = !data.restartRequired;
        restart.textContent = data.restartRequired
            ? `Update ${data.pendingVersion} is installed and ready. Restart the program when convenient to apply it. Running tasks will continue until you close the program yourself.` : '';
        const error = el('updateError');
        error.textContent = [data.applyError, data.error].filter(Boolean).join('\n');
        error.hidden = !error.textContent;
        el('updateCheck').disabled = busy || !data.supported;
        el('updateInstall').disabled = busy || !data.supported || data.restartRequired || data.phase !== 'available';
    }
    async function request(path, body) {
        const response = await fetch('/app-update/' + path, body === undefined ? {} : {
            method: 'POST', headers: { 'Content-Type': 'application/json', 'X-z3nDash-Update': '1' },
            body: JSON.stringify(body)
        });
        const data = await response.json();
        if (!response.ok) throw new Error(data.error || `HTTP ${response.status}`);
        return data;
    }
    function showError(error) {
        el('updateError').textContent = error.message;
        el('updateError').hidden = false;
    }
    async function refresh() {
        if (!active || loading) return;
        loading = true;
        try { const data = await request('status'); if (active) render(data); }
        catch (error) { if (active) showError(error); }
        finally { loading = false; }
    }
    return {
        enter() { active = true; refresh(); clearInterval(timer); timer = setInterval(refresh, 1500); },
        leave() { active = false; clearInterval(timer); timer = null; },
        async action(action) {
            el('updateCheck').disabled = true;
            el('updateInstall').disabled = true;
            try { render(await request(action, {})); }
            catch (error) { showError(error); }
        },
        async setAutomatic(automatic) {
            try { render(await request('settings', { automatic })); }
            catch (error) { el('updateAutomatic').checked = !automatic; showError(error); }
        }
    };
})();
