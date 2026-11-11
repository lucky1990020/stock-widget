namespace StockWidgetNative;

internal static class Program
{
    [MTAThread]
    private static void Main(string[] args)
    {
        Log.Write("Starting. args=" + string.Join(" ", args));

        bool comServer = args.Any(
            a => string.Equals(a, "-RegisterProcessAsComServer", StringComparison.OrdinalIgnoreCase));

        if (!comServer)
        {
            Log.Write("Not launched as a widget COM server; exiting.");
            return;
        }

        try
        {
            WinRT.ComWrappersSupport.InitializeComWrappers();

            var factory = new WidgetProviderFactory<WidgetProvider>();
            ClassObject.Register(typeof(WidgetProvider).GUID, factory, out uint cookie);

            try
            {
                Log.Write("Widget provider registered.");
                using var empty = WidgetProvider.GetEmptyWidgetListEvent();
                empty.WaitOne();
                Log.Write("All widgets removed; exiting.");
            }
            finally
            {
                int result = ClassObject.Revoke(cookie);
                if (result < 0)
                {
                    Log.Write($"CoRevokeClassObject failed: 0x{result:X8}");
                }
                GC.KeepAlive(factory);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Provider startup failed", ex);
        }
    }
}
