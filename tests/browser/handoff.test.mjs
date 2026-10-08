import assert from 'node:assert/strict';
import {supported, transfer} from '../../extension/handoff.js';
const item={id:1,state:'in_progress',totalBytes:100000,url:'https://example.test/file.zip',filename:'file.zip',danger:'safe',incognito:false};
let passed=0;
async function test(name,action){await action();passed++;console.log('PASS '+name);}
function mock(options={}){
 const calls=[];let paused=false;let cancelled=false;
 const io={calls,save:async p=>{calls.push('save:'+p);},pause:async()=>{calls.push('pause');paused=true;},
 get:async()=>({...item,paused,state:cancelled?'interrupted':options.completed?'complete':'in_progress'}),
 cancel:async()=>{calls.push('cancel');if(options.cancelFails)throw Error();cancelled=true;},
 resume:async()=>{calls.push('resume');paused=false;},
 native:async req=>{calls.push(req.command);
  if(req.command==='prepare')return {ok:!options.reject,status:options.reject?'unsupported':'ready'};
  if(req.command==='commit'&&options.commitFails)throw Error('offline');
  return {ok:true};}};return io;
}
await test('Handoff prepares before cancel and commits after cancel',async()=>{const m=mock();assert.equal(await transfer(item,'id',m),'transferred');assert(m.calls.indexOf('prepare')<m.calls.indexOf('cancel'));assert(m.calls.indexOf('cancel')<m.calls.indexOf('commit'));assert(!m.calls.includes('resume'));});
await test('Rejected native preparation resumes browser and never cancels',async()=>{const m=mock({reject:true});assert.equal(await transfer(item,'id',m),'browser');assert(m.calls.includes('resume'));assert(!m.calls.includes('cancel'));assert(!m.calls.includes('commit'));});
await test('Failed browser cancel aborts pending job and resumes browser',async()=>{const m=mock({cancelFails:true});assert.equal(await transfer(item,'id',m),'browser');assert(m.calls.includes('abort'));assert(m.calls.includes('resume'));assert(!m.calls.includes('commit'));});
await test('Commit failure retains pending app job without browser duplicate',async()=>{const m=mock({commitFails:true});assert.equal(await transfer(item,'id',m),'pending');assert(!m.calls.includes('resume'));assert(!m.calls.includes('abort'));});
await test('Completed browser file is not handed off',async()=>{const m=mock({completed:true});assert.equal(await transfer(item,'id',m),'browser');assert(!m.calls.includes('cancel'));});
await test('Incognito flag is forwarded explicitly',async()=>{const m=mock();const base=m.native;m.native=async req=>{if(req.command==='prepare')assert.equal(req.incognito,true);return base(req);};assert.equal(await transfer({...item,incognito:true},'id',m),'transferred');});
await test('HTML, blob, credentials, unsafe downloads and unknown sizes are excluded',async()=>{for(const change of [{mime:'text/html'},{url:'blob:https://example.test/id'},{url:'https://u:p@example.test/file'},{danger:'dangerous'},{totalBytes:-1},{byExtensionId:'another-extension'},{state:'complete'}])assert.equal(supported({...item,...change}),false);assert.equal(supported(item),true);});
console.log(`${passed}/${passed} browser handoff tests passed`);
