(function(root){
    'use strict';
    var active=null;
    function open(){
        if(active)return active;
        active=new Promise(function(resolve){
            var previous=document.activeElement,busy=false;
            var overlay=document.createElement('div');overlay.className='br-connection-overlay';
            overlay.innerHTML='<form class="br-connection-dialog" role="dialog" aria-modal="true" aria-labelledby="brConnectionTitle">'
                +'<h3 id="brConnectionTitle">New connection</h3><p>Add an existing server. Enable its HTTPS server in Config → Remote Tasker first.</p>'
                +'<label>Connection code<textarea name="connectionCode" rows="3" spellcheck="false" autocomplete="off" placeholder="Paste the complete connection code copied from the server"></textarea></label>'
                +'<p>Paste once to fill every field below, or enter the details manually.</p>'
                +'<label>Name<input name="name" required maxlength="128" placeholder="My server" autocomplete="off"></label>'
                +'<label>Server IP or HTTPS address<input name="address" required placeholder="192.0.2.10 or 192.0.2.10:38473" autocomplete="off"></label>'
                +'<label>Access key<input name="secret" required type="password" autocomplete="new-password" spellcheck="false"></label>'
                +'<label>Node ID<input name="nodeId" required autocomplete="off" spellcheck="false"></label>'
                +'<label>Certificate SHA-256<input name="fingerprint" required autocomplete="off" spellcheck="false" placeholder="Copy fingerprint from the server Config"></label>'
                +'<p class="br-connection-error" role="status" aria-live="polite"></p>'
                +'<div class="br-connection-actions"><button type="button" class="btn" data-cancel>Cancel</button><button type="submit" class="btn primary">Add connection</button></div></form>';
            document.body.appendChild(overlay);
            var form=overlay.querySelector('form'),error=form.querySelector('[role="status"]'),submit=form.querySelector('[type="submit"]'),cancel=form.querySelector('[data-cancel]');
            function close(result){if(busy)return;form.elements.secret.value='';form.elements.connectionCode.value='';overlay.remove();active=null;if(previous&&previous.isConnected)previous.focus();resolve(result);}
            var codeValid=true;
            form.elements.connectionCode.oninput=function(){
                error.dataset.validCode='false';
                if(!this.value.trim()){codeValid=true;error.textContent='';return;}
                try{var values=root.BridgeConnectionCode.parse(this.value);Object.keys(values).forEach(function(key){form.elements[key].value=values[key];});codeValid=true;error.dataset.validCode='true';error.textContent='Connection details filled. Click Add connection.';}
                catch(ex){codeValid=false;error.textContent=ex.message;}
            };
            cancel.onclick=function(){close(null);};overlay.onclick=function(e){if(e.target===overlay)close(null);};
            overlay.onkeydown=function(e){
                if(e.key==='Escape'){e.preventDefault();close(null);}
                if(e.key==='Tab'){var fields=Array.from(form.querySelectorAll('input,textarea,button')).filter(function(el){return !el.disabled;}),first=fields[0],last=fields[fields.length-1];if(e.shiftKey&&document.activeElement===first){e.preventDefault();last.focus();}else if(!e.shiftKey&&document.activeElement===last){e.preventDefault();first.focus();}}
            };
            form.onsubmit=async function(e){
                e.preventDefault();if(busy||!codeValid||!form.reportValidity())return;
                error.dataset.validCode='false';
                var values={};['name','address','secret','nodeId','fingerprint'].forEach(function(key){values[key]=form.elements[key].value.trim();});
                try{values.address=root.BridgeConnectionCode.normalizeAddress(values.address);form.elements.address.value=values.address;}catch(ex){error.textContent=ex.message;form.elements.address.focus();return;}
                busy=true;submit.disabled=cancel.disabled=true;submit.textContent='Adding…';error.textContent='';
                try{
                    var response=await fetch('/bridge-local/connections',{method:'POST',headers:{'Content-Type':'application/json','X-Bridge-Local':'1'},body:JSON.stringify(values)});
                    var connection=await response.json();if(!response.ok)throw new Error(connection.error||'Cannot add connection');
                    busy=false;close(connection);
                }catch(ex){error.textContent=ex.message;busy=false;submit.disabled=cancel.disabled=false;submit.textContent='Add connection';}
            };
            form.elements.connectionCode.focus();
        });
        return active;
    }
    root.TaskerConnection={open:open};
})(window);
