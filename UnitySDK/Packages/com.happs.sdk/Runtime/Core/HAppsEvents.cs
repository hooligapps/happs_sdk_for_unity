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

        public static void Invoke<T1, T2>(Action<T1, T2> handlers, T1 first, T2 second)
        {
            if (handlers == null) return;
            foreach (Action<T1, T2> handler in handlers.GetInvocationList())
                try { handler(first, second); }
                catch (Exception ex) { HAppsLog.Error("Event subscriber failed: " + ex.GetType().Name); }
        }
    }
}
