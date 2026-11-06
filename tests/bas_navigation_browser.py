"""Check BAS discovery through the real Config DOM and shared dock catalog."""
import mimetypes
from pathlib import Path
from playwright.sync_api import sync_playwright, expect

web = Path(__file__).resolve().parents[1] / 'wwwroot'
output = web.parent / '.runtime/bas-navigation'
output.mkdir(parents=True, exist_ok=True)

with sync_playwright() as p:
    browser = p.chromium.launch()
    page = browser.new_page(viewport={'width': 1360, 'height': 950})
    errors = []
    page.on('pageerror', lambda error: errors.append(str(error)))

    def respond(route):
        from urllib.parse import urlparse
        path = urlparse(route.request.url).path
        if path == '/':
            path = '/config.html'
        file = (web / path.lstrip('/')).resolve()
        if file.is_relative_to(web.resolve()) and file.is_file():
            route.fulfill(path=str(file), content_type=mimetypes.guess_type(file)[0] or 'application/octet-stream')
        else:
            route.fulfill(json=[] if path.endswith('/projects') or path.endswith('/connections') else {})

    page.route('http://cronopus.test/**', respond)
    page.goto('http://cronopus.test/?page=config')
    page.evaluate("showSection('ui')")
    checkbox = page.locator('#uiDockList input[value="bas"]')
    expect(checkbox).to_be_visible()
    expect(checkbox).to_be_checked()
    item = page.locator('#uiDockList label:has(input[value="bas"])')
    expect(item).to_contain_text('BAS')
    assert item.locator('svg').count() == 1
    dock = page.locator('#zp-dock a[href="/bas.html"]')
    expect(dock).to_have_count(1)
    checkbox.uncheck()
    expect(dock).to_be_hidden()
    page.reload()
    page.evaluate("showSection('ui')")
    expect(checkbox).not_to_be_checked()
    checkbox.check()
    page.screenshot(path=str(output / 'config-bas.png'), full_page=True)
    page.evaluate("document.querySelector('#zp-dock a[href=\"/bas.html\"]').click()")
    expect(page).to_have_url('http://cronopus.test/bas.html')
    expect(page.locator('#projectPath')).to_be_visible()
    expect(page.locator('#zp-dock a[href="/bas.html"].active')).to_have_count(1)
    assert not errors, errors
    browser.close()
print('PASS Config lists BAS with icon; dock visibility persists; dock opens BAS and identifies active page')
