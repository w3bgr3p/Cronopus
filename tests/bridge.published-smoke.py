import json,os,shutil,subprocess,time,tempfile,urllib.request
from pathlib import Path
root=Path(tempfile.mkdtemp(prefix='z3ndash-published-e2e-'))
publish=Path(os.environ.get('BRIDGE_PUBLISH_PATH',str(Path.home()/'AppData/Local/Temp/z3ndash-bridge-publish')))
runner=Path.home()/'AppData/Local/Temp/z3ndash-bridge-runner/Bridge.Integration.exe'
for name in ['local','remote']:
    shutil.copytree(publish,root/name,copy_function=os.link,ignore=shutil.ignore_patterns('appsettings.secrets.json','crash.log'))
subprocess.run(['rtk','proxy',str(runner),'--setup-e2e',str(root)],check=True)
fixture=json.loads((root/'fixture.json').read_text())
processes=[]
def request(base,path,body=None):
    data=None if body is None else json.dumps(body).encode()
    req=urllib.request.Request(base+path,data=data,headers={'Content-Type':'application/json','X-Bridge-Local':'1'})
    with urllib.request.urlopen(req,timeout=20) as r:return json.load(r)
try:
    for name in ['remote','local']:
        env=dict(os.environ,Z3NDASH_BRIDGE_STATE_DIR=str(root/(name+'-state')))
        log=open(root/(name+'.log'),'w',encoding='utf-8')
        processes.append(subprocess.Popen([str(root/name/'z3nDash.exe'),'--headless'],cwd=root/name,env=env,stdout=log,stderr=log,creationflags=subprocess.CREATE_NO_WINDOW))
    for attempt in range(90):
        try:
            request(fixture['local_url'],'/bridge-local/connections')
            request('http://localhost:39221','/tasker/list');break
        except Exception:time.sleep(1)
    else:raise RuntimeError('Published processes did not become ready; '+str(root))
    task=lambda id,name:dict(id=id,name=name,enabled='false',schedule_mode='off',executor='python',script_path=str(root/'test.py'))
    request(fixture['local_url'],'/tasker/save',task('local-only','LOCAL SENTINEL'))
    request('http://localhost:39221','/tasker/save',task('remote-only','REMOTE SENTINEL'))
    base='/bridge-local/'+fixture['connection_id']+'/api/'
    tasks=request(fixture['local_url'],base+'tasks')['items']
    assert [t['id'] for t in tasks]==['remote-only'],tasks
    env=dict(os.environ,BRIDGE_FIXTURE_PATH=str(root/'fixture.json'))
    subprocess.run(['rtk','proxy','python','tests/bridge.browser-smoke.py'],check=True,env=env)
    detail=request(fixture['local_url'],base+'tasker/detail?id=remote-only')
    request(fixture['local_url'],base+'tasker/payload',dict(id='remote-only',schema='[]',values='{}',_revision=detail['_revision']))
    request(fixture['local_url'],base+'tasker/run',dict(id='remote-only',count=1))
    time.sleep(2)
    for action in ['pause','resume','stop']:
        request(fixture['local_url'],base+'tasker/'+action,dict(id='remote-only'))
    assert [t['name'] for t in request(fixture['local_url'],'/tasker/list')]==['LOCAL SENTINEL']
    processes[0].terminate();processes[0].wait(timeout=20)
    env=dict(os.environ,Z3NDASH_BRIDGE_STATE_DIR=str(root/'remote-state'))
    log=open(root/'remote-restarted.log','w',encoding='utf-8')
    processes[0]=subprocess.Popen([str(root/'remote'/'z3nDash.exe'),'--headless'],cwd=root/'remote',env=env,stdout=log,stderr=log,creationflags=subprocess.CREATE_NO_WINDOW)
    for attempt in range(30):
        try:
            rows=request(fixture['local_url'],base+'tasks')['items'];assert rows[0]['id']=='remote-only';break
        except Exception:time.sleep(1)
    else:raise RuntimeError('Remote state did not survive restart')
    print(json.dumps({'published_two_processes':'PASS','root':str(root),'pids':[p.pid for p in processes]}),flush=True)
finally:
    for process in processes:
        if process.poll() is None:process.terminate();process.wait(timeout=20)
