mergeInto(LibraryManager.library, {
    _sendMessage: function (type, message) {
        var jsType = UTF8ToString(type);
        var jsMessage = UTF8ToString(message);
        
        sendMessage(jsType, jsMessage);
    },
    
    _openAuthPopup: function (urlPtr) {
        const url = UTF8ToString(urlPtr);
    
        window.open(
          url,
          'oidc_popup',
          'width=480,height=720,resizable=yes,scrollbars=yes'
        );
      }
});