(function(root){
 'use strict';
 function createUploadController(deps){return{start:function(o){return deps.send('/bridge-local/'+encodeURIComponent(o.connectionId)+'/transfers',{selectionId:o.selectionId,destination:o.destination});}};}
 function formatBytes(value){var n=Math.max(0,Number(value)||0),units=['B','KiB','MiB','GiB'],u=0;while(n>=1024&&u<units.length-1){n/=1024;u++;}return Number(n.toFixed(u?1:0))+' '+units[u];}
 function transferSummary(job){
  if(job.state!=='completed')return ({hashing:'Checking files',uploading:'Uploading',committing:'Saving on server',awaiting_conflicts:'Confirm replacement',failed:'Upload failed',interrupted:'Upload interrupted',cancelled:'Cancelled'})[job.state]||job.state;
  if(!Array.isArray(job.results))return 'Transfer finished; result details unavailable';
  return 'Saved on server: '+job.results.filter(function(r){return r.state==='published';}).length+' · Left unchanged: '+job.results.filter(function(r){return r.state==='skipped';}).length;
 }
 if(typeof module!=='undefined')module.exports={createUploadController:createUploadController,formatBytes:formatBytes,transferSummary:transferSummary};
 if(typeof document==='undefined')return;
 async function send(path,body,method){var response=await fetch(path,{method:method||'POST',headers:{'Content-Type':'application/json','X-Bridge-Local':'1'},body:body===undefined?undefined:JSON.stringify(body)});var data=await response.json();if(!response.ok)throw new Error(data.error||'Transfer request failed');return data;}
 var controller=createUploadController({send:send}),active=new Map();
 function remember(id){var ids=JSON.parse(localStorage.getItem('bridge-transfers')||'[]');if(!ids.includes(id))ids.push(id);localStorage.setItem('bridge-transfers',JSON.stringify(ids.slice(-30)));}
 async function begin(mode){
  var bridge=root.TaskerBridge,context=bridge.context(),connection=bridge.connection();if(!bridge.remote())return;
  try{
   var selection=await send('/bridge-local/selection',{mode:mode});if(selection.cancelled)return;
   if(!bridge.isCurrent(context))throw new Error('Selected node changed. Select the source again.');
   var destination=await root.TaskerFiles.open({mode:'folder'});if(!destination)return;
   if(!bridge.isCurrent(context))throw new Error('Selected node changed. Start a new transfer.');
   if(!await root.Dialog.confirm('Upload '+selection.files+' files ('+formatBytes(selection.total)+') to '+connection.name+'\n'+destination+'?\n'+(selection.skipped.length?'Skipped links: '+selection.skipped.join(', '):'Existing files require separate confirmation.'),'Upload'))return;
   var transfer=await controller.start({connectionId:context.nodeId,selectionId:selection.id,destination:destination});remember(transfer.id);watch(transfer.id);
  }catch(e){root.Dialog.error(e.message);}
 }
 function panel(){var el=document.getElementById('brTransfers');if(!el){el=document.createElement('div');el.id='brTransfers';document.body.appendChild(el);}return el;}
 async function watch(id){
  if(active.has(id))return;var row=document.createElement('div');row.className='br-transfer';panel().appendChild(row);active.set(id,row);
  var timer=null;
  async function poll(){
   try{var job=await send('/bridge-local/transfers/'+id,undefined,'GET');row.textContent='';
    var title=document.createElement('strong');title.textContent=job.name+' · '+(job.nodeName||job.connectionId);row.appendChild(title);
    var text=document.createElement('div');text.textContent=transferSummary(job)+'\n'+formatBytes(job.transferred)+' / '+formatBytes(job.total)+'\n'+job.destination+(job.error?'\n'+job.error:'');row.appendChild(text);
    if(job.state==='completed'){var hint=document.createElement('div');hint.textContent='Running scripts are not restarted. Stop and run the task to use the updated file. Choosing a script below only changes the task path.';row.appendChild(hint);}
    function button(label,action){var b=document.createElement('button');b.className='btn sm';b.textContent=label;b.onclick=action;row.appendChild(b);}
    if(job.state==='awaiting_conflicts')button('Review overwrites',async function(){var replace=[];for(var file of job.conflicts){if(await root.Dialog.confirm('Replace existing file on '+(job.nodeName||job.connectionId)+'?\n'+file,'Overwrite'))replace.push(file);}await send('/bridge-local/transfers/'+id+'/confirm',{replace:replace});});
    if(job.state==='completed'&&root.TaskerBridge.context().nodeId===job.connectionId)button('Use another script in task…',async function(){var context=root.TaskerBridge.context();var path=await root.TaskerFiles.open({mode:'file',start:job.destination+'\\placeholder'});if(path&&root.TaskerBridge.isCurrent(context)){var input=document.getElementById('f_script_path');if(input){input.value=path;input.dispatchEvent(new Event('change',{bubbles:true}));}}});
    if(['failed','interrupted'].includes(job.state))button('Resume',async function(){await send('/bridge-local/transfers/'+id+'/resume',{});});
    if(!['completed','cancelled'].includes(job.state))button('Cancel',async function(){await send('/bridge-local/transfers/'+id,undefined,'DELETE');});
    button('Hide',function(){clearTimeout(timer);active.delete(id);row.remove();var ids=JSON.parse(localStorage.getItem('bridge-transfers')||'[]').filter(function(x){return x!==id;});localStorage.setItem('bridge-transfers',JSON.stringify(ids));});
   }catch(e){row.textContent='Transfer '+id+' · '+e.message;}
   if(active.has(id))timer=setTimeout(poll,1500);
  }poll();
 }
 document.addEventListener('DOMContentLoaded',function(){JSON.parse(localStorage.getItem('bridge-transfers')||'[]').forEach(watch);});
 root.TaskerUpload={begin:begin};
})(typeof window!=='undefined'?window:globalThis);
