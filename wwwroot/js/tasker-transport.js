(function(root){
    'use strict';
    function createTaskerTransport(deps){
        var node=null,generation=0;
        return {selectNode:function(id){node=id||null;generation++;},context:function(){return {nodeId:node,generation:generation};},isCurrent:function(c){return c.nodeId===node&&c.generation===generation;},request:function(op,opts){return deps.send(node,op,opts);}};
    }
    if(typeof module!=='undefined')module.exports={createTaskerTransport:createTaskerTransport};
    if(typeof document==='undefined')return;
    var nativeFetch=root.fetch.bind(root),transport=createTaskerTransport({send:nativeFetch}),details=new Map(),connections=[],listCache=null,changeCursor=null,listPromise=null,runtimeCursors=new Map();
    function current(){return transport.context().nodeId;}
    function status(text){var el=document.getElementById('bridgeStatus');if(el)el.textContent=text;}
    function reply(data){return new Response(JSON.stringify(data),{headers:{'Content-Type':'application/json'}});}
    function pending(){try{return JSON.parse(localStorage.getItem('bridge-commands')||'[]');}catch(e){return [];}}
    function remember(command){var all=pending().filter(function(c){return c.id!==command.id;});all.push(command);localStorage.setItem('bridge-commands',JSON.stringify(all.slice(-100)));}
    function forget(id){localStorage.setItem('bridge-commands',JSON.stringify(pending().filter(function(c){return c.id!==id;})));}
    async function recover(command){
        try{var response=await nativeFetch('/bridge-local/'+encodeURIComponent(command.nodeId)+'/api/operations/'+encodeURIComponent(command.id),{headers:{'X-Bridge-Local':'1'}});
            if(!response.ok)return null;var op=await response.json();
            if(op.state==='succeeded'||op.state==='failed'){forget(command.id);return new Response(op.result,{status:op.status,headers:{'Content-Type':'application/json'}});}
        }catch(e){}return null;
    }
    async function waitOperation(command,context){
        for(var n=0;n<1800;n++){
            await new Promise(function(resolve){setTimeout(resolve,1000);});
            if(!transport.isCurrent(context))throw new DOMException('Node changed','AbortError');
            var response=await nativeFetch('/bridge-local/'+encodeURIComponent(command.nodeId)+'/api/operations/'+encodeURIComponent(command.id),{headers:{'X-Bridge-Local':'1'}});
            if(!response.ok)throw new Error('Connection lost during operation. Use Check commands after reconnecting.');
            var op=await response.json();if(op.state==='succeeded'||op.state==='failed'){forget(command.id);return new Response(op.result,{status:op.status,headers:{'Content-Type':'application/json'}});}
            if(op.result)root.dispatchEvent(new CustomEvent('bridge-operation-progress',{detail:{path:command.path,text:op.result}}));
            if(op.state==='outcome_unknown')break;
        }
        return new Response(JSON.stringify({ok:false,error:'Operation outcome is unknown. Use Check commands after reconnecting.'}),{status:202});
    }
    async function api(path,options){
        var ctx=transport.context();
        var mutation=options&&options.method&&options.method!=='GET';
        var command=mutation?{id:crypto.randomUUID(),nodeId:ctx.nodeId,path:path.split('?')[0],at:Date.now()}:null;
        if(command)remember(command);
        var response;
        try{
            response=await nativeFetch('/bridge-local/'+encodeURIComponent(ctx.nodeId)+'/api/'+path,Object.assign({},options,{headers:Object.assign({},options&&options.headers,{'X-Bridge-Local':'1'},command?{'X-Request-ID':command.id}:{})}));
            if(command){var text=await response.text();response=new Response(text,{status:response.status,headers:response.headers});if(response.status===202&&JSON.parse(text).operation_id)response=await waitOperation(command,ctx);else if(response.status<500&&response.status!==202)forget(command.id);else if(response.status>=500){var confirmed=await recover(command);if(confirmed)response=confirmed;}}
        }catch(e){
            if(!command)throw e;
            response=await recover(command);
            if(!response)response=new Response(JSON.stringify({ok:false,code:'outcome_unknown',request_id:command.id,error:'Command result is unknown. Reconnect to check its status; do not repeat it.'}),{status:202,headers:{'Content-Type':'application/json'}});
        }
        if(!transport.isCurrent(ctx))throw new DOMException('Node changed','AbortError');
        ['json','text'].forEach(function(name){var read=response[name].bind(response);response[name]=async function(){var data=await read();if(!transport.isCurrent(ctx))throw new DOMException('Node changed','AbortError');
            var parsed=name==='json'?data:(function(){try{return JSON.parse(data);}catch(e){return null;}})();
            if(command&&parsed&&parsed._revision&&options.body){var changed=JSON.parse(options.body);if(root.taskDraft&&root.taskDraft.id===changed.id)root.taskDraft._revision=parsed._revision;var row=(root.schedules||[]).find(function(r){return r.id===changed.id;});if(row)row._revision=parsed._revision;}
            return data;};});
        status(response.status===202?'Command outcome pending':response.ok?'Connected · '+new Date().toLocaleTimeString():'Server error · '+response.status);
        return response;
    }
    async function detail(id){
        var response=await api('tasker/detail?id='+encodeURIComponent(id));
        var data=await response.json();if(!response.ok)throw new Error(data.error||'Cannot read task');
        details.set(id,data);return data;
    }
    async function readList(){
        if(listCache&&changeCursor){
            var more=false;
            do{var response=await api('changes?cursor='+encodeURIComponent(changeCursor));var page=await response.json();if(!response.ok)throw new Error(page.error||'Cannot update tasks');
                if(page.reset_required){listCache=null;changeCursor=null;details.clear();break;}
                page.items.forEach(function(change){if(change.kind==='delete')listCache.delete(change.id);else listCache.set(change.task.id,change.task);});
                changeCursor=page.next_cursor;more=page.has_more;
            }while(more);
        }
        if(!listCache){
            var collected=new Map(),cursor=null,baseline=null;
            do{var response=await api('tasks?limit=500'+(cursor?'&cursor='+encodeURIComponent(cursor):''));var page=await response.json();if(!response.ok)throw new Error(page.error||'Cannot load tasks');
                page.items.forEach(function(row){collected.set(row.id,row);});cursor=page.next_cursor;if(!baseline)baseline=page.change_cursor;
            }while(cursor);
            listCache=collected;changeCursor=baseline;
        }
        return Array.from(listCache.values()).map(function(row){return Object.assign({},row);});
    }
    async function remoteFetch(url,options){
        var context=transport.context();
        if(!current()){
            var local=await nativeFetch(url,options);
            if(!transport.isCurrent(context))throw new DOMException('Node changed','AbortError');
            ['json','text'].forEach(function(name){var read=local[name].bind(local);local[name]=async function(){var value=await read();if(!transport.isCurrent(context))throw new DOMException('Node changed','AbortError');return value;};});
            return local;
        }
        if(!url.startsWith('/tasker/'))throw new Error('This operation is unavailable on a remote node');
        var path=url.slice(8),name=path.split('?')[0];
        if(name==='instances'||name==='queue'){
            var id=new URLSearchParams(path.split('?')[1]).get('id'),key=name+':'+id,cursor=runtimeCursors.get(key)||'';
            var response=await api('tasks/'+encodeURIComponent(id)+'/'+name+'?limit=100'+(cursor?'&cursor='+encodeURIComponent(cursor):''));
            var page=await response.json();if(!response.ok)return reply([]);
            setTimeout(function(){
                if(!transport.isCurrent(context)||root.selectedId!==id)return;
                var card=document.getElementById(name==='queue'?'queueCard':'instancesCard');if(!card)return;
                var old=card.querySelector('.br-runtime-pages');if(old)old.remove();
                if(!cursor&&!page.next_cursor)return;
                var controls=document.createElement('div');controls.className='br-runtime-pages';
                function button(label,next){var b=document.createElement('button');b.className='btn sm';b.textContent=label;b.onclick=function(){if(!transport.isCurrent(context))return;runtimeCursors.set(key,next);b.disabled=true;b.textContent='Loading…';};controls.appendChild(b);}
                if(cursor)button('First page','');if(page.next_cursor)button('Next 100',page.next_cursor);card.appendChild(controls);
            },0);
            return reply(page.items);
        }
        if(name==='pick'){var params=new URLSearchParams(path.split('?')[1]);var chosen=await root.TaskerFiles.open({mode:params.get('mode'),start:params.get('start')});if(!transport.isCurrent(context))throw new DOMException('Node changed','AbortError');return reply({ok:true,path:chosen||''});}
        if(name==='list'){
            if(!listPromise){var listContext=transport.context();listPromise=readList().finally(function(){if(transport.isCurrent(listContext))listPromise=null;});}
            var items=await listPromise;
            var selected=items.find(function(row){return row.id===root.selectedId;});
            if(selected&&!root.formDirty){var cachedSelected=details.get(selected.id);if(!cachedSelected||cachedSelected._revision!==selected._revision)await detail(selected.id);}
            items.forEach(function(row){var cached=details.get(row.id);if(cached&&cached._revision===row._revision)Object.assign(row,cached,{status:row.status,runs_total:row.runs_total,runs_success:row.runs_success});});
            return reply(items);
        }
        var opts=Object.assign({},options);
        if(opts.body){var body=JSON.parse(opts.body);if(name==='save'&&!body.id){delete body._revision;delete body.edit_version;}if(body.id&&['save','delete','payload'].includes(name)){
            var saved=(root.taskDraft&&root.taskDraft.id===body.id?root.taskDraft:null)||details.get(body.id)||(root.schedules||[]).find(function(row){return row.id===body.id;});
            if(!body._revision&&saved)body._revision=saved._revision;
        }opts.body=JSON.stringify(body);}
        var result=await api('tasker/'+path,opts);
        if(opts.method&&opts.method!=='GET'&&result.ok&&opts.body){var changed=JSON.parse(opts.body);if(changed.id)details.delete(changed.id);listCache=null;changeCursor=null;}
        return result;
    }
    async function switchNode(id){
        var selector=document.getElementById('bridgeNode');
        if(root.formDirty){
            root.Dialog._buttons([{label:'Save',cls:'primary',val:'save'},{label:'Discard',val:'discard'},{label:'Stay',val:'stay'}]);
            var choice=await root.Dialog._open('⇄','Unsaved changes','Save the task before switching nodes?','alert');
            if(choice==='save'){await root.saveSchedule(root.selectedId);if(root.formDirty){selector.value=current()||'';return;}}
            else if(choice!=='discard'){selector.value=current()||'';return;}
        }
        if(typeof root.closeSseOutput==='function')root.closeSseOutput();
        if(root.RemoteOutput)root.RemoteOutput.stop();
        if(typeof root.closeSse==='function')root.closeSse();
        if(typeof root.stopProcStatsPoll==='function')root.stopProcStatsPoll();
        if(typeof root.clearOutputPoll==='function')root.clearOutputPoll();
        transport.selectNode(id);details.clear();listCache=null;changeCursor=null;listPromise=null;
        if(root.closePayloadModal)root.closePayloadModal();
        runtimeCursors.clear();
        root.dispatchEvent(new CustomEvent('bridge-node-changed'));
        root.selectedId=null;root.selectedIds=new Set();root.taskDraft=null;root.formDirty=false;root.schedules=[];
        root._formBaseline={};root._detailSeq=(root._detailSeq||0)+1;
        document.body.dataset.remote=id?'true':'false';
        var header=document.getElementById('detailHeader');if(header)header.style.display='none';
        document.getElementById('detailBody').textContent='';status(id?'Connecting…':'Local');
        try{await root.loadList();}catch(e){status('Offline · '+e.message);}
    }
    async function recoverPending(){
        var commands=pending().filter(function(c){return c.nodeId===current();});
        if(!commands.length){root.Dialog.alert('No commands awaiting confirmation');return;}
        var messages=[];
        for(var command of commands){var response=await recover(command);messages.push(command.path+' · '+(response?(response.ok?'completed':'failed'):'outcome unknown; do not repeat automatically'));}
        root.Dialog.alert(messages.join('\n'),'Command outcomes');
    }
    document.addEventListener('DOMContentLoaded',async function(){
        var selector=document.getElementById('bridgeNode');if(!selector)return;
        selector.addEventListener('change',function(){switchNode(selector.value);});
        try{connections=await (await nativeFetch('/bridge-local/connections',{headers:{'X-Bridge-Local':'1'}})).json();
            connections.forEach(function(c){var option=document.createElement('option');option.value=c.id;option.textContent=c.name;selector.appendChild(option);});
        }catch(e){status('Connections unavailable');}
    });
    root.TaskerBridge={fetch:remoteFetch,remote:function(){return !!current();},context:transport.context,isCurrent:transport.isCurrent,
        detail:detail,api:api,connection:function(){return connections.find(function(c){return c.id===current();});},status:status,recoverPending:recoverPending,
        lastSelected:function(id){var key='bridge-selected:'+current();if(id!==undefined)localStorage.setItem(key,id);return localStorage.getItem(key);}};
})(typeof window!=='undefined'?window:globalThis);
