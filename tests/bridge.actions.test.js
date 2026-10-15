const test=require('node:test'),assert=require('node:assert/strict'),vm=require('node:vm'),fs=require('node:fs');
function fixture(server){
    const elements={},events={},calls=[];
    const c={Response,document:{getElementById(id){return elements[id]||(elements[id]={value:'',textContent:'',disabled:false,checked:false});},addEventListener(event,fn){events[event]=fn;}},
        fetch:async(path,options)=>{calls.push({path,options});return new Response(JSON.stringify(path.endsWith('/connections')?[]:server));}};
    c.window=c;vm.createContext(c);vm.runInContext(fs.readFileSync('wwwroot/js/bridge-config.js','utf8'),c);return {c,elements,events,calls};
}
const server={settings:{enabled:false,advertisedAddress:'198.51.100.10',bindAddress:'0.0.0.0',port:38473,uploadQuotaBytes:42949672960,transferBytesPerSecond:8388608},keys:[],firewall:{supported:true,port:38473,exists:false,open:false}};
test('missing backend route gives an explicit application-update error',async()=>{
    const f=fixture(server);f.c.fetch=async()=>new Response('No route',{status:404});
    await assert.rejects(f.c.BridgeConfig.send('GET','/bridge-local/server/firewall'),/running z3nDash backend.*Restart the updated application/);
});
test('older backend disables unsupported firewall actions with an explanation',async()=>{
    const old={...server};delete old.firewall;const f=fixture(old);await f.c.BridgeConfig.load();
    for(const id of ['brOpenFirewall','brCloseFirewall','brCheckFirewall'])assert.equal(f.elements[id].disabled,true);
    assert.match(f.elements.brFirewallStatus.textContent,/Restart the updated z3nDash/);
});
test('Create sends once, displays its key, and preserves unsaved server fields',async()=>{
    const f=fixture(server);await f.c.BridgeConfig.load();let finish,count=0;
    f.elements.brEnabled.checked=true;f.elements.br_port.value='41237';f.c.document.getElementById('brKeyName').value='Test';
    const original=f.c.fetch;f.c.fetch=async(path,options)=>{if(path.endsWith('/keys')){count++;return new Promise(r=>finish=()=>r(new Response(JSON.stringify({id:'key-id',secret:'test-secret'}))));}return original(path,options);};
    const button=f.c.document.getElementById('brCreateKey');button.id='brCreateKey';button.dataset={};button.textContent='Create access key';
    const event={target:{closest:()=>button}};const pending=f.events.click(event);await f.events.click(event);
    assert.equal(button.disabled,true);assert.equal(count,1);finish();await pending;
    assert.equal(f.elements.brNewKey.value,'test-secret');assert.equal(f.elements.brEnabled.checked,true);assert.equal(f.elements.br_port.value,'41237');
    assert.match(f.elements.brKeyStatus.textContent,/Access key created/);assert.equal(button.disabled,false);
});
