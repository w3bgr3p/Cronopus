const test=require('node:test'),assert=require('node:assert/strict'),vm=require('node:vm'),fs=require('node:fs');
function fixture(address='127.0.0.1',enabled=false){
    const elements={},events={},calls=[];let finish;
    const settings={enabled,advertisedAddress:address,bindAddress:'0.0.0.0',port:38473,uploadQuotaBytes:42949672960,transferBytesPerSecond:8388608};
    const c={console,document:{getElementById(id){return elements[id]||(elements[id]={value:'',textContent:'',disabled:false});},addEventListener(name,fn){events[name]=fn;}},
        fetch:async(path,options)=>{calls.push({path,options});return {ok:true,json:async()=>path.endsWith('/public-ip')?new Promise(r=>finish=r):path.endsWith('/server')?{settings,nodeId:'node',keys:[]}:[]};}};
    c.window=c;vm.createContext(c);vm.runInContext(fs.readFileSync('wwwroot/js/bridge-config.js','utf8'),c);
    return {c,elements,events,calls,finish(value){finish(value);}};
}
test('first setup detects IP into the draft without saving or enabling the server',async()=>{
    const f=fixture();await f.c.BridgeConfig.load();await new Promise(setImmediate);f.finish({address:'93.184.216.34'});await new Promise(setImmediate);
    assert.equal(f.elements.br_advertisedAddress.value,'93.184.216.34');
    assert.equal(f.elements.brEnabled.checked,false);assert.equal(f.calls.some(c=>c.options.method!=='GET'),false);
});
test('a late IP result cannot replace a manual address edit',async()=>{
    const f=fixture();await f.c.BridgeConfig.load();await new Promise(setImmediate);f.elements.br_advertisedAddress.value='198.51.100.10';
    f.events.input({target:{id:'br_advertisedAddress'}});f.finish({address:'93.184.216.34'});await new Promise(setImmediate);
    assert.equal(f.elements.br_advertisedAddress.value,'198.51.100.10');assert.equal(f.elements.brDetectIp.disabled,false);
});
test('configured server address is preserved without automatic discovery',async()=>{
    const f=fixture('198.51.100.10',true);await f.c.BridgeConfig.load();
    assert.equal(f.elements.br_advertisedAddress.value,'198.51.100.10');assert.equal(f.calls.some(c=>c.path.endsWith('/public-ip')),false);
});
