using System;
using UnityEngine;

namespace HAppsSDK
{
    public sealed class HAppsMobileException : Exception
    {
        public long StatusCode { get; }
        public string Code { get; }
        public string RequestId { get; }
        internal string ResponseBody { get; }
        public bool IsRetryable => StatusCode <= 0 || StatusCode == 408 || StatusCode == 429 || StatusCode >= 500;

        internal HAppsMobileException(long statusCode, string body, string requestId)
            : base($"Mobile request failed (HTTP {statusCode}).")
        {
            StatusCode = statusCode;
            RequestId = requestId;
            ResponseBody = body ?? string.Empty;
            try
            {
                var data = JsonUtility.FromJson<ErrorBody>(body);
                Code = !string.IsNullOrEmpty(data?.code) ? data.code : data?.error;
            }
            catch (ArgumentException) { }
        }

        [Serializable]
        private sealed class ErrorBody
        {
            public string code;
            public string error;
        }
    }
}
