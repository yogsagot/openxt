using System.Runtime.InteropServices;
using Assimp;

namespace OpenXt.AssetImport;

/// <summary>
/// AssimpNet's last release is from 2019 and its Linux loader P/Invokes <c>dlopen</c> out of
/// <c>libdl.so</c>. glibc 2.34 folded libdl into libc, so that DllImport fails on any current
/// distro and every Assimp call throws before it reaches the real library.
///
/// This redirects those imports to libc, which still exports the symbols. It is the one piece of
/// glue keeping an unmaintained package usable; if AssimpNet becomes a burden, the replacements
/// are Silk.NET.Assimp (same C library, maintained bindings) or SharpGLTF (pure managed, glTF only).
/// </summary>
internal static class NativeLoaderShim
{
    private static bool _installed;

    public static void Install()
    {
        if (_installed || !OperatingSystem.IsLinux())
            return;

        NativeLibrary.SetDllImportResolver(typeof(AssimpContext).Assembly, Resolve);
        _installed = true;
    }

    private static IntPtr Resolve(string libraryName, System.Reflection.Assembly assembly, DllImportSearchPath? path)
    {
        if (libraryName is not ("libdl.so" or "libdl"))
            return IntPtr.Zero;

        // glibc first, then musl.
        foreach (string candidate in (string[])["libc.so.6", "libdl.so.2", "libc.so"])
        {
            if (NativeLibrary.TryLoad(candidate, out IntPtr handle))
                return handle;
        }

        return IntPtr.Zero;
    }
}
