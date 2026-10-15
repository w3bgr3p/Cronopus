const test=require('node:test'),assert=require('node:assert/strict');
const {tailLines}=require('../wwwroot/js/tasker-remote-output.js');
test('remote output keeps at most two thousand lines',()=>{
 const lines=Array.from({length:3000},(_,i)=>i);const tail=tailLines(lines);
 assert.equal(tail.length,2000);assert.equal(tail[0],1000);
});
