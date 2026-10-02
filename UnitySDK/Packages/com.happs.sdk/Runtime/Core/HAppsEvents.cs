using System;

namespace HAppsSDK
{
    internal static class HAppsEvents
    {
        public static void Invoke<T>(Action<T> handlers, T value)
        {
            if (handlers == null) return;
            foreach (Action<T> handler in handlers.GetInvocationList())
                try { handler(value); }
                catch (Exception ex) { HAppsLog.Error("Event subscriber failed: " + ex.GetType().Name); }
        }
    }
}
