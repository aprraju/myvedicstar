using System.Runtime.InteropServices;

namespace MyVedicStar.Astronomy.Jpl.Provider;

/// <summary>
/// Raw P/Invoke surface for the official NAIF CSPICE toolkit. The native library is supplied by the container image
/// and the DE440/DE441 kernels are mounted read-only; nothing here downloads at run time (D1).
/// </summary>
/// <remarks>
/// Only the calls the engine needs are declared. Each declaration names the exact CSPICE signature it binds to, so a
/// toolkit version change that alters a signature fails loudly at review rather than silently rebinding.
/// </remarks>
internal static partial class SpiceNative
{
    internal const string LibraryName = "cspice";

    /// <summary>Loads a kernel file. CSPICE: <c>void furnsh_c(ConstSpiceChar *file)</c>.</summary>
    [LibraryImport(LibraryName)]
    internal static partial void furnsh_c([MarshalAs(UnmanagedType.LPUTF8Str)] string file);

    /// <summary>Unloads a kernel. CSPICE: <c>void unload_c(ConstSpiceChar *file)</c>.</summary>
    [LibraryImport(LibraryName)]
    internal static partial void unload_c([MarshalAs(UnmanagedType.LPUTF8Str)] string file);

    /// <summary>Body state relative to an observer. CSPICE:
    /// <c>void spkezr_c(ConstSpiceChar *target, SpiceDouble et, ConstSpiceChar *frame,
    /// ConstSpiceChar *abcorr, ConstSpiceChar *observer, SpiceDouble state[6], SpiceDouble *lt)</c>.</summary>
    [LibraryImport(LibraryName)]
    internal static partial void spkezr_c(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string target,
        double et,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string frame,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string aberrationCorrection,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string observer,
        [Out] double[] state,
        out double lightTime);
}

/// <summary>
/// Describes how the SPICE runtime is provided. Recorded in provenance so a result can always be traced to a toolkit
/// version and a kernel set (FR-08, AC-4).
/// </summary>
public sealed record SpiceConfiguration(string ToolkitVersion, string ToolkitSource, IReadOnlyList<string> Kernels)
{
    /// <summary>The kernel set this epic ships: DE440 for modern dates, DE441 for the extended span. Paths are
    /// mount points inside the image, never remote URLs.</summary>
    public static SpiceConfiguration Default { get; } = new(
        ToolkitVersion: "CSPICE-N0067",
        ToolkitSource: "NAIF (JPL), https://naif.jpl.nasa.gov/naif/toolkit.html - public domain",
        Kernels: ["/var/spice/de440.bsp", "/var/spice/naif0012.tls", "/var/spice/pck00011.tpc"]);
}

/// <summary>Raised when the native SPICE library or a kernel is unavailable. A dependency failure, never a silent
/// fallback to a different calculation route.</summary>
public sealed class SpiceUnavailableException(string message, Exception? inner = null)
    : Exception(message, inner);
