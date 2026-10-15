const test=require('node:test'),assert=require('node:assert/strict');
const {initialFolder}=require('../wwwroot/js/tasker-files.js');
test('server path remains a server path without client normalization',()=>{
    assert.equal(initialFolder('D:\\Scripts\\job.py','C:\\Old'), 'D:\\Scripts');
    assert.equal(initialFolder('', 'D:\\Scripts'), 'D:\\Scripts');
});
