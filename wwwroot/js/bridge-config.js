(function(root) {
    'use strict';
    function createConfigController(deps) {
        return { save: function(value) {
            var body = Object.assign({}, value); delete body.key;
            if (value.key) body.secret = value.key;
            var id = body.id; delete body.id;
            return deps.send(id ? 'PUT' : 'POST', '/bridge-local/connections' + (id ? '/' + encodeURIComponent(id) : ''), body);
        }};
    }
    async function send(method, path, body) {
        var response = await fetch(path, {method:method, headers:{'Content-Type':'application/json','X-Bridge-Local':'1'}, body:body === undefined ? undefined : JSON.stringify(body)});
        if(response.status===404||response.status===405)throw new Error('This operation is missing in the running z3nDash backend. Restart the updated application; refreshing the page alone is not enough.');
        var data;try{data=await response.json();}catch(e){throw new Error('Invalid response from z3nDash (HTTP '+response.status+'). Check that the updated application is running.');}
        if (!response.ok) throw new Error(data.error || 'Request failed (' + response.status + ')'); return data;
    }
    function esc(value) { return String(value == null ? '' : value).replace(/[&<>"']/g, function(c) {return {'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c];}); }
    function input(id) { return document.getElementById(id).value.trim(); }
    function message(text,scope) { var el=document.getElementById(scope==='connection'?'brMessage':'brServerMessage')||document.getElementById('brMessage');if(el)el.textContent = text; }
    function keyMessage(text){var el=document.getElementById('brKeyStatus');if(el)el.textContent=text;else message(text);}
    function firewallStatus(value){
        if(!value){document.getElementById('brFirewallStatus').textContent='The running backend does not support Firewall controls. Restart the updated z3nDash application.';
            ['brOpenFirewall','brCloseFirewall','brCheckFirewall','brFirewallOnApply'].forEach(function(id){document.getElementById(id).disabled=true;});document.getElementById('brFirewallOnApply').checked=false;return;}
        document.getElementById('brCheckFirewall').disabled=false;
        document.getElementById('brFirewallStatus').textContent=value.error||('TCP '+value.port+' · '+(value.open?'z3nDash inbound rule is open':value.exists?'z3nDash rule needs updating for the current port/application':'No z3nDash inbound rule'));
        ['brOpenFirewall','brCloseFirewall','brFirewallOnApply'].forEach(function(id){document.getElementById(id).disabled=!value.supported;});
        if(!value.supported)document.getElementById('brFirewallOnApply').checked=false;
        document.getElementById('brCloseFirewall').disabled=!value.supported||!value.exists;
    }
    async function changeFirewall(action){
        var buttons=['brOpenFirewall','brCloseFirewall','brCheckFirewall'];buttons.forEach(function(id){document.getElementById(id).disabled=true;});message('');
        document.getElementById('brFirewallStatus').textContent='Updating Windows Firewall… Approve the Windows administrator prompt if shown.';
        try{firewallStatus(await send('POST','/bridge-local/server/firewall',{action:action}));}
        catch(e){try{firewallStatus(await send('GET','/bridge-local/server/firewall'));}catch(ignored){}
            document.getElementById('brFirewallStatus').textContent='Firewall '+action+' failed: '+e.message;message(e.message);}
        finally{document.getElementById('brCheckFirewall').disabled=false;}
    }
    async function checkFirewall(button){
        var label=button.textContent;button.disabled=true;button.textContent='Checking…';message('');document.getElementById('brFirewallStatus').textContent='Checking Windows Firewall…';
        try{firewallStatus(await send('GET','/bridge-local/server/firewall'));var status=document.getElementById('brFirewallStatus');status.textContent='Checked '+new Date().toLocaleTimeString()+' · '+status.textContent;}
        catch(e){document.getElementById('brFirewallStatus').textContent=e.message;throw e;}
        finally{button.disabled=false;button.textContent=label;}
    }
    var connections = [], controller = createConfigController({send:send}),ipRequest=0,ipEdits=0,configLoad=0,autoIpTried=false,exportKey=null;
    async function copyConnection(){
        if(!exportKey)throw new Error('Create a new access key first. Existing keys cannot be recovered.');
        var s=await send('GET','/bridge-local/server');
        if(!s.actualUrl||!s.settings.enabled)throw new Error('Enable and apply the HTTPS server settings, then copy the connection.');
        if(!s.keys.some(function(k){return k.id===exportKey.id&&!k.revoked;}))throw new Error('This key is no longer active. Create a new key.');
        var code=root.BridgeConnectionCode.encode({name:new URL(s.actualUrl).hostname,address:s.actualUrl,nodeId:s.nodeId,fingerprint:s.fingerprint,secret:exportKey.secret});
        var field=document.getElementById('brConnectionCode');field.value=code;
        try{await navigator.clipboard.writeText(code);keyMessage('Connection copied. Paste it into New connection on your other z3nDash.');}
        catch(e){field.hidden=false;field.focus();field.select();var copied=false;try{copied=document.execCommand('copy');}catch(ignored){}
            keyMessage(copied?'Connection copied. Paste it into New connection on your other z3nDash.':'Select this complete connection code and press Ctrl+C.');}
    }
    async function detectIp(){
        var request=++ipRequest,edits=ipEdits,version=configLoad,button=document.getElementById('brDetectIp'),field=document.getElementById('br_advertisedAddress'),info=document.getElementById('brIpStatus');
        button.disabled=true;info.textContent='Detecting this server’s public IP…';
        try{
            var result=await send('GET','/bridge-local/server/public-ip');
            if(request!==ipRequest||edits!==ipEdits||version!==configLoad)return;
            field.value=result.address;info.textContent='Public IP detected. Apply server settings to use it. If your RDP address differs, enter that address instead.';
        }catch(e){if(request===ipRequest&&edits===ipEdits&&version===configLoad)info.textContent=e.message;}
        finally{if(request===ipRequest)button.disabled=false;}
    }
    if(typeof document!=='undefined')document.addEventListener('input',function(e){if(e.target.id==='br_advertisedAddress'){ipEdits++;document.getElementById('brIpStatus').textContent='Manual IP address. Apply server settings to use it.';}});
    async function load(options) {
        var preserve=options&&options.preserveServerDraft;if(!preserve)configLoad++;
        try {
            var values = await Promise.all([send('GET','/bridge-local/server'),send('GET','/bridge-local/connections')]);
            var s = values[0]; connections = values[1];
            firewallStatus(s.firewall);
            if(!preserve){document.getElementById('brEnabled').checked = s.settings.enabled;
            ['bindAddress','advertisedAddress','port'].forEach(function(k) { document.getElementById('br_' + k).value = s.settings[k]; });
            document.getElementById('br_quota').value=s.settings.uploadQuotaBytes/1073741824;
            document.getElementById('br_rate').value=s.settings.transferBytesPerSecond/1048576;}
            document.getElementById('brIdentity').textContent = 'Node ID: ' + s.nodeId + '\nSHA-256: ' + s.fingerprint + '\nExpires: ' + s.expires + '\nURL: ' + (s.actualUrl || 'disabled') + '\n' + s.storageProtection + (s.error ? '\n' + s.error : '');
            document.getElementById('brConnections').innerHTML = connections.map(function(c) {return '<div class="br-row"><span>' + esc(c.name) + ' · ' + esc(c.address) + '</span><button class="btn" data-br-test="' + esc(c.id) + '">Test</button><button class="btn" data-br-edit="' + esc(c.id) + '">Edit</button><button class="btn danger" data-br-delete="' + esc(c.id) + '">Remove</button></div>';}).join('') || 'No connections';
            document.getElementById('brKeys').innerHTML = s.keys.map(function(k) {return '<div class="br-row"><span>' + esc(k.name) + (k.revoked ? ' · revoked' : '') + '</span>' + (k.revoked ? '' : '<button class="btn danger" data-br-revoke="' + esc(k.id) + '">Revoke</button>') + '</div>';}).join('');
            var revoked=s.keys.filter(function(k){return k.revoked;}).length;
            if(revoked)document.getElementById('brKeys').innerHTML+='<button class="btn" id="brClearRevoked">Clear revoked keys ('+revoked+')</button>';
            if(!autoIpTried&&!s.settings.enabled&&['','127.0.0.1','0.0.0.0','::1'].includes(s.settings.advertisedAddress)){autoIpTried=true;detectIp();}
        } catch(e) {message(e.message);}
    }
    if (typeof document !== 'undefined') document.addEventListener('click', async function(e) {
        var el = e.target.closest('button'); if (!el) return;
        try {
            if(el.id==='brCopyConnection'){el.disabled=true;try{await copyConnection();}catch(ex){keyMessage(ex.message);throw ex;}finally{el.disabled=!exportKey;}
            }else if(el.id==='brOpenFirewall'||el.id==='brCloseFirewall'){await changeFirewall(el.id==='brOpenFirewall'?'open':'close');
            }else if(el.id==='brCheckFirewall'){await checkFirewall(el);
            }else if (el.id === 'brDetectIp') {await detectIp();
            } else if (el.id === 'brSaveConnection') {
                await controller.save({id:input('brConnectionId'),name:input('brName'),address:input('brAddress'),nodeId:input('brNodeId'),fingerprint:input('brFingerprint'),key:input('brSecret')});
                document.getElementById('brSecret').value = ''; message('Connection saved','connection'); await load({preserveServerDraft:true});
            } else if (el.id === 'brNewConnection') {
                ['brConnectionId','brName','brAddress','brNodeId','brFingerprint','brSecret'].forEach(function(id) {document.getElementById(id).value='';});
            } else if (el.dataset.brEdit) {
                var c = connections.find(function(c) {return c.id === el.dataset.brEdit;});
                [['brConnectionId',c.id],['brName',c.name],['brAddress',c.address],['brNodeId',c.nodeId],['brFingerprint',c.fingerprint],['brSecret','']].forEach(function(pair) {document.getElementById(pair[0]).value=pair[1];});
            } else if (el.dataset.brTest) {await send('POST','/bridge-local/connections/'+el.dataset.brTest+'/test',{});message('Connected · server identity verified','connection');
            } else if (el.dataset.brDelete && confirm('Remove this connection?')) {await send('DELETE','/bridge-local/connections/'+el.dataset.brDelete); await load({preserveServerDraft:true});
            } else if (el.id==='brClearRevoked') {
                if(el.disabled)return;el.disabled=true;keyMessage('Clearing revoked keys…');
                try{var cleared=await send('DELETE','/bridge-local/keys/revoked');await load({preserveServerDraft:true});keyMessage('Removed revoked keys: '+cleared.removed+'.');}
                catch(ex){keyMessage(ex.message);throw ex;}finally{el.disabled=false;}
            } else if (el.dataset.brRevoke && confirm('Revoke this key and interrupt its transfers?')) {await send('DELETE','/bridge-local/keys/'+el.dataset.brRevoke);await load({preserveServerDraft:true});keyMessage('Access key revoked.');
            } else if (el.id === 'brCreateKey') {
                if(el.disabled)return;var label=el.textContent;el.disabled=true;el.textContent='Creating…';message('');keyMessage('Creating access key…');
                try{var key = await send('POST','/bridge-local/keys',{name:input('brKeyName')});
                    if(!key||typeof key.secret!=='string'||!key.secret)throw new Error('The backend did not return an access key. Check that the updated application is running.');
                    exportKey={id:key.id,secret:key.secret};var copyButton=document.getElementById('brCopyConnection');if(copyButton)copyButton.disabled=false;
                    document.getElementById('brNewKey').value=key.secret;keyMessage('Access key created. Click Copy connection to copy all connection details at once.');await load({preserveServerDraft:true});
                }catch(e){keyMessage(e.message);throw e;}finally{el.disabled=false;el.textContent=label;}
            } else if (el.id === 'brRenewCertificate') {
                if(!confirm('Renew the server certificate? Update its fingerprint in every client.'))return;
                var existing=await send('GET','/bridge-local/server');await send('PUT','/bridge-local/server',Object.assign({},existing.settings,{renewCertificate:true}));await load({preserveServerDraft:true});message('Certificate renewed');
            } else if (el.id === 'brApplyServer') {
                var current=await send('GET','/bridge-local/server');
                var ip=input('br_advertisedAddress');
                if (ip !== current.settings.advertisedAddress && !confirm('Changing the server IP creates a new certificate. Update its fingerprint in your clients. Continue?')) return;
                var settings=Object.assign({},current.settings,{enabled:document.getElementById('brEnabled').checked,bindAddress:input('br_bindAddress'),advertisedAddress:ip,port:Number(input('br_port'))});
                settings.uploadQuotaBytes=Math.floor(Number(input('br_quota'))*1073741824);settings.transferBytesPerSecond=Math.floor(Number(input('br_rate'))*1048576);
                settings.openFirewall=document.getElementById('brFirewallOnApply').checked&&!document.getElementById('brFirewallOnApply').disabled;
                el.disabled=true;message(settings.enabled&&settings.openFirewall?'Applying settings and opening Windows Firewall. Approve the administrator prompt if shown.':'Applying server settings…');
                try{await send('PUT','/bridge-local/server',settings);await load();message('Server settings applied');}finally{el.disabled=false;}
            }
        } catch(err) {var connectionAction=el.id==='brSaveConnection'||el.dataset.brTest||el.dataset.brDelete;message(err.message,connectionAction?'connection':'server');}
    });
    if (typeof module !== 'undefined') module.exports = {createConfigController:createConfigController};
    root.BridgeConfig = {send:send, load:load};
})(typeof window !== 'undefined' ? window : globalThis);
