import json,threading
from pathlib import Path
from http.server import ThreadingHTTPServer,SimpleHTTPRequestHandler
from playwright.sync_api import sync_playwright

class Handler(SimpleHTTPRequestHandler):
    def __init__(self,*args,**kwargs):super().__init__(*args,directory=str(Path('wwwroot').resolve()),**kwargs)
    def log_message(self,*args):pass
try:server=ThreadingHTTPServer(('127.0.0.1',39287),Handler)
except OSError:server=ThreadingHTTPServer(('127.0.0.1',0),Handler)
threading.Thread(target=server.serve_forever,daemon=True).start()
url='http://127.0.0.1:'+str(server.server_port)+'/tasker.html'
print('Modal browser fixture: '+url,flush=True)
posts=[];errors=[];fail=[True]
def api(route):
    request=route.request;path=request.url.split('/bridge-local/',1)[1]
    if path=='connections' and request.method=='POST':
        posts.append(request.post_data_json)
        if fail[0]:route.fulfill(status=400,json={'error':'SHA-256 fingerprint required'});return
        route.fulfill(json={'id':'new-server','name':'Modal Server','address':posts[-1]['address'],'hasKey':True});return
    route.fulfill(json=[] if path=='connections' else {'items':[],'next_cursor':None,'change_cursor':'epoch:0'})
try:
    with sync_playwright() as p:
        browser=p.chromium.launch(headless=True,executable_path='C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe')
        page=browser.new_page(viewport={'width':1280,'height':900});page.on('pageerror',lambda e:errors.append(str(e)))
        page.route('**/bridge-local/**',api)
        page.route('**/tasker/**',lambda route:route.fulfill(json=[] if '/list' in route.request.url else {'Tasks':{}}))
        page.route('**/ai/cwd',lambda route:route.fulfill(json={}))
        page.goto(url);page.wait_for_function("typeof TaskerBridge!=='undefined'")
        page.evaluate("formDirty=true;taskDraft={id:'draft'}")
        page.select_option('#bridgeNode','__new__');page.wait_for_selector('.br-connection-dialog')
        assert page.locator('#bridgeNode').input_value()==''
        page.keyboard.press('Escape');assert page.evaluate("formDirty && taskDraft.id==='draft'")
        page.evaluate('formDirty=false');page.select_option('#bridgeNode','__new__')
        values=dict(name='Modal Server',address='https://192.0.2.10:38473',secret='fixture-secret',nodeId='fixture-node',fingerprint='invalid')
        for key,value in values.items():page.locator('.br-connection-dialog [name="'+key+'"]').fill(value)
        page.get_by_role('button',name='Add connection',exact=True).click()
        page.get_by_role('status').filter(has_text='SHA-256 fingerprint required').wait_for()
        assert page.locator('[name="secret"]').input_value()=='fixture-secret'
        output=Path('.superpowers/connection-modal');output.mkdir(parents=True,exist_ok=True)
        for theme in ['graphite','graphite-light']:
            page.evaluate('theme=>document.documentElement.dataset.theme=theme',theme)
            page.screenshot(path=str(output/(theme+'.png')))
        fail[0]=False;page.locator('[name="fingerprint"]').fill('a'*64)
        page.get_by_role('button',name='Add connection',exact=True).click()
        page.wait_for_selector('.br-connection-dialog',state='detached')
        page.wait_for_function("TaskerBridge.context().nodeId==='new-server'")
        assert page.locator('#bridgeNode').input_value()=='new-server'
        assert page.locator('#bridgeNode option').last.get_attribute('value')=='__new__'
        assert page.locator('#bridgeNode option[value="new-server"]').count()==1
        assert len(posts)==2 and posts[-1]['secret']=='fixture-secret'
        assert page.locator('[name="secret"]').count()==0
        generation=page.evaluate('TaskerBridge.context().generation')
        page.select_option('#bridgeNode','__new__');page.get_by_role('button',name='Cancel',exact=True).click()
        assert page.locator('#bridgeNode').input_value()=='new-server'
        assert page.evaluate('TaskerBridge.context().generation')==generation
        assert not errors,errors
        browser.close();print(json.dumps({'connection_modal':'PASS','checks':['cancel-keeps-draft','validation-keeps-form','save-adds-selects-node','cancel-keeps-remote','secret-cleared'],'errors':errors}))
finally:server.shutdown();server.server_close()
