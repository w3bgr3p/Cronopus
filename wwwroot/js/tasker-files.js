(function(root){
    'use strict';
    function initialFolder(path,last){if(!path)return last||'';var at=Math.max(path.lastIndexOf('\\'),path.lastIndexOf('/'));return at>=0?path.slice(0,at)||'/':last||'';}
    if(typeof module!=='undefined')module.exports={initialFolder:initialFolder};
    if(typeof document==='undefined')return;
    async function open(options){
        var bridge=root.TaskerBridge,context=bridge.context(),connection=options.connection||bridge.connection();
        async function api(path,opts){
            if(!options.connection)return bridge.api(path,opts);
            return fetch('/bridge-local/'+encodeURIComponent(connection.id)+'/api/'+path,Object.assign({},opts,{headers:Object.assign({},opts&&opts.headers,{'X-Bridge-Local':'1'})}));
        }
        var folderKey=connection?connection.id:context.nodeId;
        return new Promise(function(resolve){
            var overlay=document.createElement('div');overlay.className='br-file-overlay';
            overlay.innerHTML='<div class="br-file-dialog"><h3></h3><div class="br-file-toolbar"><button class="btn" data-root>Disks</button><button class="btn" data-up>Up</button><input aria-label="Server folder"><button class="btn" data-go>Go</button><button class="btn" data-new>New folder</button></div><div class="br-file-list"></div><p role="status"></p><div class="br-file-toolbar"><button class="btn" data-more hidden>More</button><button class="btn primary" data-select>Select folder</button><button class="btn" data-cancel>Cancel</button></div></div>';
            document.body.appendChild(overlay);overlay.querySelector('h3').textContent=(connection?connection.name:'Server')+' · '+(options.mode==='folder'?'Choose folder':'Choose script');
            var list=overlay.querySelector('.br-file-list'),input=overlay.querySelector('input'),message=overlay.querySelector('p'),current='',parent=null,cursor=null,seq=0;
            var select=overlay.querySelector('[data-select]');select.hidden=options.mode!=='folder';
            function changed(){close(null);}
            function close(path){root.removeEventListener('bridge-node-changed',changed);overlay.remove();resolve(path);}
            root.addEventListener('bridge-node-changed',changed);
            async function load(path,more){
                var request=++seq;message.textContent='Loading…';select.disabled=true;
                try{
                    var response=await api(path?'files/list?path='+encodeURIComponent(path)+(more&&cursor?'&cursor='+encodeURIComponent(cursor):''):'files/roots');
                    var data=await response.json();if(!response.ok)throw new Error(data.error||'Cannot read folder');
                    if(request!==seq||!bridge.isCurrent(context))return;
                    if(!more)list.textContent='';current=path||'';input.value=current;parent=data.parent;cursor=data.next_cursor;overlay.querySelector('[data-more]').hidden=!cursor;
                    (Array.isArray(data)?data:data.items).forEach(function(item){var button=document.createElement('button');button.className='br-file-entry';button.textContent=(item.kind==='directory'?'▸ ':'  ')+(item.name||item.path)+(item.link?' ↗':'');
                        button.onclick=function(){if(item.kind==='directory')load(item.path,false);else if(options.mode!=='folder'){localStorage.setItem('bridge-folder:'+folderKey,current);close(item.path);}};list.appendChild(button);});
                    message.textContent='';select.disabled=!current;
                }catch(e){message.textContent=e.message;}
            }
            overlay.querySelector('[data-root]').onclick=function(){load('',false);};overlay.querySelector('[data-up]').onclick=function(){load(parent||'',false);};
            overlay.querySelector('[data-go]').onclick=function(){load(input.value,false);};input.onkeydown=function(e){if(e.key==='Enter')load(input.value,false);};
            overlay.querySelector('[data-more]').onclick=function(){load(current,true);};
            overlay.querySelector('[data-cancel]').onclick=function(){close(null);};
            select.onclick=function(){if(current){localStorage.setItem('bridge-folder:'+folderKey,current);close(current);}};
            overlay.querySelector('[data-new]').onclick=async function(){if(!current)return;var name=await root.Dialog.prompt('Folder name','Create on '+connection.name);if(!name||!bridge.isCurrent(context))return;try{var res=await api('files/directories',{method:'POST',headers:{'Content-Type':'application/json','X-Request-ID':crypto.randomUUID()},body:JSON.stringify({parent:current,name:name})});var data=await res.json();if(!res.ok)throw new Error(data.error);load(data.path,false);}catch(e){message.textContent=e.message;}};
            load(options.start?initialFolder(options.start,''):localStorage.getItem('bridge-folder:'+folderKey)||'',false);
        });
    }
    root.TaskerFiles={open:open};
})(typeof window!=='undefined'?window:globalThis);
