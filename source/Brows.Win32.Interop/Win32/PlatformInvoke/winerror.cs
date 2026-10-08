using System.Diagnostics.CodeAnalysis;

namespace Brows.Win32.PlatformInvoke;

[SuppressMessage("Style", "IDE1006:Naming Styles", Justification = "N/A")]
internal static class winerror {
    public const uint ERROR_SUCCESS = 0;
    public const uint ERROR_SHARING_VIOLATION = 32;
    public const uint ERROR_FILE_EXISTS = 80;
    public const uint ERROR_ALREADY_EXISTS = 183;
    public const uint ERROR_CANCELLED = 1223;
}
