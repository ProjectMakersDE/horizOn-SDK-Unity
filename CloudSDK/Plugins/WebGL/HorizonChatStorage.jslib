mergeInto(LibraryManager.library, {
  $HorizonChatStorage: {
    busy: false, dirty: false, target: '',
    flush: function () {
      if (HorizonChatStorage.busy || !HorizonChatStorage.dirty) return;
      HorizonChatStorage.busy = true;
      HorizonChatStorage.dirty = false;
      var target = HorizonChatStorage.target;
      var done = function (error) {
        HorizonChatStorage.busy = false;
        if (error) SendMessage(target, 'OnHorizonChatStorageError', 'LOCAL_HISTORY_IO_FAILED');
        // Writes that arrived during sync require one additional flush. A failed flush
        // does not spin/retry forever and never causes a server request.
        HorizonChatStorage.flush();
      };
      try { FS.syncfs(false, done); } catch (error) { done(error); }
    }
  },
  HorizonChatFlushStorage__deps: ['$HorizonChatStorage'],
  HorizonChatFlushStorage: function (objectName) {
    HorizonChatStorage.target = UTF8ToString(objectName);
    HorizonChatStorage.dirty = true;
    HorizonChatStorage.flush();
  }
});
