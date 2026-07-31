using System.Runtime.InteropServices;

namespace Prometheus.Updater;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            return args.Length > 0 && string.Equals(args[0], "apply", StringComparison.OrdinalIgnoreCase)
                ? Bootstrapper.ApplyAsync(GetOption(args, "--plan")).GetAwaiter().GetResult()
                : Bootstrapper.LaunchCurrent();
        }
        catch (Exception exception)
        {
            BootstrapperLog.Write(exception.ToString());
            NativeMethods.MessageBox(IntPtr.Zero,
                "Prometheus could not be started or updated. See the updater log for details.\n\n"
                + exception.Message,
                "Prometheus", 0x10);
            return 1;
        }
    }

    private static string GetOption(string[] args, string name)
    {
        for (var index = 1; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        throw new ArgumentException($"Missing required option {name}.");
    }
}

internal static partial class NativeMethods
{
    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int MessageBox(IntPtr window, string text, string caption, uint type);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateHardLinkW",
        StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CreateHardLink(string fileName, string existingFileName,
        IntPtr securityAttributes);
}
