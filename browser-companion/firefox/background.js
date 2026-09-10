const bridgeKey = self.ITSQMET_BRIDGE_KEY || '';
function safe(raw){try{const u=new URL(raw);if(!['http:','https:'].includes(u.protocol))return null;return `${u.protocol}//${u.host}${u.pathname}`;}catch{return null}}
browser.webNavigation.onCommitted.addListener(async d=>{if(d.frameId!==0)return;const url=safe(d.url);if(!url||!bridgeKey)return;try{await fetch('http://127.0.0.1:43119/browser-event',{method:'POST',headers:{'Content-Type':'application/json','X-ITSQMET-Bridge':bridgeKey},body:JSON.stringify({url,title:null,browser:'firefox'})})}catch{}});
