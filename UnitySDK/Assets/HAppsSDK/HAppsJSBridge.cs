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
        public event Action<string> OnAuthTicket;

        public void OnHooligappsMessage(string json)
        {
            Debug.Log($"[HApps] JS → {json}");

            HAppsMessage msg;

            try
            {
                msg = JsonUtility.FromJson<HAppsMessage>(json);
            }
            catch (Exception e)
            {
                Debug.LogError($"[HApps] JSON parse error: {e}");
                return;
            }

            if (msg == null || string.IsNullOrEmpty(msg.type))
                return;

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

                case "paymentComplete":
                    OnPaymentCompleted?.Invoke(msg.paymentData);
                    break;
                
                case "authTicket":
                    OnAuthTicket?.Invoke(msg.authTicket);
                    break;
            }
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void _sendMessage(string type, string message);

        [DllImport("__Internal")]
        private static extern void _openAuthPopup(string url);
#else
        private static void _sendMessage(string type, string message) { }
        private static void _openAuthPopup(string url) { }
#endif

        public void SendMessage(string type, string payloadJson)
        {
            Debug.Log($"HAppsJSBridge.SendMessage {type} {payloadJson}");
            _sendMessage(type, payloadJson);
        }

        public void OpenAuthPopup(string url)
        {
            Debug.Log($"HAppsJSBridge.OpenAuthPopup {url}");
            _openAuthPopup(url);
        }
    }
}