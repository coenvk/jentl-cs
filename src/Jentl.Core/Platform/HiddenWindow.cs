using System.Runtime.InteropServices;
using System.Text;
using Jentl.Core.Input;

namespace Jentl.Core.Platform;

/// <summary>
/// Keeps a desktop game's window out of sight, for screenshots and checks run while someone works at the screen: the
/// app stays out of the Dock and doesn't take focus (<see cref="Prepare"/>, before the game is made), and the window
/// is borderless, far off screen, and hidden after its first frame (<see cref="JentlGame.Hidden"/>). Through SDL2,
/// which MonoGame's desktop build runs on; where there is none (the browser) it does nothing.
/// </summary>
public static class HiddenWindow
{
    private static readonly Lazy<IntPtr> Library = new(FindSdl);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SetHint(IntPtr name, IntPtr value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void WindowCall(IntPtr window);

    /// <summary>
    /// Sets SDL's hints for a background app; call before the game is made (SDL starts with it). False when there is
    /// no SDL to tell.
    /// </summary>
    public static bool Prepare() =>
        Export<SetHint>("SDL_SetHint") is { } set && Hint(set, "SDL_MAC_BACKGROUND_APP", "1");

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SetPosition(IntPtr window, int x, int y);

    /// <summary>Moves a window by its SDL handle far off every screen; false when there is no SDL.</summary>
    internal static bool MoveAway(IntPtr window)
    {
        if (window == IntPtr.Zero || Export<SetPosition>("SDL_SetWindowPosition") is not { } move)
        {
            return false;
        }

        move(window, -30000, -30000);
        return true;
    }

    /// <summary>Hides a window by its SDL handle; false when there is no SDL.</summary>
    internal static bool Hide(IntPtr window)
    {
        if (window == IntPtr.Zero || Export<WindowCall>("SDL_HideWindow") is not { } hide)
        {
            return false;
        }

        hide(window);
        return true;
    }

    private static bool Hint(SetHint set, string name, string value)
    {
        var (n, v) = (GCHandle.Alloc(Encoding.UTF8.GetBytes(name + "\0"), GCHandleType.Pinned), GCHandle.Alloc(Encoding.UTF8.GetBytes(value + "\0"), GCHandleType.Pinned));
        try
        {
            return set(n.AddrOfPinnedObject(), v.AddrOfPinnedObject()) != 0;
        }
        finally
        {
            n.Free();
            v.Free();
        }
    }

    private static T? Export<T>(string name)
        where T : Delegate =>
        Library.Value != IntPtr.Zero && NativeLibrary.TryGetExport(Library.Value, name, out var export)
            ? Marshal.GetDelegateForFunctionPointer<T>(export)
            : null;

    /// <summary>The SDL MonoGame loads: the app's own copy first, so hints reach the library the game runs on.</summary>
    private static IntPtr FindSdl()
    {
        if (OperatingSystem.IsBrowser())
        {
            return IntPtr.Zero;
        }

        var (runtime, library) = OperatingSystem.IsWindows() ? ("win-x64", "SDL2.dll")
            : OperatingSystem.IsMacOS() ? ("osx", "libSDL2-2.0.0.dylib")
            : ("linux-x64", "libSDL2-2.0.so.0");
        foreach (var candidate in Clipboard.Candidates(AppContext.BaseDirectory, runtime, library).Reverse())
        {
            if (NativeLibrary.TryLoad(candidate, out var handle))
            {
                return handle;
            }
        }

        return IntPtr.Zero;
    }
}
