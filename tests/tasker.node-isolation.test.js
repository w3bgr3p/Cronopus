const test=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),vm=require('node:vm');
function tasker(){
    const c={console,document:{addEventListener(){}},window:{addEventListener(){}},fetch:()=>new Promise(()=>{}),setInterval(){},clearInterval(){},setTimeout,clearTimeout,URLSearchParams};
    vm.createContext(c);vm.runInContext(fs.readFileSync('wwwroot/js/tasker.js','utf8'),c);return c;
}
test('bulk mutation stops when the initiating node changes',async()=>{
    const c=tasker();let node='A';const calls=[];
    c.window.TaskerBridge={context:()=>({node}),isCurrent:x=>x.node===node};
    c._postTask=async(_url,body)=>{calls.push({node,id:body.id});node='B';return 'Node changed';};
    await c._forSelected([{id:'one'},{id:'two'}],'/tasker/run',s=>({id:s.id}));
    assert.deepEqual(calls,[{node:'A',id:'one'}]);
});
test('From Script uses the Tasker transport and folder prompt exists',async()=>{
    const c=tasker();const calls=[];
    c.pmScheduleId='remote';c.taskDraft={id:'remote',executor:'python',script_path:'D:/server.py'};
    c.taskerFetch=async url=>{calls.push(url);return {ok:true,json:async()=>({ok:true,params:[]})};};
    c.renderPayloadFields=()=>{};c.Dialog.alert=()=>{};
    await c.importSchemaFromScript();
    assert.equal(calls.length,1);assert.match(calls[0],/server.py/);assert.equal(typeof c.Dialog.prompt,'function');
});
test('late local response is rejected after switching to a server',async()=>{
    let finish,change;const selector={value:'B',addEventListener(_event,fn){change=fn;},appendChild(){}};
    const c={console,Response,DOMException,URLSearchParams,Map,Set,crypto:require('node:crypto').webcrypto,
        document:{body:{dataset:{}},getElementById:id=>id==='bridgeNode'?selector:{style:{},textContent:''},addEventListener(_event,fn){this.ready=fn;},createElement:()=>({})},
        fetch:async url=>url==='/bridge-local/connections'?new Response('[]'):new Promise(r=>finish=r),
        localStorage:{getItem:()=>null,setItem(){}},dispatchEvent(){},CustomEvent:class{},loadList:async()=>{}};
    c.window=c;vm.createContext(c);vm.runInContext(fs.readFileSync('wwwroot/js/tasker-transport.js','utf8'),c);await c.document.ready();
    const pending=c.TaskerBridge.fetch('/tasker/list');change();finish(new Response('[]'));
    await assert.rejects(pending,{name:'AbortError'});
});
test('payload from a previous node cannot open or save on the new node',async()=>{
    const c=tasker();let node='A',reject,opened=false,posts=0;
    c.window.TaskerBridge={context:()=>({node}),isCurrent:x=>x.node===node};
    c.document.getElementById=()=>({classList:{add(){opened=true;},remove(){}},style:{}});
    c.taskerFetch=async(_url,options)=>{if(options){posts++;return;}return new Promise((_r,j)=>reject=j);};
    const opening=c.openPayloadEditor('same-id','A payload');node='B';reject(Object.assign(new Error('Node changed'),{name:'AbortError'}));
    await opening;await c.savePayload();assert.equal(opened,false);assert.equal(posts,0);
});
