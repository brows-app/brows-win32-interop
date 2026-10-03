using System.Runtime.InteropServices;

namespace Brows.Win32.PlatformInvoke;

internal static class HRESULTExtension {
    public static HRESULT FromWin32(uint error) {
        if (error == 0 || error >= 0x80000000) {
            return (HRESULT)error;
        }
        return (HRESULT)(0x80070000 | (error & 0x0000FFFF));
    }

    public static void ThrowOnError(this HRESULT hresult) {
        var hr = (uint)hresult;
        switch (hr) {
            case 0:
                break;
            default:
                Marshal.ThrowExceptionForHR(unchecked((int)hresult));
                break;
        }
    }
}
