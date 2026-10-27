(function(root){
 'use strict';function tailLines(lines){return lines.slice(-2000);}
 if(typeof module!=='undefined')module.exports={tailLines:tailLines};
 if(typeof document==='undefined')return;
 var task=null,cursor=null,previous=null,controller=null,lines=[];
 function setTask(id){if(task!==id){stop();task=id;cursor=null;previous=null;lines=[];}}
 function render(page,append){
  if(page.gap)lines.push({line:'[output gap: older data was discarded]',level:'WARN'});
  lines=tailLines(append?lines.concat(page.items):page.items);
  var box=root._getOutputBox();if(!box)return;box.innerHTML='';
  lines.forEach(function(line){root._appendLine(box,line);});
  cursor=page.next_cursor;previous=page.previous_cursor||previous;
  if(root._outputAutoScroll!==false)box.scrollTop=box.scrollHeight;
 }
 async function load(id,older){
  setTask(id);var context=root.TaskerBridge.context();
  try{var response=await root.TaskerBridge.api('tasks/'+encodeURIComponent(id)+'/output'+(older&&previous?'?direction=before&cursor='+encodeURIComponent(previous):''));
   var page=await response.json();if(!response.ok)throw new Error(page.error||'Cannot read output');
   if(root.TaskerBridge.isCurrent(context)&&root.selectedId===id)render(page,false);
  }catch(e){if(e.name!=='AbortError')root.TaskerBridge.status(e.message);}
 }
 function stop(){if(controller)controller.abort();controller=null;var b=document.getElementById('brObserve');if(b)b.textContent='Observe';}
 async function toggle(){
  if(controller){stop();return;}if(!root.selectedId||root.selectedId==='__new__')return;
  setTask(root.selectedId);var id=task,context=root.TaskerBridge.context();controller=new AbortController();
  var signal=controller.signal;document.getElementById('brObserve').textContent='Stop observing';
  try{var response=await root.TaskerBridge.api('tasks/'+encodeURIComponent(id)+'/output/stream'+(cursor?'?cursor='+encodeURIComponent(cursor):''),{signal:signal});
   if(!response.ok)throw new Error('Cannot start observation ('+response.status+')');
   var reader=response.body.getReader(),decoder=new TextDecoder(),buffer='';
   while(!signal.aborted){var chunk=await reader.read();if(chunk.done)break;buffer+=decoder.decode(chunk.value,{stream:true});var end;
    if(buffer.length>262144)throw new Error('Output frame exceeds limit');
    while((end=buffer.indexOf('\n\n'))>=0){var event=buffer.slice(0,end);buffer=buffer.slice(end+2);var data=event.split('\n').find(function(s){return s.startsWith('data: ');});
     if(data&&root.TaskerBridge.isCurrent(context)&&root.selectedId===id)render(JSON.parse(data.slice(6)),true);
    }
   }
  }catch(e){if(e.name!=='AbortError')root.TaskerBridge.status(e.message);}finally{if(!signal.aborted)stop();}
 }
 document.addEventListener('visibilitychange',function(){if(document.hidden)stop();});
 async function install(id,button){
  var path='tasks/'+encodeURIComponent(id)+'/actions/install',context=root.TaskerBridge.context();
  function progress(event){if(event.detail.path!==path||!root.TaskerBridge.isCurrent(context)||root.selectedId!==id)return;
   var items=event.detail.text.split('\n').filter(function(s){return s.startsWith('data: ');}).map(function(s){try{return JSON.parse(s.slice(6));}catch(e){return null;}}).filter(function(x){return x&&x.line;});
   render({items:items,gap:false,next_cursor:cursor},false);
  }
  var label=button.getAttribute('aria-label');
  button.disabled=true;root.ActionIcons.set(button,'loading','Installing dependencies…');root.addEventListener('bridge-operation-progress',progress);
  try{var response=await root.TaskerBridge.api(path,{method:'POST',headers:{'Content-Type':'application/json'},body:'{}'});var text=await response.text();
   if(!response.ok)throw new Error(text);progress({detail:{path:path,text:text}});
  }catch(e){root.TaskerBridge.status(e.message);}finally{root.removeEventListener('bridge-operation-progress',progress);button.disabled=false;root.ActionIcons.set(button,'install',label);}
 }
 root.RemoteOutput={load:load,toggle:toggle,stop:stop,setTask:setTask,install:install};
})(typeof window!=='undefined'?window:globalThis);
