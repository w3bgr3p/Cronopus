// Tasker overview uses the System memory map's balanced area partition.
var taskOverviewMode = 'results';
var taskOverviewMetrics = { results: 'failures', resources: 'memory' };
var taskOverviewLabels = { failures: 'Failed attempts', failureRate: 'Failure rate',
    successRate: 'Success rate', successes: 'Successful attempts', tasks: 'Current status',
    memory: 'RAM', cpu: 'CPU' };

function taskOverviewItems(metric) {
    return schedules.map(function(s) {
        var total = Math.max(0, parseInt(s.runs_total) || 0);
        var success = Math.min(total, Math.max(0, parseInt(s.runs_success) || 0));
        var failed = total - success;
        var res = _resources && _resources.Tasks && _resources.Tasks[s.id];
        var value = null, reason = 'No attempts yet';
        if (metric === 'tasks') value = 1;
        else if (metric === 'memory' || metric === 'cpu') {
            reason = res && !res.Processes ? 'In-process / not attributable' : 'No measurement';
            if (res && res.Processes > 0) {
                var measured = metric === 'memory' ? res.MemoryMB : res.CpuPct;
                if (Number.isFinite(measured) && measured >= 0) value = measured;
                else reason = 'Measurement unavailable / warming up';
            }
        } else if (total > 0) {
            value = metric === 'failureRate' ? failed / total * 100
                : metric === 'successRate' ? success / total * 100
                : metric === 'successes' ? success : failed;
        }
        return { id: s.id, name: s.name || '(unnamed)', status: getTaskStatus(s),
            total: total, success: success, failed: failed, value: value,
            reason: value === 0 ? 'Zero for this metric' : reason };
    }).sort(function(a, b) {
        return (b.value || 0) - (a.value || 0) || a.name.localeCompare(b.name);
    });
}

function taskMapLayout(items, x = 0, y = 0, width = 1000, height = 420) {
    if (!items.length) return [];
    if (items.length === 1) return [Object.assign({}, items[0], { x, y, width, height })];
    var total = items.reduce(function(sum, item) { return sum + item.value; }, 0);
    var cut = 1, first = items[0].value;
    while (cut < items.length - 1 && Math.abs(first + items[cut].value - total / 2) < Math.abs(first - total / 2))
        first += items[cut++].value;
    var ratio = first / total;
    return width >= height
        ? taskMapLayout(items.slice(0, cut), x, y, width * ratio, height)
            .concat(taskMapLayout(items.slice(cut), x + width * ratio, y, width * (1 - ratio), height))
        : taskMapLayout(items.slice(0, cut), x, y, width, height * ratio)
            .concat(taskMapLayout(items.slice(cut), x, y + height * ratio, width, height * (1 - ratio)));
}

function taskOverviewValue(item, metric) {
    if (item.value === null) return item.reason;
    if (metric === 'tasks') return item.status;
    if (metric === 'memory') return fmtMB(item.value);
    if (metric === 'cpu' || metric === 'failureRate' || metric === 'successRate') return item.value.toFixed(1) + '%';
    return String(item.value);
}

