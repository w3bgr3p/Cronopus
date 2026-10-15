(function(root){
    'use strict';
    function createTaskerTransport(deps){
        var node=null,generation=0;
        return {selectNode:function(id){node=id||null;generation++;},context:function(){return {nodeId:node,generation:generation};},isCurrent:function(c){return c.nodeId===node&&c.generation===generation;},request:function(op,opts){return deps.send(node,op,opts);}};
    }
    if(typeof module!=='undefined')module.exports={createTaskerTransport:createTaskerTransport};
    if(typeof document==='undefined')return;
    var nativeFetch=root.fetch.bind(root),transport=createTaskerTransport({send:nativeFetch}),details=new Map(),connections=[];
    function current(){return transport.context().nodeId;}
    function status(text){var el=document.getElementById('bridgeStatus');if(el)el.textContent=text;}
    function reply(data){return new Response(JSON.stringify(data),{headers:{'Content-Type':'application/json'}});}
    async function api(path,options){
        var ctx=transport.context();
        var response=await nativeFetch('/bridge-local/'+encodeURIComponent(ctx.nodeId)+'/api/'+path,Object.assign({},options,{headers:Object.assign({},options&&options.headers,{'X-Bridge-Local':'1'})}));
        if(!transport.isCurrent(ctx))throw new DOMException('Node changed','AbortError');
        ['json','text'].forEach(function(name){var read=response[name].bind(response);response[name]=async function(){var data=await read();if(!transport.isCurrent(ctx))throw new DOMException('Node changed','AbortError');return data;};});
        status(response.ok?'Connected · '+new Date().toLocaleTimeString():'Server error · '+response.status);
        return response;
    }
    async function detail(id){
        var response=await api('tasker/detail?id='+encodeURIComponent(id));
        var data=await response.json();if(!response.ok)throw new Error(data.error||'Cannot read task');
        details.set(id,data);return data;
    }
    async function remoteFetch(url,options){
        if(!current())return nativeFetch(url,options);
        if(!url.startsWith('/tasker/'))throw new Error('This operation is unavailable on a remote node');
        var path=url.slice(8),name=path.split('?')[0];
        if(name==='pick'){var params=new URLSearchParams(path.split('?')[1]);var chosen=await root.TaskerFiles.open({mode:params.get('mode'),start:params.get('start')});return reply({ok:true,path:chosen||''});}
        if(name==='list'){
            var items=[],cursor=null;
            do {var response=await api('tasks?limit=500'+(cursor?'&cursor='+encodeURIComponent(cursor):''));var page=await response.json();if(!response.ok)throw new Error(page.error||'Cannot load tasks');items=items.concat(page.items);cursor=page.next_cursor;}while(cursor);
            var selected=items.find(function(row){return row.id===root.selectedId;});
            if(selected&&!root.formDirty){var cachedSelected=details.get(selected.id);if(!cachedSelected||cachedSelected._revision!==selected._revision)await detail(selected.id);}
            items.forEach(function(row){var cached=details.get(row.id);if(cached&&cached._revision===row._revision)Object.assign(row,cached,{status:row.status,runs_total:row.runs_total,runs_success:row.runs_success});});
            return reply(items);
        }
        var opts=Object.assign({},options);
        if(opts.body){var body=JSON.parse(opts.body);if(body.id&&['save','delete','payload'].includes(name)){
            var saved=(root.taskDraft&&root.taskDraft.id===body.id?root.taskDraft:null)||details.get(body.id);
            if(!body._revision&&saved)body._revision=saved._revision;
        }opts.body=JSON.stringify(body);}
        var result=await api('tasker/'+path,opts);
        if(opts.method&&opts.method!=='GET'&&result.ok&&opts.body){var changed=JSON.parse(opts.body);if(changed.id)details.delete(changed.id);}
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
        if(typeof root.closeSse==='function')root.closeSse();
        if(typeof root.stopProcStatsPoll==='function')root.stopProcStatsPoll();
        if(typeof root.clearOutputPoll==='function')root.clearOutputPoll();
        transport.selectNode(id);details.clear();
        root.selectedId=null;root.selectedIds=new Set();root.taskDraft=null;root.formDirty=false;root.schedules=[];
        root._formBaseline={};root._detailSeq=(root._detailSeq||0)+1;
        document.body.dataset.remote=id?'true':'false';
        var header=document.getElementById('detailHeader');if(header)header.style.display='none';
        document.getElementById('detailBody').textContent='';status(id?'Connecting…':'Local');
        try{await root.loadList();}catch(e){status('Offline · '+e.message);}
    }
    document.addEventListener('DOMContentLoaded',async function(){
        var selector=document.getElementById('bridgeNode');if(!selector)return;
        selector.addEventListener('change',function(){switchNode(selector.value);});
        try{connections=await (await nativeFetch('/bridge-local/connections',{headers:{'X-Bridge-Local':'1'}})).json();
            connections.forEach(function(c){var option=document.createElement('option');option.value=c.id;option.textContent=c.name;selector.appendChild(option);});
        }catch(e){status('Connections unavailable');}
    });
    root.TaskerBridge={fetch:remoteFetch,remote:function(){return !!current();},context:transport.context,isCurrent:transport.isCurrent,
        detail:detail,api:api,connection:function(){return connections.find(function(c){return c.id===current();});},status:status};
})(typeof window!=='undefined'?window:globalThis);
