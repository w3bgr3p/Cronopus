import json
from uuid import uuid4
from pathlib import Path
from playwright.sync_api import sync_playwright

fixture_path = Path('.superpowers/sdd/2026-10-15-tasker-remote-bridge/fixture.json')
fixture = json.loads(fixture_path.read_text())
proof = Path('.superpowers/task-upload')
proof.mkdir(parents=True, exist_ok=True)
name = 'BROWSER UPLOADED COPY '+uuid4().hex[:8]
destination = Path(fixture['root']) / ('browser-task-upload-'+uuid4().hex[:8])
destination.mkdir(exist_ok=True)
with sync_playwright() as p:
    edge = Path('C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe')
    browser = p.chromium.launch(headless=True, executable_path=str(edge) if edge.exists() else None)
    page = browser.new_page(viewport={'width': 1500, 'height': 1000})
    errors = []
    page.on('pageerror', lambda error: errors.append(str(error)))
    base = fixture['local_url']
    payload = {'id': 'local-only', 'schema': '[{"name":"text","type":"string"}]', 'values': '{"text":"тест payload"}'}
    assert page.request.post(base+'/tasker/payload', data=payload).json()['ok']
    page.goto(base+'/?page=tasker')
    page.wait_for_function("typeof schedules!=='undefined' && schedules.some(s=>s.id==='local-only')")
    page.evaluate("selectRow('local-only',false,'settings')")
    page.locator('#f_name').fill(name)
    page.locator('#f_args').fill('--example current')
    page.locator('#f_max_threads').fill('4')
    page.locator('#f_timeout_seconds').fill('73')
    page.locator('#f_timeout_seconds').blur()
    page.get_by_role('button', name='↑ Upload', exact=True).click()
    page.wait_for_selector('.br-task-upload select option', state='attached')
    assert page.locator('[aria-label="Upload server"]').input_value() == fixture['connection_id']
    for theme in ['graphite', 'graphite-light']:
        page.evaluate('theme=>document.documentElement.dataset.theme=theme', theme)
        page.screenshot(path=str(proof/(theme+'.png')), full_page=True)
    page.get_by_role('button', name='Choose folder…').click()
    page.wait_for_function("document.querySelector('.br-file-dialog p').textContent!== 'Loading…'")
    page.locator('.br-file-toolbar input').fill(str(destination))
    page.locator('[data-go]').click()
    page.wait_for_function("document.querySelector('.br-file-dialog p').textContent!== 'Loading…'")
    assert page.locator('.br-file-dialog p').inner_text() == '', page.locator('.br-file-dialog').inner_text()
    assert page.locator('[data-select]').is_enabled(), page.locator('.br-file-toolbar input').input_value()
    page.locator('[data-select]').click()
    assert page.locator('[aria-label="Upload destination"]').input_value() == str(destination)
    assert page.locator('#bridgeNode').input_value() == ''
    page.locator('.br-task-upload button[type="submit"]').click()
    page.wait_for_function("Array.from(document.querySelectorAll('.br-transfer')).some(el=>el.textContent.includes('Task created on server'))", timeout=30000)
    prefix = base+'/bridge-local/'+fixture['connection_id']+'/api/'
    headers = {'X-Bridge-Local': '1'}
    tasks = page.request.get(prefix+'tasks?limit=500', headers=headers).json()['items']
    copies = [task for task in tasks if task['name'] == name]
    assert len(copies) == 1, copies
    copy = page.request.get(prefix+'tasker/detail?id='+copies[0]['id'], headers=headers).json()
    for key, value in {'args': '--example current', 'max_threads': '4', 'timeout_seconds': '73', 'payload_schema': payload['schema'], 'payload_values': payload['values']}.items():
        assert copy[key] == value, (key, copy[key])
    assert copy['script_path'] == str(destination/'test.py')
    assert (destination/'test.py').read_bytes() == (Path(fixture['root'])/'test.py').read_bytes()
    local = page.request.get(base+'/tasker/detail?id=local-only').json()
    assert local['name'] == 'LOCAL SENTINEL', local
    assert page.evaluate('formDirty && selectedId==="local-only"')
    page.screenshot(path=str(proof/'completed.png'), full_page=True)
    assert not errors, errors
    browser.close()
    print('PASS browser: Upload modal, server folder, unsaved settings, file bytes, payload, original task preserved, Graphite/Aluminium')
