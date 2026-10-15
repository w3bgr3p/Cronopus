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
                +'<label>Name<input name="name" required maxlength="128" placeholder="My server" autocomplete="off"></label>'
                +'<label>Server address<input name="address" required type="url" placeholder="https://192.0.2.10:38473" autocomplete="off"></label>'
                +'<label>Access key<input name="secret" required type="password" autocomplete="new-password" spellcheck="false"></label>'
                +'<label>Node ID<input name="nodeId" required autocomplete="off" spellcheck="false"></label>'
                +'<label>Certificate SHA-256<input name="fingerprint" required autocomplete="off" spellcheck="false" placeholder="Copy fingerprint from the server Config"></label>'
                +'<p class="br-connection-error" role="status" aria-live="polite"></p>'
                +'<div class="br-connection-actions"><button type="button" class="btn" data-cancel>Cancel</button><button type="submit" class="btn primary">Add connection</button></div></form>';
            document.body.appendChild(overlay);
            var form=overlay.querySelector('form'),error=form.querySelector('[role="status"]'),submit=form.querySelector('[type="submit"]'),cancel=form.querySelector('[data-cancel]');
            function close(result){if(busy)return;form.elements.secret.value='';overlay.remove();active=null;if(previous&&previous.isConnected)previous.focus();resolve(result);}
            cancel.onclick=function(){close(null);};overlay.onclick=function(e){if(e.target===overlay)close(null);};
            overlay.onkeydown=function(e){
                if(e.key==='Escape'){e.preventDefault();close(null);}
                if(e.key==='Tab'){var fields=Array.from(form.querySelectorAll('input,button')).filter(function(el){return !el.disabled;}),first=fields[0],last=fields[fields.length-1];if(e.shiftKey&&document.activeElement===first){e.preventDefault();last.focus();}else if(!e.shiftKey&&document.activeElement===last){e.preventDefault();first.focus();}}
            };
            form.onsubmit=async function(e){
                e.preventDefault();if(busy||!form.reportValidity())return;
                var values={};['name','address','secret','nodeId','fingerprint'].forEach(function(key){values[key]=form.elements[key].value.trim();});
                if(!values.address.startsWith('https://')){error.textContent='Use an HTTPS server address.';form.elements.address.focus();return;}
                busy=true;submit.disabled=cancel.disabled=true;submit.textContent='Adding…';error.textContent='';
                try{
                    var response=await fetch('/bridge-local/connections',{method:'POST',headers:{'Content-Type':'application/json','X-Bridge-Local':'1'},body:JSON.stringify(values)});
                    var connection=await response.json();if(!response.ok)throw new Error(connection.error||'Cannot add connection');
                    busy=false;close(connection);
                }catch(ex){error.textContent=ex.message;busy=false;submit.disabled=cancel.disabled=false;submit.textContent='Add connection';}
            };
            form.elements.name.focus();
        });
        return active;
    }
    root.TaskerConnection={open:open};
})(window);
