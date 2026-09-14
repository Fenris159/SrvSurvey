using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

[assembly: InternalsVisibleTo("PipeWire.NET.Tests")]

// Note: [DisableRuntimeMarshalling] is emitted by ClangSharpPInvokeGenerator into
// generated/DisableRuntimeMarshalling.g.cs (controlled by generate-disable-runtime-marshalling).

namespace PipeWire.NET;

internal static class AssemblyInitializer
{
    // CA2255 advises against [ModuleInitializer] in libraries because it runs eagerly
    // and inflates startup. The trade-off here is acceptable: a single
    // SetDllImportResolver call (microseconds) is the canonical AOT-safe way to register
    // a soname fallback for libpipewire-0.3.so.0 -> libpipewire-0.3.so, and we have no
    // other entry point a consumer must call.
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Initialize()
    {
        // Try versioned soname first - present on production systems without -dev packages.
        // Fall back to the unversioned symlink provided by the -dev package.
        NativeLibrary.SetDllImportResolver(
            typeof(AssemblyInitializer).Assembly,
            static (name, asm, path) =>
            {
                if (name is not "libpipewire-0.3") return 0;
                if (NativeLibrary.TryLoad("libpipewire-0.3.so.0", asm, path, out nint h)) return h;
                if (NativeLibrary.TryLoad("libpipewire-0.3.so",   asm, path, out h))      return h;
                return 0;
            });
    }
}
