using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace HAppsSDK
{
    public sealed class HAppsJSBridge : MonoBehaviour
    {
        public event Action<InitData, UserData, SignatureData> OnInitialized;
        public event Action<UserData> OnProfile;
        public event Action<PaymentData> OnPaymentCreated;
        public event Action<PaymentData> OnPaymentCompleted;
        public event Action<AuthPopupData> OnAuthPopupCompleted;
        public event Action<UserData, SignatureData> OnPortalAuthCompleted;

        public void OnMessage(string json)
        {
            HAppsLog.Log($"JS → {json}");

            if (string.IsNullOrEmpty(json))
            {
                HAppsLog.Warn("Empty JS message");
                return;
            }

            HAppsMessage msg;

            try
            {
                msg = JsonUtility.FromJson<HAppsMessage>(json);
            }
            catch (Exception e)
            {
                HAppsLog.Error($"JSON parse error: {e}");
                return;
            }

            if (msg == null || string.IsNullOrEmpty(msg.type))
            {
                HAppsLog.Warn("Invalid JS message");
                return;
            }

            HAppsLog.Log($"Dispatch: {msg.type}");

            switch (msg.type)
            {
                case "init":
                    OnInitialized?.Invoke(msg.initData, msg.userData, msg.signatureData);
                    break;

                case "profile":
                    OnProfile?.Invoke(msg.userData);
                    break;

                case "payment":
                    OnPaymentCreated?.Invoke(msg.paymentData);
                    break;

                case "payment_complete":
                    OnPaymentCompleted?.Invoke(msg.paymentData);
                    break;

                case "popup_auth_result":
                    OnAuthPopupCompleted?.Invoke(msg.authPopupData);
                    break;

                case "auth_complete":
                    OnPortalAuthCompleted?.Invoke(msg.userData, msg.signatureData);
                    break;

                default:
                    HAppsLog.Warn($"Unknown message type: {msg.type}");
                    break;
            }
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void _sendMessage(string type, string message);
        [DllImport("__Internal")]
        private static extern int _isPortalSite();
        [DllImport("__Internal")]
        private static extern void _redirect(string url);
#else
        private static void _sendMessage(string type, string message) { }
        private static int _isPortalSite() { return 0; }
        private static void _redirect(string url) { }
#endif

        public void SendMessage(string type, string payloadJson)
        {
            HAppsLog.Log($"Unity → JS: {type} {payloadJson}");
            _sendMessage(type, payloadJson);
        }
        
        public void RunNextFrame(Action action)
        {
            StartCoroutine(RunNextFrameRoutine(action));
        }
        
        private System.Collections.IEnumerator RunNextFrameRoutine(Action action)
        {
            yield return null;
            action?.Invoke();
        }
        
        public static bool IsPortalSite()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return _isPortalSite() == 1;
#else
            return false;
#endif
        }
        
        public static void Redirect(string url)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            _redirect(url);
#else
            Application.OpenURL(url);
#endif
        }
    }
}
