const fs = require('node:fs');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const pending = [], events = [];
const context = { LibraryManager: { library: {} }, mergeInto: Object.assign,
  UTF8ToString: x => x, SendMessage: (...args) => events.push(args),
  FS: { syncfs: (populate, callback) => { assert.equal(populate, false); pending.push(callback); } } };
vm.createContext(context);
vm.runInContext(fs.readFileSync('CloudSDK/Plugins/WebGL/HorizonChatStorage.jslib', 'utf8'), context);
const api = context.LibraryManager.library;
context.HorizonChatStorage = api.$HorizonChatStorage;
api.HorizonChatFlushStorage('chat');
for (let i = 0; i < 100; i++) api.HorizonChatFlushStorage('chat');
assert.equal(pending.length, 1);
pending.shift()(null);
assert.equal(pending.length, 1);
pending.shift()(null);
assert.equal(pending.length, 0);
assert.equal(events.length, 0);
api.HorizonChatFlushStorage('chat');
pending.shift()(new Error('quota'));
assert.equal(pending.length, 0);
assert.equal(events[0][2], 'LOCAL_HISTORY_IO_FAILED');
context.FS.syncfs = () => { throw new Error('missing filesystem'); };
api.HorizonChatFlushStorage('chat');
assert.equal(events.length, 2);
assert.equal(context.HorizonChatStorage.busy, false);
console.log('WebGL Chat storage: coalesced IndexedDB flushes and visible failures passed.');
