"""Compare the BAS form with actual input metadata, without running the user's script."""
import hashlib
import json
import sys
from pathlib import Path
from playwright.sync_api import sync_playwright, expect

url = sys.argv[1]
source = Path(sys.argv[2]).resolve()
original = hashlib.sha256(source.read_bytes()).digest()
output = Path('.runtime/bas-browser'); output.mkdir(parents=True, exist_ok=True)
with sync_playwright() as p:
    browser = p.chromium.launch()
    page = browser.new_page(viewport={'width': 1440, 'height': 1050})
    errors = []; page.on('pageerror', lambda error: errors.append(str(error)))
    page.goto(url)
    expect(page.locator('.project')).to_have_count(1)
    page.locator('#new').click()
    page.route('**/tasker/pick?*', lambda route: route.fulfill(json={'ok': True, 'path': str(source)}), times=1)
    page.locator('#pickProject').click()
    expect(page.locator('#resourceStatus')).to_contain_text('6 inputs')
    expect(page.locator('.resource-field:visible')).to_have_count(6)
    expect(page.locator('#advancedResourcesLabel')).to_be_hidden()
    expected = {
        'DiagLog': ('diag log', 'false'),
        'UberService': ('Simroute service code', 'ub'),
        'UberCountry': ('number ISO; default proxy ISO', 'za'),
        'UberProxyCountry': ('override proxy ISO', ''),
        'UberProvider': ('Simroute provider ID of the number; empty = not selected', ''),
        'Thread': ('Threads', '1'),
    }
    for name, (label, value) in expected.items():
        field = page.locator('[data-resource=' + json.dumps(name) + ']')
        expect(field).to_be_enabled()
        if name == 'DiagLog':
            expect(field).to_have_attribute('role', 'switch'); expect(field).not_to_be_checked()
        else:
            expect(field).to_have_value(value)
        assert page.locator('[data-resource-row=' + json.dumps(name) + '] > label').evaluate('(element) => element.firstChild.textContent') == label
    expect(page.locator('[data-resource="Mode"]')).to_have_count(0)
    expect(page.locator('[data-resource="BuildVersion"]')).to_have_count(0)
    assert page.locator('[data-resource^="ServiceInfo_"]').count() == 0
    page.locator('[data-resource="DiagLog"]').check()
    assert json.loads(page.locator('#parameters').input_value())['DiagLog'] == 'true'
    page.locator('[data-resource="DiagLog"]').press('Space')
    expect(page.locator('[data-resource="DiagLog"]')).not_to_be_checked()
    assert json.loads(page.locator('#parameters').input_value())['DiagLog'] == 'false'
    page.locator('[data-resource="DiagLog"]').check()
    page.locator('[data-resource-row="DiagLog"] button').click()
    expect(page.locator('[data-resource="DiagLog"]')).not_to_be_checked()
    assert 'DiagLog' not in json.loads(page.locator('#parameters').input_value())
    page.locator('[data-resource="DiagLog"]').check()
    for name, value in {'UberService': 'ot', 'UberCountry': 'de', 'UberProxyCountry': 'us', 'UberProvider': '42', 'Thread': '3'}.items():
        page.locator('[data-resource=' + json.dumps(name) + ']').fill(value)
    overrides = {'DiagLog': 'true', 'UberService': 'ot', 'UberCountry': 'de', 'UberProxyCountry': 'us', 'UberProvider': '42', 'Thread': 3}
    assert json.loads(page.locator('#parameters').input_value()) == overrides
    page.locator('#name').fill('BAS native inputs acceptance')
    page.locator('#save').click()
    expect(page.locator('.project')).to_have_count(2)
    page.reload()
    page.locator('.project').filter(has_text='BAS native inputs acceptance').click()
    expect(page.locator('[data-resource="DiagLog"]')).to_be_checked()
    expect(page.locator('[data-resource="Thread"]')).to_have_value('3')
    assert json.loads(page.locator('#parameters').input_value()) == overrides
    for theme in ('graphite', 'graphite-light'):
        page.evaluate('theme => document.documentElement.dataset.theme = theme', theme)
        page.screenshot(path=str(output / ('bas-native-inputs-' + theme + '.png')), full_page=True)
    assert not errors, errors
    browser.close()
assert hashlib.sha256(source.read_bytes()).digest() == original
print('PASS actual Uber XML: exactly six native input labels/defaults; all editable; select/reset/save/reload; source unchanged; script never run')
