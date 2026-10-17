(function(root){
 'use strict';
 function createUploadController(deps){return{
  start:function(o){return deps.send('/bridge-local/'+encodeURIComponent(o.connectionId)+'/transfers',{selectionId:o.selectionId,destination:o.destination});},
  startTask:function(o){return deps.send('/bridge-local/'+encodeURIComponent(o.connectionId)+'/task-upload',{task:o.task,destination:o.destination});}
 };}
 function formatBytes(value){var n=Math.max(0,Number(value)||0),units=['B','KiB','MiB','GiB'],u=0;while(n>=1024&&u<units.length-1){n/=1024;u++;}return Number(n.toFixed(u?1:0))+' '+units[u];}
 function transferSummary(job){
  if(job.state!=='completed')return ({hashing:'Checking files',uploading:'Uploading',committing:'Saving on server',creating_task:'Creating task on server',awaiting_conflicts:'Confirm replacement',failed:'Upload failed',interrupted:'Upload interrupted',cancelled:'Cancelled'})[job.state]||job.state;
  if(job.taskUpload)return 'Task created on server · '+job.remoteTaskId;
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
    if(job.state==='completed'){var hint=document.createElement('div');hint.textContent=job.taskUpload?'The task has the copied settings and payload. Select its server to view it.':'Running scripts are not restarted. Stop and run the task to use the updated file. Choosing a script below only changes the task path.';row.appendChild(hint);}
    function button(label,action){var b=document.createElement('button');b.className='btn sm';b.textContent=label;b.onclick=action;row.appendChild(b);}
    if(job.state==='awaiting_conflicts')button('Review overwrites',async function(){var replace=[];for(var file of job.conflicts){if(await root.Dialog.confirm('Replace existing file on '+(job.nodeName||job.connectionId)+'?\n'+file,'Overwrite'))replace.push(file);}await send('/bridge-local/transfers/'+id+'/confirm',{replace:replace});});
    if(job.state==='completed'&&!job.taskUpload&&root.TaskerBridge.context().nodeId===job.connectionId)button('Use another script in task…',async function(){var context=root.TaskerBridge.context();var path=await root.TaskerFiles.open({mode:'file',start:job.destination+'\\placeholder'});if(path&&root.TaskerBridge.isCurrent(context)){var input=document.getElementById('f_script_path');if(input){input.value=path;input.dispatchEvent(new Event('change',{bubbles:true}));}}});
    if(['failed','interrupted'].includes(job.state))button('Resume',async function(){await send('/bridge-local/transfers/'+id+'/resume',{});});
    if(!['completed','cancelled'].includes(job.state))button('Cancel',async function(){await send('/bridge-local/transfers/'+id,undefined,'DELETE');});
    button('Hide',function(){clearTimeout(timer);active.delete(id);row.remove();var ids=JSON.parse(localStorage.getItem('bridge-transfers')||'[]').filter(function(x){return x!==id;});localStorage.setItem('bridge-transfers',JSON.stringify(ids));});
   }catch(e){row.textContent='Transfer '+id+' · '+e.message;}
   if(active.has(id))timer=setTimeout(poll,1500);
  }poll();
 }
 document.addEventListener('DOMContentLoaded',function(){JSON.parse(localStorage.getItem('bridge-transfers')||'[]').forEach(watch);});
 var taskDialog=null;
 async function task(task){
  if(taskDialog)return;
  var bridge=root.TaskerBridge,context=bridge.context();if(bridge.remote())return;
  var overlay=document.createElement('div');overlay.className='br-connection-overlay br-task-upload';taskDialog=overlay;
  overlay.innerHTML='<form class="br-connection-dialog" role="dialog" aria-modal="true" aria-labelledby="taskUploadTitle"><h3 id="taskUploadTitle">Upload task</h3><p data-source></p><label>Server<select required aria-label="Upload server"></select></label><label>Destination folder<input required readonly aria-label="Upload destination"></label><button type="button" class="btn" data-browse>Choose folder…</button><p>The file and current settings, schedule and payload will be copied. An active schedule remains active on the server.</p><p class="br-connection-error" role="status" aria-live="polite"></p><div class="br-connection-actions"><button type="button" class="btn" data-cancel>Cancel</button><button type="submit" class="btn primary" disabled>Upload</button></div></form>';
  document.body.appendChild(overlay);
  var form=overlay.querySelector('form'),select=form.querySelector('select'),folder=form.querySelector('input'),error=form.querySelector('[role="status"]'),browse=form.querySelector('[data-browse]'),submit=form.querySelector('[type="submit"]'),busy=false,connections=[],previous=document.activeElement;
  form.querySelector('[data-source]').textContent=task.name+' · '+task.script_path;
  function close(){if(busy)return;root.removeEventListener('bridge-node-changed',close);overlay.remove();taskDialog=null;if(previous&&previous.isConnected)previous.focus();}
  root.addEventListener('bridge-node-changed',close);
  form.querySelector('[data-cancel]').onclick=close;
  overlay.onclick=function(e){if(e.target===overlay)close();};
  overlay.onkeydown=function(e){if(e.key==='Escape'){e.preventDefault();close();}if(e.key==='Tab'){var fields=Array.from(form.querySelectorAll('select,input,button')).filter(function(el){return !el.disabled;}),first=fields[0],last=fields[fields.length-1];if(e.shiftKey&&document.activeElement===first){e.preventDefault();last.focus();}else if(!e.shiftKey&&document.activeElement===last){e.preventDefault();first.focus();}}};
  select.onchange=function(){folder.value='';submit.disabled=true;error.textContent='';};
  browse.onclick=async function(){
   var connection=connections.find(function(c){return c.id===select.value;});if(!connection)return;
   // Keep the local task selected while browsing an explicitly chosen server.
   overlay.style.display='none';
   try{var destination=await root.TaskerFiles.open({mode:'folder',connection:connection});if(taskDialog!==overlay||!bridge.isCurrent(context))return;if(select.value===connection.id&&destination){folder.value=destination;submit.disabled=false;}}
   catch(e){error.textContent=e.message;}finally{overlay.style.display='';}
  };
  form.onsubmit=async function(e){
   e.preventDefault();if(busy||!folder.value||!select.value||!bridge.isCurrent(context))return;
   busy=true;Array.from(form.elements).forEach(function(el){el.disabled=true;});submit.textContent='Starting…';error.textContent='';
   try{var transfer=await controller.startTask({connectionId:select.value,destination:folder.value,task:task});remember(transfer.id);watch(transfer.id);busy=false;close();}
   catch(ex){error.textContent=ex.message;busy=false;Array.from(form.elements).forEach(function(el){el.disabled=false;});submit.textContent='Upload';if(!bridge.isCurrent(context))close();}
  };
  try{
   connections=await send('/bridge-local/connections',undefined,'GET');if(taskDialog!==overlay)return;
   connections.forEach(function(c){var option=document.createElement('option');option.value=c.id;option.textContent=c.name;select.appendChild(option);});
   browse.disabled=!connections.length;if(!connections.length)error.textContent='Add a server with + New connection in Tasker first.';select.focus();
  }catch(ex){error.textContent=ex.message;browse.disabled=true;}
 }
 root.TaskerUpload={begin:begin,task:task};
})(typeof window!=='undefined'?window:globalThis);
