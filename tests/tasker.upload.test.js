const test=require('node:test'),assert=require('node:assert/strict');
const {createUploadController}=require('../wwwroot/js/tasker-upload.js');
test('a transfer stays attached to its captured connection',async()=>{
 const calls=[];const upload=createUploadController({send:async(path,body)=>{calls.push({path,body});return {id:'transfer'};}});
 await upload.start({connectionId:'A',destination:'D:\\Scripts',selectionId:'local-file'});
 assert.equal(calls[0].path,'/bridge-local/A/transfers');
 assert.equal(calls[0].body.selectionId,'local-file');
});
test('task upload sends a captured task directly to the selected server',async()=>{
 const calls=[];const upload=createUploadController({send:async(path,body)=>{calls.push({path,body});return {id:'transfer'};}});
 const task={id:'local',name:'Edited name',payload_values:'{"count":2}',enabled:'true'};
 assert.equal(typeof upload.startTask,'function');
 await upload.startTask({connectionId:'B',destination:'D:\\Tasks',task});
 assert.equal(calls[0].path,'/bridge-local/B/task-upload');
 assert.deepEqual(calls[0].body,{destination:'D:\\Tasks',task});
});
