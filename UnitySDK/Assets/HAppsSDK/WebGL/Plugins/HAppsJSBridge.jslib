mergeInto(LibraryManager.library, {

    _sendMessage: function (typePtr, messagePtr) {
        var jsType = UTF8ToString(typePtr);
        var jsMessage = UTF8ToString(messagePtr);
        
        if (typeof window.HApps === "undefined" || typeof window.HApps.onUnityEvent !== "function") {
            console.error("[HApps] window.HApps.onUnityEvent not available");
            return;
        }

        window.HApps.onUnityEvent(jsType, jsMessage);
    },
    
    _isPortalSite: function () {
    
        if (typeof window.HApps === "undefined")
            return 0;

        if (typeof window.HApps.getUser !== "function")
            return 0;

        return window.HApps.getUser() == null ? 0 : 1;
    },
    
    _redirect: function (urlPtr) {
        var url = UTF8ToString(urlPtr);
        window.location.href = url;
    }
});