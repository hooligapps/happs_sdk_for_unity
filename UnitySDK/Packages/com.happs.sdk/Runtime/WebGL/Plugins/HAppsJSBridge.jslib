mergeInto(LibraryManager.library, {

    _sendMessage: function (typePtr, messagePtr) {
        var jsType = UTF8ToString(typePtr);
        var jsMessage = UTF8ToString(messagePtr);
        
        if (typeof window.HApps === "undefined" ||
            !window.HApps.unity ||
            typeof window.HApps.unity.receive !== "function") {
            console.error("[HApps] window.HApps.unity.receive not available");
            return;
        }

        window.HApps.unity.receive(jsType, jsMessage);
    },
    
    _isPortalSite: function () {
    
        if (typeof window.HApps === "undefined")
            return 0;

        if (typeof window.HApps.isPortal !== "function")
            return 0;

        return window.HApps.isPortal() ? 1 : 0;
    },

    _isReady: function () {
        if (typeof window.HApps === "undefined")
            return 0;

        if (typeof window.HApps.isReady !== "function")
            return 0;

        return window.HApps.isReady() ? 1 : 0;
    },
    
    _redirect: function (urlPtr) {
        var url = UTF8ToString(urlPtr);
        window.location.href = url;
    },

    _focusWindow: function () {
        try {
            window.focus();
        } catch (e) {
        }
    }
});
