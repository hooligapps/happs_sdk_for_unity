mergeInto(LibraryManager.library, {

    _sendMessage: function (typePtr, messagePtr) {
        var jsType = UTF8ToString(typePtr);
        var jsMessage = UTF8ToString(messagePtr);
        
        if (typeof window.HApps === "undefined" || typeof window.HApps.onUnityEvent !== "function") {
            console.error("[HApps] window.HApps.onUnityEvent not available");
            return;
        }

        window.HApps.onUnityEvent(jsType, jsMessage);
    }
});