const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const instances = [];
const callbacks = [];
class BrowserSocket {
  constructor(url) { this.url = url; this.readyState = 0; this.bufferedAmount = 0; this.sent = []; instances.push(this); }
  send(frame) { this.sent.push(frame); }
  close() { this.readyState = 3; this.onclose?.(); }
  open() { this.readyState = 1; this.onopen?.(); }
}
const context = { LibraryManager: { library: {} }, mergeInto: Object.assign, WebSocket: BrowserSocket,
  UTF8ToString: x => x, SendMessage: (...args) => callbacks.push(args) };
vm.createContext(context);
vm.runInContext(fs.readFileSync('CloudSDK/Plugins/WebGL/HorizonRealtime.jslib', 'utf8'), context);
const api = context.LibraryManager.library;
context.HorizonRealtimeSockets = api.$HorizonRealtimeSockets;
const first = api.HorizonRealtimeConnect('wss://example.invalid/api/v1/app/realtime/socket', 'manager');
instances[0].open();
assert.equal(callbacks[0][1], 'OnHorizonRealtimeOpen');
assert.equal(callbacks[0][2], String(first));
api.HorizonRealtimeSend(first, '{"type":"ping"}');
assert.equal(instances[0].sent[0], '{"type":"ping"}');
instances[0].onmessage({ data: '{"type":"ready","version":1}' });
assert.equal(callbacks[1][2], first + '|{"type":"ready","version":1}');
api.HorizonRealtimeClose(first);
assert.equal(api.$HorizonRealtimeSockets.sockets[first], undefined);
const second = api.HorizonRealtimeConnect('wss://example.invalid/api/v1/app/realtime/socket', 'manager');
instances[1].open();
assert.notEqual(first, second, 'late callbacks retain their old transport identity');
instances[1].onmessage({ data: 'x'.repeat(65536) });
assert.equal(instances[1].readyState, 1, 'a snapshot frame up to 64 KiB passes');
instances[1].onmessage({ data: 'x'.repeat(65537) });
assert.equal(instances[1].readyState, 3, 'oversized incoming frames close');
assert.deepEqual(callbacks.at(-1).slice(1), ['OnHorizonRealtimeClosed', second + '|0']);
const restart = api.HorizonRealtimeConnect('wss://example.invalid/api/v1/app/realtime/socket', 'manager');
instances.at(-1).open(); instances.at(-1).onclose({ code: 1012 });
assert.deepEqual(callbacks.at(-1).slice(1), ['OnHorizonRealtimeClosed', restart + '|1012'], 'the close code reaches the manager');
const third = api.HorizonRealtimeConnect('wss://example.invalid/api/v1/app/realtime/socket', 'manager');
instances.at(-1).open(); instances.at(-1).bufferedAmount = 65537;
api.HorizonRealtimeSend(third, '{}');
assert.equal(instances.at(-1).readyState, 3, 'outgoing buffers are bounded');
console.log('WebGL realtime bridge checks passed');
