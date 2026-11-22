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

    /// <summary>Toolkit version string. CSPICE: <c>ConstSpiceChar *tkvrsn_c(ConstSpiceChar *item)</c>.</summary>
    /// <remarks>
    /// <para>Used as the availability probe: it reads no state, so it cannot abort the way an invalid unload does.</para>
    /// <para><b>Returned as IntPtr, not string.</b> CSPICE returns a pointer to its own static buffer. Marshalling that
    /// as <c>string</c> makes the runtime free a pointer it does not own - measured as a hard
    /// <c>free(): invalid pointer</c> crash of the test host, which is the same class of defect as the aborting probe:
    /// a probe that kills the process. The caller reads the buffer and copies it; nothing is freed.</para>
    /// </remarks>
    [LibraryImport(LibraryName)]
    internal static partial IntPtr tkvrsn_c([MarshalAs(UnmanagedType.LPUTF8Str)] string item);

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
