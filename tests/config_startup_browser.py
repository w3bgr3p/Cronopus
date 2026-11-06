"""Exercise startup settings in the real Config DOM without changing machine settings."""
import re
from pathlib import Path
from playwright.sync_api import sync_playwright, expect

WEB = Path(__file__).resolve().parents[1] / 'wwwroot'
with sync_playwright() as p:
    browser = p.chromium.launch()
    page = browser.new_page(viewport={'width': 1200, 'height': 900})
    source = (WEB / 'config.html').read_text('utf-8')
    html = re.sub(r'<script\b[^>]*>.*?</script>', '', source, flags=re.S)
    page.set_content(re.sub(r'<link\b[^>]*>', '', html))
    for href in re.findall(r'<link[^>]+href="(/[^"?]+\.css)(?:\?[^"\s]*)?"', source):
        page.add_style_tag(content=(WEB / href.lstrip('/')).read_text('utf-8'))
    page.evaluate('''() => {
        window.fetch = () => new Promise(() => {});
        window.setInterval = () => 0;
        window.PageState = {save:() => {}, load:() => ({})};
        window.AboutUpdates = {enter:() => {}, leave:() => {}};
        window.BridgeConfig = {load:() => {}};
    }''')
    page.add_script_tag(content=(WEB / 'js/icons.js').read_text('utf-8'))
    page.add_script_tag(content=(WEB / 'js/config.js').read_text('utf-8'))
    page.evaluate('''() => {
        window.saved = {};
        window.requests = [];
        window.fetch = async (url, options = {}) => {
            if (url === '/config' && options.method === 'POST') {
                saved = JSON.parse(options.body); requests.push(saved);
            }
            return {ok:true, json:async () => url === '/config' ? saved : {}};
        };
    }''')
    page.evaluate("async () => { await loadConfig(); showSection('server'); }")
    checkbox = page.get_by_label('Start with Windows', exact=True)
    expect(checkbox).not_to_be_checked()
    for theme in ('graphite', 'graphite-light', 'hyper', 'hyper-light'):
        page.evaluate('theme => document.documentElement.dataset.theme = theme', theme)
        expect(checkbox).to_be_visible()
        assert checkbox.bounding_box()['width'] > 0
    for value in (True, False):
        checkbox.set_checked(value)
        page.evaluate('saveConfig()')
        assert page.evaluate('requests.at(-1).startupConfig.startWithWindows') == value
        checkbox.set_checked(not value)
        page.evaluate('loadConfig()')
        assert checkbox.is_checked() == value
    page.evaluate("async () => { saved = {StartupConfig:{StartWithWindows:true}}; await loadConfig(); }")
    expect(checkbox).to_be_checked()
    print('PASS Config startup default, four themes, enable/disable save and reload, PascalCase config')
    browser.close()
