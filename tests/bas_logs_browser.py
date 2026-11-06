"""Native BAS log readability, color metadata, repetition and untrusted text rendering."""
from pathlib import Path
from playwright.sync_api import sync_playwright, expect

root = Path(__file__).resolve().parents[1]
output = root / '.runtime/bas-browser'; output.mkdir(parents=True, exist_ok=True)
with sync_playwright() as p:
    browser = p.chromium.launch()
    page = browser.new_page(viewport={'width': 1100, 'height': 600})
    page.set_content('<html data-theme="graphite"><body><section class="logs"><div class="section-title">BAS Log</div><div id="log"></div></section></body></html>')
    for file in ('css/themes.css', 'css/bas.css'):
        page.add_style_tag(content=(root / 'wwwroot' / file).read_text('utf-8'))
    page.add_script_tag(content=(root / 'wwwroot/js/bas-log.js').read_text('utf-8'))
    page.evaluate('''() => {
        const log = document.getElementById('log');
        const append = (type, text) => BasLog.append(log, {type, text, time: '2026-11-04T14:00:00Z'});
        append('engine', 'Local XML engine ready: 31.1.0');
        append('log', 'log\\\\2026.11.04.09.17.11.txt · ~~CUSTOM~LOG~~[.][black][990160001] [09:17:11] Thread #1 : Preparing project');
        append('log', 'log\\\\2026.11.04.09.17.11.txt · ~~CUSTOM~LOG~~[+][green][09:17:12] Thread #1 : Resources loaded');
        append('log', 'log\\\\2026.11.04.09.17.11.txt · ~~CUSTOM~LOG~~[.][orange][09:17:13] Thread #1 : Waiting for response');
        for (let i=0; i<4; i++) append('log', 'log\\\\2026.11.04.09.17.11.txt · ~~CUSTOM~LOG~~[-][red][990160001] [09:17:' + (14+i) + '] Thread #1 : BAS_SHARED_ROOT_NOT_FOUND');
        append('error', '<img src=x onerror=alert(1)> unsafe text');
    }''')
    rows = page.locator('.bas-log-row')
    expect(rows).to_have_count(6)
    expect(page.locator('#log')).not_to_contain_text('~~CUSTOM~LOG~~')
    expect(page.locator('#log')).not_to_contain_text('2026.11.04.09.17.11.txt')
    expect(rows.nth(4).locator('.bas-log-repeat')).to_have_text('×4')
    expect(rows.nth(4).locator('time')).to_have_text('09:17:17')
    expect(rows.nth(4).locator('.bas-log-tag')).to_have_text('Thread 1')
    assert page.locator('#log img').count() == 0
    for theme in ('graphite', 'graphite-light'):
        page.evaluate('theme => document.documentElement.dataset.theme = theme', theme)
        for index, color in ((1, 'text'), (2, 'green'), (3, 'yellow'), (4, 'red'), (5, 'red')):
            assert rows.nth(index).locator('.bas-log-message').evaluate('(e, token) => {const probe=document.createElement("span");probe.style.color="var(--"+token+")";document.body.append(probe);const expected=getComputedStyle(probe).color;probe.remove();return getComputedStyle(e).color===expected;}', color)
        page.screenshot(path=str(output / ('bas-logs-' + theme + '.png')))
    page.evaluate('''() => { const log=document.getElementById('log');
        for(let i=0;i<1500;i++) BasLog.append(log,{type:'log',text:'Line '+i+'x'.repeat(400),time:Date.now()}); }''')
    assert rows.count() <= 1000
    assert page.locator('#log').evaluate('e => e.textContent.length') <= 262144
    browser.close()
print('PASS BAS colors, clean metadata, aligned rows, repeated-message count, safe text and bounded history in dark/light themes')
