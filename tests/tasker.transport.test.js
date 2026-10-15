const test=require('node:test');
const assert=require('node:assert/strict');
const {createTaskerTransport}=require('../wwwroot/js/tasker-transport.js');
test('responses from a previous node context become stale',()=>{
    const t=createTaskerTransport({send:async()=>({})});
    t.selectNode('A');const old=t.context();t.selectNode('B');
    assert.equal(t.isCurrent(old),false);
});
