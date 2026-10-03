using System;
using System.Linq;
using System.Reflection;

namespace Brows.Win32;

internal static class Win32WindowHelper {
    /*
     * Uses reflection so that this assembly does not depend on WPF.
     */
    private static IntPtr WpfWindow() {
        var appType = LoadedType("PresentationFramework", "System.Windows.Application");
        if (appType is null) {
            return IntPtr.Zero;
        }
        var app = appType.GetProperty("Current", BindingFlags.Public | BindingFlags.Static)
            ?.GetValue(null);
        if (app is null) {
            return IntPtr.Zero;
        }
        var dispatcher = appType.GetProperty("Dispatcher", BindingFlags.Public | BindingFlags.Instance)
            ?.GetValue(app);
        if (dispatcher is null) {
            return IntPtr.Zero;
        }
        var helperType = appType.Assembly.GetType("System.Windows.Interop.WindowInteropHelper", throwOnError: false);
        if (helperType is null) {
            return IntPtr.Zero;
        }
        var invoke = dispatcher.GetType().GetMethod("Invoke", [typeof(Delegate), typeof(object[])]);
        if (invoke is null) {
            return IntPtr.Zero;
        }
        var getHandle = new Func<IntPtr>(() => {
            var main = appType.GetProperty("MainWindow", BindingFlags.Public | BindingFlags.Instance)
                ?.GetValue(app);
            if (main is null) {
                return IntPtr.Zero;
            }
            var helper = Activator.CreateInstance(helperType, main);
            var handle = helperType.GetProperty("Handle", BindingFlags.Public | BindingFlags.Instance)
                ?.GetValue(helper);
            return handle is IntPtr h ? h : IntPtr.Zero;
        });
        var result = invoke.Invoke(dispatcher, [getHandle, Array.Empty<object>()]);
        return result is IntPtr ptr ? ptr : IntPtr.Zero;
    }

    /*
     * Uses reflection so that this assembly does not depend on Windows Forms.
     */
    private static IntPtr FormsWindow() {
        var formType = LoadedType("System.Windows.Forms", "System.Windows.Forms.Form");
        if (formType is null) {
            return IntPtr.Zero;
        }
        var form = formType.GetProperty("ActiveForm", BindingFlags.Public | BindingFlags.Static)
            ?.GetValue(null);
        if (form is null) {
            var appType = formType.Assembly.GetType("System.Windows.Forms.Application", throwOnError: false);
            var openForms = appType?.GetProperty("OpenForms", BindingFlags.Public | BindingFlags.Static)
                ?.GetValue(null) as System.Collections.IEnumerable;
            form = openForms?.Cast<object>().FirstOrDefault();
        }
        if (form is null) {
            return IntPtr.Zero;
        }
        var getHandle = new Func<IntPtr>(() => {
            var handle = formType.GetProperty("Handle", BindingFlags.Public | BindingFlags.Instance)
                ?.GetValue(form);
            return handle is IntPtr h ? h : IntPtr.Zero;
        });
        var invokeRequired = formType.GetProperty("InvokeRequired", BindingFlags.Public | BindingFlags.Instance)
            ?.GetValue(form) is true;
        if (invokeRequired) {
            var invoke = formType.GetMethod("Invoke", [typeof(Delegate)]);
            var result = invoke?.Invoke(form, [getHandle]);
            return result is IntPtr ptr ? ptr : IntPtr.Zero;
        }
        return getHandle();
    }

    private static Type LoadedType(string assemblyName, string typeName) {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) {
            if (string.Equals(assembly.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase)) {
                var type = assembly.GetType(typeName, throwOnError: false);
                if (type is not null) {
                    return type;
                }
            }
        }
        return null;
    }

    public static IntPtr GetWindow() {
        var window = WpfWindow();
        if (window == IntPtr.Zero) {
            window = FormsWindow();
        }
        return window;
    }
}
