mergeInto(LibraryManager.library, {

    _sendMessage: function (typePtr, messagePtr) {
        var type = UTF8ToString(typePtr);
        var message = UTF8ToString(messagePtr);

        if (typeof window.HApps === "undefined" || typeof window.HApps.onUnityEvent !== "function") {
            console.error("[HApps] window.HApps.onUnityEvent not available");
            return;
        }

        window.HApps.onUnityEvent(type, message);
    },

    _openAuthPopup: function (urlPtr) {

        var url = UTF8ToString(urlPtr);

        // Validate URL scheme — only allow https
        if (url.indexOf("https://") !== 0) {
            console.error("[HApps] Auth popup URL rejected: must use https://");
            return;
        }

        var expectedOrigin = new URL(url).origin;

        var width = 480;
        var height = 770;

        var dualScreenLeft = window.screenLeft !== undefined ? window.screenLeft : window.screenX;
        var dualScreenTop = window.screenTop !== undefined ? window.screenTop : window.screenY;

        var windowWidth = window.innerWidth || document.documentElement.clientWidth || screen.width;
        var windowHeight = window.innerHeight || document.documentElement.clientHeight || screen.height;

        var left = dualScreenLeft + (windowWidth - width) / 2;
        var top = dualScreenTop + (windowHeight - height) / 2;

        var popup = window.open(
            url,
            "oidc_popup",
            "width=" + width + ",height=" + height + ",left=" + left + ",top=" + top + ",resizable=yes,scrollbars=yes"
        );

        function sendAuthTicket(ticket) {
            if (typeof window.HApps !== "undefined" && typeof window.HApps.onUnityEvent === "function") {
                window.HApps.onUnityEvent("authTicket", JSON.stringify({ authTicket: ticket || "" }));
            }
        }

        if (!popup) {
            console.warn("[HApps] Popup blocked by browser");
            sendAuthTicket("");
            return;
        }

        var resolved = false;

        function resolve(ticket) {
            if (resolved) return;
            resolved = true;

            window.removeEventListener("message", onMessage);
            clearInterval(interval);

            try { popup.close(); } catch (e) { }

            sendAuthTicket(ticket);
        }

        function onMessage(event) {
            if (event.origin !== expectedOrigin) return;

            var data = event.data;
            if (!data || !data.ticket) return;

            resolve(data.ticket);
        }

        window.addEventListener("message", onMessage, false);

        var interval = setInterval(function () {
            if (popup.closed) resolve("");
        }, 500);
    }
});