function renderTaskOverview() {
    var metric = taskOverviewMetrics[taskOverviewMode];
    var resourceMode = taskOverviewMode === 'resources';
    var items = taskOverviewItems(metric), measured = items.filter(function(i) { return i.value > 0; });
    var total = measured.reduce(function(sum, i) { return sum + i.value; }, 0);
    var tiles = taskMapLayout(measured);
    var choices = resourceMode ? ['memory', 'cpu'] : ['failures', 'failureRate', 'successRate', 'successes', 'tasks'];
    function button(item, tile) {
        var value = taskOverviewValue(item, metric);
        var detail = item.success + ' succeeded / ' + item.failed + ' failed / ' + item.total + ' attempts';
        var label = item.name + ' · ' + item.status + ' · ' + taskOverviewLabels[metric] + ': ' + value + ' · ' + detail;
        if (tile) label += ' · ' + (item.value / total * 100).toFixed(1) + '% of shown metric';
        var tone = resourceMode || metric === 'tasks' ? item.status
            : !item.total ? 'newbie' : !item.failed ? 'done' : !item.success ? 'fail' : 'mixed';
        var style = tile ? ' style="left:' + item.x / 10 + '%;top:' + item.y / 4.2
            + '%;width:' + item.width / 10 + '%;height:' + item.height / 4.2 + '%"' : '';
        return '<button type="button" class="' + (tile ? 'task-map-tile' : 'task-map-zero') + ' ' + tone
            + '" data-task-id="' + escHtml(item.id) + '" title="' + escHtml(label) + '" aria-label="' + escHtml(label) + '"'
            + style + ' onclick="selectRow(this.dataset.taskId)">'
            + '<span class="task-map-name">' + escHtml(item.name) + '</span>'
            + '<span class="task-map-value">' + escHtml(value) + '</span>'
            + (tile ? '<span class="task-map-detail">' + escHtml(item.status) + ' · ' + detail + '</span>' : '') + '</button>';
    }
    return '<div class="task-overview-toolbar"><div class="task-overview-modes" role="group" aria-label="Task overview">'
        + ['results', 'resources'].map(function(mode) {
            return '<button class="btn sm" data-overview-mode="' + mode + '" aria-pressed="' + (taskOverviewMode === mode)
                + '" onclick="setTaskOverviewMode(\'' + mode + '\')">' + (mode === 'results' ? 'Results' : 'Resources') + '</button>';
        }).join('') + '</div><label>Block area: <select aria-label="Block area" onchange="setTaskOverviewMetric(this.value)">'
        + choices.map(function(key) { return '<option value="' + key + '"' + (key === metric ? ' selected' : '') + '>'
            + taskOverviewLabels[key] + '</option>'; }).join('') + '</select></label></div>'
        + '<p class="task-map-hint">' + (resourceMode
            ? 'Live task process trees on the selected machine. In-process runs cannot be attributed. Block areas exclude unmeasured and zero values.'
            : 'Stored attempt counters (since last reset). Block area follows the selected metric; rates use each task’s own attempts.')
        + ' Click a task to open it. Hover or focus for exact values.</p>'
        + '<div class="task-map-legend">' + (resourceMode || metric === 'tasks'
            ? '<span class="running">Running</span><span class="fail">Last run failed</span><span class="done">Done</span><span class="planned">Planned</span><span>Paused / new</span>'
            : '<span class="fail">All attempts failed</span><span class="mixed">Mixed results</span><span class="done">All attempts succeeded</span>') + '</div>'
        + (tiles.length ? '<div class="task-map" role="group" aria-label="Tasks by ' + taskOverviewLabels[metric] + '">'
            + tiles.map(function(i) { return button(i, true); }).join('') + '</div>'
            : '<div class="task-map-empty">' + (!items.length ? 'No tasks yet.' : resourceMode && !_resources
                ? 'Resource measurements unavailable. Waiting for the next sample.' : 'No positive values for this metric.') + '</div>')
        + (items.length > tiles.length ? '<details class="task-map-remainder" open><summary>Zero / unavailable · '
            + (items.length - tiles.length) + ' tasks</summary><div class="task-map-zeros">'
            + items.filter(function(i) { return !(i.value > 0); }).map(function(i) { return button(i, false); }).join('') + '</div></details>' : '');
}

function updateTaskOverview() {
    var host = document.getElementById('taskOverview');
    if (!host || selectedId) return;
    var active = document.activeElement;
    var taskId = active && host.contains(active) && active.dataset.taskId;
    var focusedMode = active && host.contains(active) && active.dataset.overviewMode;
    var remainder = host.querySelector('details');
    var collapsed = remainder && !remainder.open;
    // Preserve controls and keyboard focus while live samples replace the map.
    var fresh = document.createElement('div');
    fresh.innerHTML = renderTaskOverview();
    var toolbar = host.querySelector('.task-overview-toolbar');
    if (toolbar && toolbar.outerHTML === fresh.firstElementChild.outerHTML) {
        fresh.firstElementChild.remove();
        while (toolbar.nextSibling) toolbar.nextSibling.remove();
        host.append(...fresh.childNodes);
    } else {
        host.replaceChildren(...fresh.childNodes);
        if (active && active.tagName === 'SELECT') host.querySelector('select').focus({ preventScroll: true });
        if (focusedMode) host.querySelector('[data-overview-mode="' + focusedMode + '"]').focus({ preventScroll: true });
    }
    if (collapsed && host.querySelector('details')) host.querySelector('details').open = false;
    if (taskId) Array.from(host.querySelectorAll('[data-task-id]')).find(function(el) {
        return el.dataset.taskId === taskId;
    })?.focus({ preventScroll: true });
}

function setTaskOverviewMode(mode) {
    if (mode !== 'results' && mode !== 'resources') return;
    taskOverviewMode = mode;
    updateTaskOverview();
}

function setTaskOverviewMetric(metric) {
    var allowed = taskOverviewMode === 'resources' ? ['memory', 'cpu']
        : ['failures', 'failureRate', 'successRate', 'successes', 'tasks'];
    if (allowed.indexOf(metric) < 0) return;
    taskOverviewMetrics[taskOverviewMode] = metric;
    updateTaskOverview();
}
