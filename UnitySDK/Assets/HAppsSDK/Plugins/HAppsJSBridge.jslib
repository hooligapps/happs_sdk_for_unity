mergeInto(LibraryManager.library, {

    _sendMessage: function (typePtr, messagePtr) {
        var jsType = UTF8ToString(typePtr);
        var jsMessage = UTF8ToString(messagePtr);

        console.log("[HApps][JS] _sendMessage → Unity",
            "type:", jsType,
            "message:", jsMessage
        );

        if (!window.unityInstance) {
            console.error("[HApps][JS] unityInstance not found");
            return;
        }

        window.unityInstance.SendMessage(
            "HAppsJSBridge",
            "OnHooligappsMessage",
            jsMessage
        );
    },

    _openAuthPopup: function (urlPtr) {

        const url = UTF8ToString(urlPtr);
        const expectedOrigin = new URL(url).origin;

        const width = 480;
        const height = 770;

        // ---- центрирование ----
        const dualScreenLeft = window.screenLeft !== undefined
            ? window.screenLeft
            : window.screenX;

        const dualScreenTop = window.screenTop !== undefined
            ? window.screenTop
            : window.screenY;

        const windowWidth = window.innerWidth
            || document.documentElement.clientWidth
            || screen.width;

        const windowHeight = window.innerHeight
            || document.documentElement.clientHeight
            || screen.height;

        const left = dualScreenLeft + (windowWidth - width) / 2;
        const top = dualScreenTop + (windowHeight - height) / 2;
        // ------------------------

        console.log("[HApps][JS] Opening auth popup:",
            "url:", url,
            "expectedOrigin:", expectedOrigin,
            "left:", left,
            "top:", top
        );

        const popup = window.open(
            url,
            "oidc_popup",
            `width=${width},height=${height},left=${left},top=${top},resizable=yes,scrollbars=yes`
        );

        console.log("[HApps][JS] popup object:", popup);

        if (!popup) {
            console.warn("[HApps][JS] Popup blocked by browser");

            window.unityInstance.SendMessage(
                "HAppsJSBridge",
                "OnHooligappsMessage",
                JSON.stringify({ type: "authTicket", authTicket: "" })
            );
            return;
        }

        let resolved = false;

        function resolve(ticket) {

            if (resolved) {
                console.warn("[HApps][JS] resolve() called twice — ignored");
                return;
            }

            resolved = true;

            console.log("[HApps][JS] Resolving auth popup. Ticket:",
                ticket ? "[RECEIVED]" : "[EMPTY]"
            );

            window.removeEventListener("message", onMessage);
            clearInterval(interval);

            try {
                popup.close();
                console.log("[HApps][JS] Popup closed");
            } catch (e) {
                console.warn("[HApps][JS] Failed to close popup:", e);
            }

            window.unityInstance.SendMessage(
                "HAppsJSBridge",
                "OnHooligappsMessage",
                JSON.stringify({
                    type: "authTicket",
                    authTicket: ticket || ""
                })
            );
        }

        function onMessage(event) {

            console.log("[HApps][JS] postMessage received:",
                "origin:", event.origin,
                "data:", event.data
            );

            if (event.origin !== expectedOrigin) {
                console.warn("[HApps][JS] Ignored message from unexpected origin:",
                    event.origin
                );
                return;
            }

            const data = event.data;

            if (!data || !data.ticket) {
                console.warn("[HApps][JS] Message has no ticket");
                return;
            }

            console.log("[HApps][JS] Auth ticket received");

            resolve(data.ticket);
        }

        window.addEventListener("message", onMessage, false);

        console.log("[HApps][JS] Listening for postMessage...");

        const interval = setInterval(function () {

            if (popup.closed) {
                console.log("[HApps][JS] Popup manually closed by user");
                resolve("");
            }

        }, 500);
    }
});