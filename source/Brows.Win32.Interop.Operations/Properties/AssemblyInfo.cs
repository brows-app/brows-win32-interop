[assembly: ComVisible(false)]
[assembly: InternalsVisibleTo("Brows.Win32.Interop.Composition")]
[assembly: InternalsVisibleTo("Brows.Win32.Interop.Operations.Tests")]

#if NETFRAMEWORK
#pragma warning disable IDE0161 // Convert to file-scoped namespace
#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace System.Runtime.CompilerServices {
#pragma warning restore IDE0130 // Namespace does not match folder structure
#pragma warning restore IDE0161 // Convert to file-scoped namespace
    [ComponentModel.EditorBrowsable(ComponentModel.EditorBrowsableState.Never)]
    internal static class IsExternalInit {
    }
}
#endif
