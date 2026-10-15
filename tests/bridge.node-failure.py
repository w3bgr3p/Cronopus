import json,threading
from pathlib import Path
from http.server import ThreadingHTTPServer,SimpleHTTPRequestHandler
from playwright.sync_api import sync_playwright
class Handler(SimpleHTTPRequestHandler):
    def __init__(self,*a,**k):super().__init__(*a,directory=str(Path('wwwroot').resolve()),**k)
    def log_message(self,*a):pass
try:server=ThreadingHTTPServer(('127.0.0.1',39289),Handler)
except OSError:server=ThreadingHTTPServer(('127.0.0.1',0),Handler)
threading.Thread(target=server.serve_forever,daemon=True).start()
local=[dict(id='local-one',name='LOCAL TASK ONE',executor='python',schedule_mode='off',status='running'),dict(id='local-two',name='LOCAL TASK TWO',executor='python',schedule_mode='off')]
def api(route):
    if route.request.url.endswith('/connections'):route.fulfill(json=[{'id':'offline','name':'Offline server'}]);return
    route.fulfill(status=500,json={'error':'Server is unreachable (fixture)'})
try:
    with sync_playwright() as p:
        browser=p.chromium.launch(headless=True,executable_path='C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe')
        page=browser.new_page(viewport={'width':1400,'height':900})
        page.add_init_script("window.testStreams=[];window.EventSource=class{constructor(){this.handlers={};testStreams.push(this);}addEventListener(name,fn){this.handlers[name]=fn;}close(){}fire(name,data){if(this.handlers[name])this.handlers[name]({data:JSON.stringify(data)});}}")
        page.route('**/bridge-local/**',api)
        page.route('**/tasker/**',lambda r:r.fulfill(json=local if '/list' in r.request.url else {'Tasks':{},'output':''}))
        page.route('**/ai/cwd',lambda r:r.fulfill(json={}))
        page.goto('http://127.0.0.1:'+str(server.server_port)+'/tasker.html')
        page.wait_for_function("schedules.length===2&&document.querySelector('#bridgeNode').options.length===3")
        page.evaluate("selectedId='local-one';_getOutputBox().textContent='LOCAL OUTPUT';document.getElementById('bottomPanels').style.display='flex';startSseOutput(selectedId)")
        page.select_option('#bridgeNode','offline')
        page.wait_for_function("TaskerBridge.context().nodeId==='offline'&&document.querySelector('#bridgeStatus').textContent.includes('unreachable')")
        assert 'LOCAL TASK' not in page.locator('#listScroll').inner_text(),'Local tasks remain visible on an unreachable remote node'
        assert page.locator('#listCount').inner_text()=='0'
        assert 'LOCAL OUTPUT' not in page.evaluate('_getOutputBox().textContent')
        page.evaluate("testStreams.forEach(s=>s.fire('output',{line:'LATE LOCAL OUTPUT',level:'INFO'}))")
        page.wait_for_timeout(100)
        assert 'LATE LOCAL OUTPUT' not in page.evaluate('_getOutputBox().textContent')
        page.select_option('#bridgeNode','');page.wait_for_function("schedules.length===2&&document.querySelector('#listScroll').textContent.includes('LOCAL TASK ONE')")
        browser.close();print(json.dumps({'remote_failure_isolation':'PASS'}))
finally:server.shutdown();server.server_close()
