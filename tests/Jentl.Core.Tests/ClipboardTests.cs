using System.Runtime.InteropServices;
using Jentl.Core.Input;

namespace Jentl.Core.Tests;

/// <summary>The clipboard's text handed to SDL as UTF-8; the real SDL is not called here, so the tests leave the clipboard alone.</summary>
public class ClipboardTests
{
    [Fact]
    public void TheText_IsHandedOverAsUtf8_EndedByANull()
    {
        string? seen = null;

        var copied = Clipboard.TrySetText("Token ✓ 42", text =>
        {
            seen = Marshal.PtrToStringUTF8(text);
            return 0;
        });

        copied.Should().BeTrue();
        seen.Should().Be("Token ✓ 42");
    }

    [Fact]
    public void AFailingSdl_OrNone_IsNoCopy()
    {
        Clipboard.TrySetText("x", _ => -1).Should().BeFalse("SDL says it failed with a negative number");
        Clipboard.TrySetText("x", null).Should().BeFalse("without SDL (the browser) there is no clipboard");
    }

    [Fact]
    public void TheLibraryNames_AreLookedForBesideTheAppAndInItsRuntimesFolder()
    {
        var names = Clipboard.Candidates("/app/", "osx", "libSDL2-2.0.0.dylib");

        names.Should().Equal("libSDL2-2.0.0.dylib", "/app/libSDL2-2.0.0.dylib", "/app/runtimes/osx/native/libSDL2-2.0.0.dylib");
    }
}
