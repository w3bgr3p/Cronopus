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
        var data = await response.json(); if (!response.ok) throw new Error(data.error || 'Request failed (' + response.status + ')'); return data;
    }
    function esc(value) { return String(value == null ? '' : value).replace(/[&<>"']/g, function(c) {return {'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c];}); }
    function input(id) { return document.getElementById(id).value.trim(); }
    function message(text) { document.getElementById('brMessage').textContent = text; }
    var connections = [], controller = createConfigController({send:send}),ipRequest=0,ipEdits=0,configLoad=0,autoIpTried=false;
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
    async function load() {
        configLoad++;
        try {
            var values = await Promise.all([send('GET','/bridge-local/server'),send('GET','/bridge-local/connections')]);
            var s = values[0]; connections = values[1];
            document.getElementById('brEnabled').checked = s.settings.enabled;
            ['bindAddress','advertisedAddress','port'].forEach(function(k) { document.getElementById('br_' + k).value = s.settings[k]; });
            document.getElementById('br_quota').value=s.settings.uploadQuotaBytes/1073741824;
            document.getElementById('br_rate').value=s.settings.transferBytesPerSecond/1048576;
            document.getElementById('brIdentity').textContent = 'Node ID: ' + s.nodeId + '\nSHA-256: ' + s.fingerprint + '\nExpires: ' + s.expires + '\nURL: ' + (s.actualUrl || 'disabled') + '\n' + s.storageProtection + (s.error ? '\n' + s.error : '');
            document.getElementById('brConnections').innerHTML = connections.map(function(c) {return '<div class="br-row"><span>' + esc(c.name) + ' · ' + esc(c.address) + '</span><button class="btn" data-br-test="' + esc(c.id) + '">Test</button><button class="btn" data-br-edit="' + esc(c.id) + '">Edit</button><button class="btn danger" data-br-delete="' + esc(c.id) + '">Remove</button></div>';}).join('') || 'No connections';
            document.getElementById('brKeys').innerHTML = s.keys.map(function(k) {return '<div class="br-row"><span>' + esc(k.name) + (k.revoked ? ' · revoked' : '') + '</span>' + (k.revoked ? '' : '<button class="btn danger" data-br-revoke="' + esc(k.id) + '">Revoke</button>') + '</div>';}).join('');
            if(!autoIpTried&&!s.settings.enabled&&['','127.0.0.1','0.0.0.0','::1'].includes(s.settings.advertisedAddress)){autoIpTried=true;detectIp();}
        } catch(e) {message(e.message);}
    }
    if (typeof document !== 'undefined') document.addEventListener('click', async function(e) {
        var el = e.target.closest('button'); if (!el) return;
        try {
            if (el.id === 'brDetectIp') {await detectIp();
            } else if (el.id === 'brSaveConnection') {
                await controller.save({id:input('brConnectionId'),name:input('brName'),address:input('brAddress'),nodeId:input('brNodeId'),fingerprint:input('brFingerprint'),key:input('brSecret')});
                document.getElementById('brSecret').value = ''; message('Connection saved'); await load();
            } else if (el.id === 'brNewConnection') {
                ['brConnectionId','brName','brAddress','brNodeId','brFingerprint','brSecret'].forEach(function(id) {document.getElementById(id).value='';});
            } else if (el.dataset.brEdit) {
                var c = connections.find(function(c) {return c.id === el.dataset.brEdit;});
                [['brConnectionId',c.id],['brName',c.name],['brAddress',c.address],['brNodeId',c.nodeId],['brFingerprint',c.fingerprint],['brSecret','']].forEach(function(pair) {document.getElementById(pair[0]).value=pair[1];});
            } else if (el.dataset.brTest) {await send('POST','/bridge-local/connections/'+el.dataset.brTest+'/test',{});message('Connected · server identity verified');
            } else if (el.dataset.brDelete && confirm('Remove this connection?')) {await send('DELETE','/bridge-local/connections/'+el.dataset.brDelete); await load();
            } else if (el.dataset.brRevoke && confirm('Revoke this key and interrupt its transfers?')) {await send('DELETE','/bridge-local/keys/'+el.dataset.brRevoke);await load();
            } else if (el.id === 'brCreateKey') {
                var key = await send('POST','/bridge-local/keys',{name:input('brKeyName')}); await load(); document.getElementById('brNewKey').value=key.secret;
            } else if (el.id === 'brRenewCertificate') {
                if(!confirm('Renew the server certificate? Update its fingerprint in every client.'))return;
                var existing=await send('GET','/bridge-local/server');await send('PUT','/bridge-local/server',Object.assign({},existing.settings,{renewCertificate:true}));await load();message('Certificate renewed');
            } else if (el.id === 'brApplyServer') {
                var current=await send('GET','/bridge-local/server');
                var ip=input('br_advertisedAddress');
                if (ip !== current.settings.advertisedAddress && !confirm('Changing the server IP creates a new certificate. Update its fingerprint in your clients. Continue?')) return;
                var settings=Object.assign({},current.settings,{enabled:document.getElementById('brEnabled').checked,bindAddress:input('br_bindAddress'),advertisedAddress:ip,port:Number(input('br_port'))});
                settings.uploadQuotaBytes=Math.floor(Number(input('br_quota'))*1073741824);settings.transferBytesPerSecond=Math.floor(Number(input('br_rate'))*1048576);
                await send('PUT','/bridge-local/server',settings); await load(); message('Server settings applied');
            }
        } catch(err) {message(err.message);}
    });
    if (typeof module !== 'undefined') module.exports = {createConfigController:createConfigController};
    root.BridgeConfig = {send:send, load:load};
})(typeof window !== 'undefined' ? window : globalThis);
