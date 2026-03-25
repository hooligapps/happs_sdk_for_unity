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

        return window.HApps.isPortal ? 1 : 0;
    },
    
    _redirect: function (urlPtr) {
        var url = UTF8ToString(urlPtr);
        window.location.href = url;
    }
});
