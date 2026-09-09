export const API=import.meta.env.VITE_API_URL||'http://localhost:5080';
export function key(){return sessionStorage.getItem('itsqmet-admin-key')||''}
export async function api(path:string,init:RequestInit={}){const r=await fetch(API+path,{...init,headers:{'Content-Type':'application/json','X-ITSQMET-Key':key(),...(init.headers||{})}});if(r.status===204)return null;if(!r.ok)throw new Error(await r.text());return r.json()}
