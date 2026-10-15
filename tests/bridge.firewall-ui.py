import json,threading
from pathlib import Path
from http.server import ThreadingHTTPServer,SimpleHTTPRequestHandler
from playwright.sync_api import sync_playwright
class Handler(SimpleHTTPRequestHandler):
    def __init__(self,*a,**k):super().__init__(*a,directory=str(Path('wwwroot').resolve()),**k)
    def log_message(self,*a):pass
try:server=ThreadingHTTPServer(('127.0.0.1',39293),Handler)
except OSError:server=ThreadingHTTPServer(('127.0.0.1',0),Handler)
threading.Thread(target=server.serve_forever,daemon=True).start()
state={'supported':True,'port':39291,'ruleName':'fixture','exists':False,'open':False,'error':None};calls=[]
settings=dict(enabled=True,advertisedAddress='93.184.216.34',bindAddress='0.0.0.0',port=38473,uploadQuotaBytes=42949672960,transferBytesPerSecond=8388608)
def api(route):
    path=route.request.url.split('/bridge-local/',1)[1];method=route.request.method
    if path=='connections':route.fulfill(json=[]);return
    if path=='server/firewall':
        if method=='POST':
            body=route.request.post_data_json;calls.append(body);state.update(exists=body['action']=='open',open=body['action']=='open')
        route.fulfill(json=state);return
    if path=='server':
        if method=='PUT':calls.append(route.request.post_data_json)
        route.fulfill(json={'settings':settings,'nodeId':'node','fingerprint':'a'*64,'keys':[],'firewall':state});return
    route.fulfill(json={})
try:
    with sync_playwright() as p:
        browser=p.chromium.launch(headless=True,executable_path='C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe')
        page=browser.new_page(viewport={'width':1450,'height':1000});page.route('**/bridge-local/**',api)
        page.goto('http://127.0.0.1:'+str(server.server_port)+'/config.html')
        page.evaluate('BridgeConfig.load()');page.locator('[data-sec="remote-tasker"].cfg-rail-item').click()
        page.wait_for_function("document.querySelector('#brFirewallStatus').textContent.includes('39291')")
        assert page.locator('#brCloseFirewall').is_disabled()
        page.locator('#brOpenFirewall').click();page.wait_for_function("document.querySelector('#brFirewallStatus').textContent.includes('is open')")
        assert calls[-1]=={'action':'open'}
        page.locator('#brCloseFirewall').click();page.wait_for_function("document.querySelector('#brFirewallStatus').textContent.includes('No ZenDash')")
        assert calls[-1]=={'action':'close'}
        page.locator('#brApplyServer').click();page.wait_for_function("document.querySelector('#brServerMessage').textContent==='Server settings applied'")
        assert calls[-1]['openFirewall'] is True
        proof=Path('.superpowers/firewall-ui');proof.mkdir(parents=True,exist_ok=True)
        for theme in ['graphite','graphite-light']:
            page.evaluate('theme=>document.documentElement.dataset.theme=theme',theme);page.screenshot(path=str(proof/(theme+'.png')),full_page=True)
        browser.close();print(json.dumps({'firewall_ui':'PASS','checks':['actual-port','open','close','apply-opens-firewall']}))
finally:server.shutdown();server.server_close()
