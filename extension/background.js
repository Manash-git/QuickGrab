import {supported, transfer} from './handoff.js';
const HOST = 'com.quickgrab.bridge';
const privateContext = !!chrome.extension.inIncognitoContext;
const prefix = `handoff:${privateContext ? 'private' : 'normal'}:`;
const busy = new Set();
const defaults = {autoCapture: true, privateCapture: true};
function native(request) {
  return new Promise((resolve, reject) => {
    const port = chrome.runtime.connectNative(HOST);
    let finished = false;
    const finish = (value, error) => {
      if (finished) return; finished = true; clearTimeout(timer); port.disconnect();
      if (error) reject(new Error(error)); else resolve(value);
    };
    const timer = setTimeout(() => finish(null, 'Native host timed out'), 43000);
    port.onMessage.addListener(reply => finish(reply));
    port.onDisconnect.addListener(() => finish(null, chrome.runtime.lastError?.message || 'Native host disconnected'));
    port.postMessage(request);
  });
}
async function status(text, warning = false) {
  await chrome.storage.session.set({lastStatus: {text, time: Date.now()}});
  await chrome.action.setBadgeText({text: warning ? '!' : ''});
  if (warning) await chrome.action.setBadgeBackgroundColor({color: '#B45309'});
}
const get = async id => (await chrome.downloads.search({id}))[0];
function ioFor(id, requestId) {
  return {native, get, pause: id => chrome.downloads.pause(id), cancel: id => chrome.downloads.cancel(id),
    resume: id => chrome.downloads.resume(id),
    save: phase => chrome.storage.session.set({[prefix + id]: {id, requestId, phase}})};
}
async function consider(initial) {
  if (!!initial.incognito !== privateContext || busy.has(initial.id)) return;
  busy.add(initial.id);
  try {
    const settings = await chrome.storage.local.get(defaults);
    if (!settings.autoCapture || (privateContext && !settings.privateCapture)) return;
    if ((await chrome.storage.session.get(prefix + initial.id))[prefix + initial.id]) return;
    let item = initial;
    // Metadata may not yet be available when onCreated fires.
    for (let i = 0; item?.state === 'in_progress' && item.totalBytes <= 0 && i < 10; i++) {
      await new Promise(r => setTimeout(r, 200)); item = await get(initial.id);
    }
    if (!item || !supported(item) || item.paused) return;
    const requestId = crypto.randomUUID();
    const result = await transfer(item, requestId, ioFor(item.id, requestId));
    await report(result);
  } finally { busy.delete(initial.id); }
}
// Bind the same durable request ID into both protocol and session state.
async function report(result) {
  if (result === 'transferred') await status('Download sent to QuickGrab.');
  else if (result === 'pending') await status('File saved as a pending QuickGrab job. Open QuickGrab and select Resume.', true);
  else await status('This download stayed in the browser. Check QuickGrab connection or use a direct public file link.', true);
}
async function recover() {
  const records = await chrome.storage.session.get(null);
  for (const [key, record] of Object.entries(records)) {
    if (!key.startsWith(prefix) || busy.has(record.id) || ['done','browser'].includes(record.phase)) continue;
    busy.add(record.id);
    try {
      const item = await get(record.id);
      const io = ioFor(record.id, record.requestId);
      if (record.phase === 'cancelled' || (record.phase === 'cancelling' && item?.state === 'interrupted' && item.error === 'USER_CANCELED')) {
        const reply = await native({version: 1, requestId: record.requestId, command: 'commit'});
        if (reply.ok) { await io.save('done'); await report('transferred'); }
        else await report('pending');
      } else {
        await native({version: 1, requestId: record.requestId, command: 'abort'}).catch(() => {});
        if (item?.state === 'in_progress' && item.paused) await chrome.downloads.resume(record.id);
        await io.save('browser');
      }
    } catch { await report('pending'); }
    finally { busy.delete(record.id); }
  }
  // Finished markers contain IDs only; retain a bounded recent set during this session.
  const finished = Object.entries(records).filter(([key, r]) => key.startsWith(prefix) && ['done','browser'].includes(r.phase));
  if (finished.length > 200) await chrome.storage.session.remove(finished.slice(0, finished.length - 200).map(([k]) => k));
}
chrome.runtime.onInstalled.addListener(() => {
  chrome.contextMenus.removeAll(() => {
    chrome.contextMenus.create({id: 'quickgrab-link', title: 'Download with QuickGrab', contexts: ['link'], targetUrlPatterns: ['http://*/*','https://*/*']});
  });
});
chrome.downloads.onCreated.addListener(item => { void consider(item).catch(() => status('Could not capture download. Use the browser download list.', true)); });
chrome.downloads.onChanged.addListener(delta => {
  void get(delta.id).then(item => item && consider(item)).catch(() => {});
});
chrome.contextMenus.onClicked.addListener((info, tab) => {
  if (info.menuItemId !== 'quickgrab-link' || !!tab?.incognito !== privateContext) return;
  void (async () => {
    const incognito = !!tab?.incognito;
    const settings = await chrome.storage.local.get(defaults);
    if (incognito && !settings.privateCapture) { await status('Private-window capture is turned off in QuickGrab extension settings.', true); return; }
    const requestId = crypto.randomUUID();
    let prepared = false;
    try {
      const ready = await native({version: 1, requestId, command: 'prepare', url: info.linkUrl, incognito});
      if (!ready.ok) { await status(ready.message || 'This link is not supported. Use the browser.', true); return; }
      prepared = true;
      const started = await native({version: 1, requestId, command: 'commit'});
      await report(started.ok ? 'transferred' : 'pending');
    } catch { await status(prepared ? 'Open QuickGrab and Resume the pending job.' : 'QuickGrab is unavailable. Run RegisterBrowser.cmd, then Test connection.', true); }
  })();
});
chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (sender.id !== chrome.runtime.id || message?.command !== 'testConnection') return false;
  native({version: 1, requestId: crypto.randomUUID(), command: 'ping'})
    .then(reply => sendResponse(reply)).catch(() => sendResponse({ok: false, message: 'No connection. Run RegisterBrowser.cmd and open QuickGrab 0.2.0.'}));
  return true;
});
void recover().catch(() => {});
