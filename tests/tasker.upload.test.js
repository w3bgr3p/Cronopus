const test=require('node:test'),assert=require('node:assert/strict');
const {createUploadController}=require('../wwwroot/js/tasker-upload.js');
test('a transfer stays attached to its captured connection',async()=>{
 const calls=[];const upload=createUploadController({send:async(path,body)=>{calls.push({path,body});return {id:'transfer'};}});
 await upload.start({connectionId:'A',destination:'D:\\Scripts',selectionId:'local-file'});
 assert.equal(calls[0].path,'/bridge-local/A/transfers');
 assert.equal(calls[0].body.selectionId,'local-file');
});
