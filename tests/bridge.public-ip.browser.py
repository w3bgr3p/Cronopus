import json,subprocess,time,urllib.request
from pathlib import Path
from playwright.sync_api import sync_playwright
proof=Path('.superpowers/public-ip');proof.mkdir(parents=True,exist_ok=True)
fixture=Path('.superpowers/sdd/2026-10-15-tasker-remote-bridge/fixture.json')
fixture.parent.mkdir(parents=True,exist_ok=True)
started=time.time();log=open(proof/'fixture.log','w',encoding='utf-8')
process=subprocess.Popen([str(Path('tests/Bridge.Integration/bin/Debug/net10.0/win-x64/Bridge.Integration.exe').resolve()),'--serve'],stdout=log,stderr=log,creationflags=subprocess.CREATE_NO_WINDOW)
def server_state(base):
    req=urllib.request.Request(base+'/bridge-local/server',headers={'X-Bridge-Local':'1'})
    with urllib.request.urlopen(req,timeout=10) as response:return json.load(response)
try:
    for attempt in range(60):
        if fixture.exists() and fixture.stat().st_mtime>=started:break
        if process.poll() is not None:raise RuntimeError('Fixture exited; see '+str(proof/'fixture.log'))
        time.sleep(1)
    else:raise RuntimeError('Fixture did not become ready')
    base=json.loads(fixture.read_text())['local_url'];before=server_state(base)
    with sync_playwright() as p:
        browser=p.chromium.launch(headless=True,executable_path='C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe')
        page=browser.new_page(viewport={'width':1450,'height':1000})
        page.goto(base+'/?page=config');page.locator('[data-sec="remote-tasker"].cfg-rail-item').click()
        page.wait_for_function("document.querySelector('#br_advertisedAddress').value!=='127.0.0.1' && !document.querySelector('#brDetectIp').disabled",timeout=15000)
        address=page.locator('#br_advertisedAddress').input_value();assert len(address.split('.'))==4
        after=server_state(base);assert after['settings']==before['settings'] and after['fingerprint']==before['fingerprint']
        assert 'Apply server settings' in page.locator('#brIpStatus').inner_text()
        page.locator('#brDetectIp').click();page.wait_for_function("!document.querySelector('#brDetectIp').disabled",timeout=15000)
        for theme in ['graphite','graphite-light']:
            page.evaluate('theme=>document.documentElement.dataset.theme=theme',theme);page.screenshot(path=str(proof/(theme+'.png')),full_page=True)
        browser.close()
    print(json.dumps({'public_ip_browser':'PASS','auto_detect':True,'manual_detect':True,'settings_unchanged':True,'certificate_unchanged':True}))
finally:
    if process.poll() is None:process.terminate();process.wait(timeout=20)
    log.close()
