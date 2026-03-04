mergeInto(LibraryManager.library, {

    _sendMessage: function (typePtr, messagePtr) {
        var type = UTF8ToString(typePtr);
        var message = UTF8ToString(messagePtr);

        if (typeof window.HApps === "undefined" || typeof window.HApps.onUnityEvent !== "function") {
            console.error("[HApps] window.HApps.onUnityEvent not available");
            return;
        }

        window.HApps.onUnityEvent(type, message);
    }
});
