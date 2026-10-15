import json,subprocess,time,urllib.request
from pathlib import Path
from playwright.sync_api import sync_playwright
proof=Path('.superpowers/actions-live');proof.mkdir(parents=True,exist_ok=True)
fixture=Path('.superpowers/sdd/2026-10-15-tasker-remote-bridge/fixture.json');fixture.parent.mkdir(parents=True,exist_ok=True)
started=time.time();log=open(proof/'fixture.log','w',encoding='utf-8')
process=subprocess.Popen([str(Path('tests/Bridge.Integration/bin/Debug/net10.0/win-x64/Bridge.Integration.exe').resolve()),'--serve'],stdout=log,stderr=log,creationflags=subprocess.CREATE_NO_WINDOW)
def state(base):
    req=urllib.request.Request(base+'/bridge-local/server',headers={'X-Bridge-Local':'1'})
    with urllib.request.urlopen(req,timeout=10) as response:return json.load(response)
try:
    for _ in range(60):
        if fixture.exists() and fixture.stat().st_mtime>=started:break
        if process.poll() is not None:raise RuntimeError('Fixture failed')
        time.sleep(1)
    else:raise RuntimeError('Fixture timeout')
    base=json.loads(fixture.read_text())['local_url'];calls=[]
    with sync_playwright() as p:
        browser=p.chromium.launch(headless=True,executable_path='C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe')
        page=browser.new_page(viewport={'width':1450,'height':1050})
        page.on('response',lambda r:calls.append((r.request.method,r.url.split(base)[-1],r.status)) if '/bridge-local/' in r.url else None)
        page.goto(base+'/?page=config');page.locator('[data-sec="remote-tasker"].cfg-rail-item').click()
        page.wait_for_function("document.querySelector('#brIdentity').textContent.includes('Node ID:') && !document.querySelector('#brDetectIp').disabled")
        page.locator('#brEnabled').check();page.locator('#br_port').fill('41237');page.locator('#brKeyName').fill('Live UI key')
        before=len(state(base)['keys']);page.locator('#brCreateKey').click()
        page.wait_for_function("document.querySelector('#brNewKey').value.length>40")
        assert page.locator('#brEnabled').is_checked(),'Creating a key reset the unsaved Enable HTTPS setting'
        assert page.locator('#br_port').input_value()=='41237','Creating a key reset the unsaved HTTPS port'
        assert len(state(base)['keys'])==before+1
        page.locator('#brCheckFirewall').click()
        page.wait_for_function("document.querySelector('#brFirewallStatus').textContent.includes('Checked')")
        assert ('GET','/bridge-local/server/firewall',200) in calls
        assert ('POST','/bridge-local/keys',200) in calls
        for theme in ['graphite','graphite-light']:
            page.evaluate('theme=>document.documentElement.dataset.theme=theme',theme)
            page.locator('#brNewKey').evaluate("el=>el.value='[test key hidden]' ")
            page.screenshot(path=str(proof/(theme+'.png')),full_page=True)
        browser.close()
    print(json.dumps({'real_backend_buttons':'PASS','check_firewall_http':200,'create_key_http':200,'draft_preserved':True,'requests':len(calls)}))
finally:
    if process.poll() is None:process.terminate();process.wait(timeout=20)
    log.close()
