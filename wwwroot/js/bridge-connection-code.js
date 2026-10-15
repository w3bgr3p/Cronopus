(function(root){
 'use strict';
 function normalizeAddress(value){
  var text=String(value||'').trim(),bare=!text.includes('://');
  if(bare)text='https://'+text;
  var url;try{url=new URL(text);}catch(e){throw new Error('Enter a server IP or HTTPS address.');}
  if(url.protocol!=='https:'||!url.hostname||url.username||url.password||url.pathname!=='/'||url.search||url.hash)throw new Error('Use an HTTPS server address without a path or credentials.');
  if(bare&&!url.port&&!/:\d+$/.test(text))url.port='38473';
  return url.origin;
 }
 function validate(value){
  if(!value||value.type!=='z3ndash-connection'||value.version!==1)throw new Error('Unsupported connection code. Copy a new code from the server.');
  var out={};['name','address','secret','nodeId','fingerprint'].forEach(function(key){if(typeof value[key]!=='string'||!value[key].trim())throw new Error('Connection code is incomplete: '+key+'.');out[key]=value[key].trim();});
  if(!out.address.startsWith('https://'))throw new Error('Connection code must contain an HTTPS address.');
  out.address=normalizeAddress(out.address);out.fingerprint=out.fingerprint.replace(/[:\s]/g,'');
  if(!/^[a-f0-9]{64}$/i.test(out.fingerprint)||!/^(?:[a-f0-9]{32}|[a-f0-9]{8}(?:-[a-f0-9]{4}){3}-[a-f0-9]{12})$/i.test(out.nodeId))throw new Error('Connection code has an invalid server identity or certificate fingerprint.');
  if(out.name.length>128||out.secret.length>4096)throw new Error('Connection code contains an oversized field.');
  return out;
 }
 function encode(value){var input=Object.assign({type:'z3ndash-connection',version:1},value);return JSON.stringify(Object.assign({type:input.type,version:input.version},validate(input)));}
 function parse(text){if(typeof text!=='string'||text.length>16384)throw new Error('Connection code is too large.');var value;try{value=JSON.parse(text.trim());}catch(e){throw new Error('Invalid connection code. Paste the complete JSON copied from the server.');}return validate(value);}
 var api={encode:encode,parse:parse,normalizeAddress:normalizeAddress};
 if(typeof module!=='undefined')module.exports=api;root.BridgeConnectionCode=api;
})(typeof window!=='undefined'?window:globalThis);
