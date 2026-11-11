using System.Runtime.InteropServices;
using Microsoft.Windows.Widgets.Providers;
using WinRT;

namespace StockWidgetNative;

internal static class ComGuids
{
    public const string IClassFactory = "00000001-0000-0000-C000-000000000046";
    public const string IUnknown = "00000000-0000-0000-C000-000000000046";
}

[ComImport]
[ComVisible(false)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid(ComGuids.IClassFactory)]
internal interface IClassFactory
{
    [PreserveSig]
    int CreateInstance(IntPtr pUnkOuter, ref Guid riid, out IntPtr ppvObject);

    [PreserveSig]
    int LockServer(bool fLock);
}

internal static class ClassObject
{
    [DllImport("ole32.dll")]
    private static extern int CoRegisterClassObject(
        [MarshalAs(UnmanagedType.LPStruct)] Guid rclsid,
        [MarshalAs(UnmanagedType.IUnknown)] object pUnk,
        uint dwClsContext,
        uint flags,
        out uint lpdwRegister);

    [DllImport("ole32.dll")]
    private static extern int CoRevokeClassObject(uint dwRegister);

    public static void Register(Guid clsid, object pUnk, out uint cookie)
    {
        int result = CoRegisterClassObject(clsid, pUnk, 0x4, 0x1, out cookie);
        if (result != 0)
        {
            Marshal.ThrowExceptionForHR(result);
        }
    }

    public static int Revoke(uint cookie)
    {
        return CoRevokeClassObject(cookie);
    }
}

internal sealed class WidgetProviderFactory<T> : IClassFactory
    where T : IWidgetProvider, new()
{
    private const int ClassENoAggregation = -2147221232;
    private const int ENoInterface = -2147467262;

    public int CreateInstance(IntPtr pUnkOuter, ref Guid riid, out IntPtr ppvObject)
    {
        ppvObject = IntPtr.Zero;

        if (pUnkOuter != IntPtr.Zero)
        {
            return ClassENoAggregation;
        }

        if (riid == typeof(T).GUID || riid == Guid.Parse(ComGuids.IUnknown))
        {
            try
            {
                ppvObject = MarshalInspectable<IWidgetProvider>.FromManaged(new T());
                return 0;
            }
            catch (Exception ex)
            {
                Log.Error("WidgetProviderFactory.CreateInstance failed", ex);
                return Marshal.GetHRForException(ex);
            }
        }

        return ENoInterface;
    }

    int IClassFactory.LockServer(bool fLock)
    {
        return 0;
    }
}
