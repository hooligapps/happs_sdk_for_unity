using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace HAppsSDK
{
    public sealed class HAppsJSBridge : MonoBehaviour
    {
        private const int MaxJsonLength = 8192; // 8KB
        private const int MaxUserIdLength = 64;
        private const int MaxUserNameLength = 128;
        private const int MaxSignatureLength = 512;

        private static readonly HashSet<string> AllowedTypes = new HashSet<string>
        {
            "init", "profile", "payment_complete", "auth_ticket", "register_complete", "visibility"
        };

        public event Action<InitData, UserData, SignatureData> OnInitialized;
        public event Action<UserData> OnProfile;
        public event Action<PaymentData> OnPaymentCompleted;
        public event Action<string> OnAuthTicket;
        public event Action<UserData, SignatureData> OnRegisterComplete;
        public event Action<bool> OnVisibility;

        /// <summary>
        /// Called from JavaScript via SendMessage("HAppsManager", "OnHAppsMessage", json).
        /// </summary>
        public void OnHAppsMessage(string json)
        {
            if (string.IsNullOrEmpty(json) || json.Length > MaxJsonLength)
            {
                Debug.LogWarning($"[HApps] Message rejected: invalid length ({json?.Length ?? 0})");
                return;
            }

            Debug.Log($"[HApps] JS → type={TryParseType(json)}");

            HAppsMessage msg;

            try
            {
                msg = JsonUtility.FromJson<HAppsMessage>(json);
            }
            catch (Exception e)
            {
                Debug.LogError($"[HApps] JSON parse error: {e.Message}");
                return;
            }

            if (msg == null || string.IsNullOrEmpty(msg.type))
                return;

            if (!AllowedTypes.Contains(msg.type))
            {
                Debug.LogWarning($"[HApps] Unknown message type: {msg.type}");
                return;
            }

            if (!ValidateFields(msg))
                return;

            switch (msg.type)
            {
                case "init":
                    OnInitialized?.Invoke(msg.initData, msg.userData, msg.signatureData);
                    break;

                case "profile":
                    OnProfile?.Invoke(msg.userData);
                    break;

                case "payment_complete":
                    OnPaymentCompleted?.Invoke(msg.paymentData);
                    break;

                case "auth_ticket":
                    OnAuthTicket?.Invoke(msg.authTicket);
                    break;

                case "register_complete":
                    OnRegisterComplete?.Invoke(msg.userData, msg.signatureData);
                    break;

                case "visibility":
                    var visible = msg.initData?.ready == true;
                    OnVisibility?.Invoke(visible);
                    break;
            }
        }

        private static bool ValidateFields(HAppsMessage msg)
        {
            if (msg.userData != null)
            {
                if (msg.userData.userId != null && msg.userData.userId.Length > MaxUserIdLength)
                {
                    Debug.LogWarning("[HApps] userId exceeds max length");
                    return false;
                }

                if (msg.userData.userName != null && msg.userData.userName.Length > MaxUserNameLength)
                {
                    Debug.LogWarning("[HApps] userName exceeds max length");
                    return false;
                }
            }

            if (msg.signatureData?.signature != null && msg.signatureData.signature.Length > MaxSignatureLength)
            {
                Debug.LogWarning("[HApps] signature exceeds max length");
                return false;
            }

            return true;
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void _sendMessage(string type, string message);
#else
        private static void _sendMessage(string type, string message)
        {
            Debug.Log($"[HApps][Editor] _sendMessage({type})");
        }
#endif

        public void SendToJS(string type, string payloadJson)
        {
            Debug.Log($"[HApps] → JS type={type}");
            _sendMessage(type, payloadJson);
        }

        private static string TryParseType(string json)
        {
            const string key = "\"type\":\"";
            var idx = json.IndexOf(key, StringComparison.Ordinal);
            if (idx < 0) return "?";
            var start = idx + key.Length;
            var end = json.IndexOf('"', start);
            return end > start ? json.Substring(start, end - start) : "?";
        }
    }
}
