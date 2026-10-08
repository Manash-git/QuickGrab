const auto = document.getElementById('auto'), privateBox = document.getElementById('private');
const settings = await chrome.storage.local.get({autoCapture:true,privateCapture:true});
auto.checked=settings.autoCapture; privateBox.checked=settings.privateCapture;
auto.addEventListener('change',()=>chrome.storage.local.set({autoCapture:auto.checked}));
privateBox.addEventListener('change',()=>chrome.storage.local.set({privateCapture:privateBox.checked}));
const {lastStatus}=await chrome.storage.session.get('lastStatus');
if(lastStatus) document.getElementById('last').textContent=lastStatus.text;
document.getElementById('test').addEventListener('click',async()=>{
 const result=document.getElementById('result'); result.textContent='Connecting…';
 try{const reply=await chrome.runtime.sendMessage({command:'testConnection'});result.textContent=reply.ok?`Connected to QuickGrab ${reply.version}`:reply.message;}
 catch{result.textContent='Connection failed. Check native-host registration.';}
});
