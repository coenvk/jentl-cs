using System.Runtime.InteropServices;
using System.Text;

namespace Jentl.Core.Input;

/// <summary>
/// The system clipboard on the desktop, through SDL2, which MonoGame's desktop build already loads (<c>SDL_SetClipboardText</c>).
/// Where there is no SDL (the browser build, or a library missing) nothing is copied and <see cref="TrySetText(string)"/>
/// says so, so a game can show the text instead.
/// </summary>
public static class Clipboard
{
    private static readonly Lazy<SetText?> Sdl = new(FindSdl);

    /// <summary>What <c>SDL_SetClipboardText</c> is: a UTF-8 text ended by a null in, 0 out on success.</summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int SetText(IntPtr text);

    /// <summary>Puts <paramref name="text"/> on the clipboard; false when there is no clipboard or SDL could not set it.</summary>
    public static bool TrySetText(string text) => TrySetText(text, Sdl.Value);

    /// <summary><see cref="TrySetText(string)"/> through <paramref name="set"/> (none: no clipboard).</summary>
    internal static bool TrySetText(string text, SetText? set)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (set is null)
        {
            return false;
        }

        var bytes = Encoding.UTF8.GetBytes(text + "\0");
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            return set(handle.AddrOfPinnedObject()) == 0;
        }
        finally
        {
            handle.Free();
        }
    }

    /// <summary>
    /// Where SDL may be, in order: by its name (as the system finds libraries), beside the app (a published app), and in
    /// the app's <c>runtimes/&lt;rid&gt;/native</c> folder (a build).
    /// </summary>
    internal static IReadOnlyList<string> Candidates(string baseDirectory, string runtime, string library) =>
        [library, Path.Combine(baseDirectory, library), Path.Combine(baseDirectory, "runtimes", runtime, "native", library)];

    private static SetText? FindSdl()
    {
        if (OperatingSystem.IsBrowser())
        {
            return null;
        }

        var (runtime, library) = OperatingSystem.IsWindows() ? ("win-x64", "SDL2.dll")
            : OperatingSystem.IsMacOS() ? ("osx", "libSDL2-2.0.0.dylib")
            : ("linux-x64", "libSDL2-2.0.so.0");
        foreach (var candidate in Candidates(AppContext.BaseDirectory, runtime, library))
        {
            if (!NativeLibrary.TryLoad(candidate, out var handle))
            {
                continue;
            }

            if (NativeLibrary.TryGetExport(handle, "SDL_SetClipboardText", out var export))
            {
                return Marshal.GetDelegateForFunctionPointer<SetText>(export);
            }

            NativeLibrary.Free(handle);
        }

        return null;
    }
}
