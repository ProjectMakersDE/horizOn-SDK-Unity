mergeInto(LibraryManager.library, {
  $HorizonRealtimeSockets: { next: 1, sockets: {} },
  HorizonRealtimeConnect__deps: ['$HorizonRealtimeSockets'],
  HorizonRealtimeConnect: function(urlPtr, objectPtr) {
    var id = HorizonRealtimeSockets.next++;
    var objectName = UTF8ToString(objectPtr);
    var socket = new WebSocket(UTF8ToString(urlPtr));
    HorizonRealtimeSockets.sockets[id] = socket;
    socket.onopen = function() { SendMessage(objectName, 'OnHorizonRealtimeOpen', String(id)); };
    socket.onmessage = function(event) {
      if (typeof event.data === 'string' && event.data.length <= 8192) SendMessage(objectName, 'OnHorizonRealtimeMessage', id + '|' + event.data);
      else socket.close();
    };
    socket.onclose = function() { delete HorizonRealtimeSockets.sockets[id]; SendMessage(objectName, 'OnHorizonRealtimeClosed', String(id)); };
    return id;
  },
  HorizonRealtimeSend__deps: ['$HorizonRealtimeSockets'],
  HorizonRealtimeSend: function(id, framePtr) {
    var socket = HorizonRealtimeSockets.sockets[id];
    if (socket && socket.readyState === 1 && socket.bufferedAmount < 65536) socket.send(UTF8ToString(framePtr));
    else if (socket) socket.close();
  },
  HorizonRealtimeClose__deps: ['$HorizonRealtimeSockets'],
  HorizonRealtimeClose: function(id) {
    var socket = HorizonRealtimeSockets.sockets[id];
    if (socket) { socket.onopen = socket.onmessage = socket.onclose = null; socket.close(); delete HorizonRealtimeSockets.sockets[id]; }
  }
});
