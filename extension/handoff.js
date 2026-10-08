// Browser handoff policy; independent of Chrome so failure paths can be tested.
export function supported(item) {
  try {
    const u = new URL(item.finalUrl || item.url);
    return ['http:', 'https:'].includes(u.protocol) && !u.username && !u.password &&
      item.state === 'in_progress' && !item.byExtensionId && item.totalBytes > 0 &&
      (!item.danger || item.danger === 'safe') &&
      !['text/html', 'application/xhtml+xml'].includes((item.mime || '').toLowerCase());
  } catch { return false; }
}
export async function transfer(item, requestId, io) {
  const req = {version: 1, requestId, command: 'prepare', url: item.finalUrl || item.url,
    suggestedName: (item.filename || '').split(/[\\/]/).pop(), expectedBytes: item.totalBytes,
    mime: item.mime || '', incognito: !!item.incognito};
  let cancelled = false;
  try {
    await io.save('pausing');
    await io.pause(item.id);
    await io.save('preparing');
    const ready = await io.native(req);
    if (!ready.ok || ready.status !== 'ready') throw new Error('unsupported');
    await io.save('ready');
    const current = await io.get(item.id);
    if (!current || current.state !== 'in_progress' || !current.paused) throw new Error('browser changed');
    await io.save('cancelling');
    await io.cancel(item.id);
    cancelled = true;
    await io.save('cancelled');
    let result;
    try { result = await io.native({version: 1, requestId, command: 'commit'}); }
    catch { result = await io.native({version: 1, requestId, command: 'commit'}); }
    if (!result.ok) throw new Error('commit failed');
    await io.save('done');
    return 'transferred';
  } catch {
    if (cancelled) {
      // Never restart the browser after cancellation: the durable QuickGrab job owns it.
      await io.save('cancelled').catch(() => {});
      return 'pending';
    }
    await io.native({version: 1, requestId, command: 'abort'}).catch(() => {});
    const current = await io.get(item.id).catch(() => null);
    if (current?.state === 'in_progress' && current.paused) await io.resume(item.id).catch(() => {});
    await io.save('browser').catch(() => {});
    return 'browser';
  }
}
