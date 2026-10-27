(function(root){
    'use strict';
    function lineEndings(text,original){
        var endings=original.match(/\r\n|\r|\n/g)||[],lines=text.split(/\r\n|\r|\n/);
        return lines.map(function(line,i){return (i?(endings[i-1]||endings[0]||'\n'):'')+line;}).join('');
    }
    if(typeof module!=='undefined')module.exports={lineEndings:lineEndings};
    if(typeof document==='undefined')return;
    async function open(options){
        var bridge=root.TaskerBridge,context=bridge.context(),connection=options.connection||bridge.connection();
        if(!connection)return;
        var overlay=document.createElement('div');overlay.className='br-file-overlay br-file-editor';
        overlay.innerHTML='<div class="br-file-dialog" role="dialog" aria-modal="true" aria-labelledby="brEditorTitle"><h3 id="brEditorTitle"></h3><div class="br-editor-path"></div><div class="br-editor-info"></div><textarea aria-label="Server file text" spellcheck="false" disabled></textarea><p role="status" aria-live="polite"></p><div class="br-file-toolbar"><button class="btn" data-reopen>Reopen</button><button class="btn primary" data-save disabled>Save</button><button class="btn" data-close>Close</button></div></div>';
        document.body.appendChild(overlay);
        overlay.querySelector('h3').textContent=connection.name+' · Edit text';
        overlay.querySelector('.br-editor-path').textContent=options.path;
        var field=overlay.querySelector('textarea'),message=overlay.querySelector('[role="status"]'),save=overlay.querySelector('[data-save]'),reopen=overlay.querySelector('[data-reopen]'),closeButton=overlay.querySelector('[data-close]');
        var original='',baseline='',revision=null,busy=false,blocked=false,closed=false,previous=document.activeElement;
        function dirty(){return field.value!==baseline;}
        function state(){save.disabled=busy||blocked||!revision||!dirty();field.disabled=busy||!revision;reopen.disabled=busy;closeButton.disabled=busy;}
        async function request(method,body){
            var response=await fetch('/bridge-local/'+encodeURIComponent(connection.id)+'/api/files/text'+(method==='GET'?'?path='+encodeURIComponent(options.path):''),{method:method,headers:{'X-Bridge-Local':'1','Content-Type':'application/json'},body:body?JSON.stringify(body):undefined});
            if(response.status===404)throw new Error('File unavailable, or this server needs a Cronopus update to support text editing.');
            var data=await response.json();if(!response.ok)throw new Error(data.error||'Server file request failed');return data;
        }
        function unloading(e){if(dirty()){e.preventDefault();e.returnValue='';}}
        root.addEventListener('beforeunload',unloading);
        function nodeChanged(){blocked=true;message.textContent='Selected server changed. Your edits are kept; reopen the editor on the original server to save.';state();}
        root.addEventListener('bridge-node-changed',nodeChanged);
        async function close(){
            if(busy)return;
            if(dirty()&&!await root.Dialog.confirm('Discard unsaved changes to '+options.path+'?','Close editor'))return;
            closed=true;root.removeEventListener('beforeunload',unloading);root.removeEventListener('bridge-node-changed',nodeChanged);overlay.remove();if(previous&&previous.isConnected)previous.focus();
        }
        closeButton.onclick=close;
        field.oninput=function(){message.textContent=dirty()?'Unsaved changes':'';state();};
        async function load(){
            if(busy||!bridge.isCurrent(context))return;
            if(dirty()&&!await root.Dialog.confirm('Discard your edits and reload the current server file?','Reopen file'))return;
            busy=true;state();message.textContent='Opening server file…';
            try{
                var data=await request('GET');if(closed||!bridge.isCurrent(context))return;
                original=data.text;field.value=original;baseline=field.value;revision=data.revision;blocked=false;
                overlay.querySelector('.br-editor-info').textContent=data.encoding+(data.bom?' · BOM':'')+' · Ctrl+S to save';
                message.textContent='';
            }catch(e){message.textContent=e.message;}
            finally{busy=false;state();if(!closed&&revision)field.focus();}
        }
        reopen.onclick=load;
        save.onclick=async function(){
            if(save.disabled||!bridge.isCurrent(context))return;
            busy=true;state();message.textContent='Saving on server…';
            try{
                var text=lineEndings(field.value,original),data=await request('PUT',{path:options.path,revision:revision,text:text});
                original=text;baseline=field.value;revision=data.revision;
                message.textContent='Saved on '+connection.name;
            }catch(e){blocked=true;message.textContent=e.message+' Save was not confirmed. Your edits are kept. Reopen to check the server file before saving again.';}
            finally{busy=false;state();}
        };
        overlay.onkeydown=function(e){
            if((e.ctrlKey||e.metaKey)&&e.key.toLowerCase()==='s'){e.preventDefault();save.click();}
            if(e.key==='Escape'){e.preventDefault();close();}
            if(e.key==='Tab'){
                var fields=Array.from(overlay.querySelectorAll('textarea,button')).filter(function(el){return !el.disabled;}),first=fields[0],last=fields[fields.length-1];
                if(e.shiftKey&&document.activeElement===first){e.preventDefault();last.focus();}else if(!e.shiftKey&&document.activeElement===last){e.preventDefault();first.focus();}
            }
        };
        await load();
    }
    root.TaskerFileEditor={open:open};
})(typeof window!=='undefined'?window:globalThis);
