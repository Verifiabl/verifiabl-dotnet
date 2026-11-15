using System.Runtime.InteropServices;

namespace Verifiabl.Verifier.Extraction.Internal.Wasmtime;

/// <summary>Makes Wasmtime's native library loadable before the first call into it.</summary>
/// <remarks>
/// .NET finds the library through the Wasmtime package's <c>runtimes/</c>
/// folder. .NET Framework does not, so this package's build targets copy it to
/// <c>wasmtime/x64/</c> and <c>wasmtime/arm64/</c> in the output folder, and
/// it is loaded from there by full path. Windows then resolves
/// <see cref="WasmtimeNative.Library"/> to the module already loaded. It is
/// loaded from nowhere else: the default DLL search includes the working
/// directory and <c>PATH</c>, where another <c>wasmtime.dll</c> could be planted.
/// </remarks>
internal static class WasmtimeLibrary
{
#if NETFRAMEWORK
    private const uint LoadWithAlteredSearchPath = 0x00000008;

    private static readonly object Gate = new();
    private static bool loaded;

    internal static void EnsureLoaded()
    {
        lock (Gate)
        {
            if (!loaded)
            {
                LoadForNetFramework();
                loaded = true;
            }
        }
    }

    private static void LoadForNetFramework()
    {
        string architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => throw new InvalidOperationException(
                $"Wasmtime has no native library for {RuntimeInformation.ProcessArchitecture} processes. Run the application as a 64-bit process."),
        };

        foreach (string directory in SearchDirectories())
        {
            string path = Path.Combine(directory, "wasmtime", architecture, "wasmtime.dll");
            if (File.Exists(path))
            {
                if (LoadLibraryEx(path, IntPtr.Zero, LoadWithAlteredSearchPath) == IntPtr.Zero)
                {
                    throw new InvalidOperationException(
                        $"Wasmtime's native library at {path} could not be loaded (error {Marshal.GetLastWin32Error()}).");
                }

                return;
            }
        }

        throw new InvalidOperationException(
            $"Wasmtime's native library was not found at wasmtime\\{architecture}\\wasmtime.dll in the application folder. "
            + "Reference Verifiabl.Verifier.Extraction with PackageReference and deploy that folder with the application.");
    }

    /// <summary>The application folder and, for ASP.NET, its private bin folders.</summary>
    private static IEnumerable<string> SearchDirectories()
    {
        AppDomain domain = AppDomain.CurrentDomain;
        yield return domain.BaseDirectory;
        if (!string.IsNullOrEmpty(domain.RelativeSearchPath))
        {
            foreach (string relative in domain.RelativeSearchPath.Split(';'))
            {
                yield return Path.Combine(domain.BaseDirectory, relative);
            }
        }
    }

    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
#else
    internal static void EnsureLoaded()
    {
    }
#endif
}
