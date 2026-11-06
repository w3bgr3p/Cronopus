/* Native BAS log markers are metadata, never executable HTML. */
window.BasLog = (() => {
    const colors = { red: 'red', green: 'green', orange: 'yellow', yellow: 'yellow', blue: 'cyan',
        cyan: 'cyan', purple: 'purple', magenta: 'purple', gray: 'text3', grey: 'text3', black: 'text', white: 'text',
        '#808080': 'text3', '#ff9800': 'yellow', '#2196f3': 'cyan', '#4caf50': 'green', '#ff0000': 'red', '#008000': 'green' };
    function parse(event) {
        let message = String(event.text || ''), color = event.type === 'error' ? 'red' : 'text';
        let time = new Date(event.time).toLocaleTimeString(), source = '', tag = event.type;
        const native = message.match(/^(.*?)~~CUSTOM~LOG~~\[([^\]]*)\]\[([^\]]*)\](?:\[(\d+)\])?\s*(?:\[([^\]]+)\])?\s*(.*)$/s);
        if (native) {
            source = native[1].replace(/\s*·\s*$/, '');
            color = colors[native[3].toLowerCase()] || 'text';
            message = native[6];
            if (native[5]) time = native[5];
            tag = native[4] ? '#' + native[4] : 'log';
            const thread = message.match(/^Thread\s+#(\d+)\s*:\s*/);
            if (thread) { tag = 'Thread ' + thread[1]; message = message.slice(thread[0].length); }
        } else {
            const file = message.match(/^(.+?\.txt)\s*·\s*([\s\S]*)$/);
            if (file) { source = file[1]; message = file[2]; }
        }
        return { message, color, time, source, tag };
    }
    function append(container, event) {
        if (!container.children.length) container.dataset.size = '0';
        const entry = parse(event), last = container.lastElementChild;
        const key = JSON.stringify([entry.message, entry.color, entry.tag, entry.source]);
        if (last?.dataset.key === key) {
            const count = Number(last.dataset.count || 1) + 1; last.dataset.count = count;
            last.querySelector('.bas-log-repeat').textContent = '×' + count;
            last.querySelector('time').textContent = entry.time;
            return;
        }
        const row = document.createElement('div'); row.className = 'bas-log-row'; row.dataset.key = key;
        const time = document.createElement('time'); time.textContent = entry.time;
        const tag = document.createElement('span'); tag.className = 'bas-log-tag'; tag.textContent = entry.tag;
        const message = document.createElement('span'); message.className = 'bas-log-message'; message.textContent = entry.message;
        message.style.color = 'var(--' + entry.color + ')';
        const repeat = document.createElement('span'); repeat.className = 'bas-log-repeat';
        row.title = entry.source || event.type; row.append(time, tag, message, repeat); container.append(row);
        let size = Number(container.dataset.size || 0) + row.textContent.length;
        while (container.children.length > 1000 || (size > 262144 && container.children.length > 1)) {
            size -= container.firstElementChild.textContent.length; container.firstElementChild.remove();
        }
        container.dataset.size = size;
    }
    return { parse, append };
})();
