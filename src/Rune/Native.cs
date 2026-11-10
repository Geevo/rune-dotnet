using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

[assembly: DisableRuntimeMarshalling]

namespace Rune;

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct Utf8Ref
{
    public byte* Ptr;
    public nuint Len;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NativeCallbacks
{
    public delegate* unmanaged<nint> Init;
    public delegate* unmanaged<nint, nint, uint> View;
    public delegate* unmanaged<uint, nint, void> Scene;
    public delegate* unmanaged<nint, nint, void> Setup;
    public delegate* unmanaged<nint, double, nint, bool> Poll;
    public delegate* unmanaged<nint, nint, nint, bool> OnKey;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NativeSpec
{
    public Utf8Ref Title;
    public float Width;
    public float Height;
    public Utf8Ref* Scenes;
    public nuint SceneCount;
    public bool HasMinSize;
    public float MinWidth;
    public float MinHeight;
    public Utf8Ref* Fonts;
    public nuint FontCount;
    public Utf8Ref Sans;
    public Utf8Ref Mono;
}

/// <summary>The runtime core; everything else is in Generated.cs.</summary>
internal static unsafe partial class Native
{
    private const string Lib = "rune_capi";

    [DllImport(Lib)] public static extern nint rune_build_spec(NativeCallbacks* callbacks, NativeSpec* spec);
    [DllImport(Lib)] public static extern void rune_run(nint spec);
    [DllImport(Lib)] public static extern void rune_set_release(delegate* unmanaged<nint, void> release);
    [DllImport(Lib)] public static extern void rune_set_equals(delegate* unmanaged<nint, nint, byte> equals);
    [DllImport(Lib)] public static extern uint rune_scene_limit();
    [DllImport(Lib)] public static extern uint rune_selftest_unwind();
    [DllImport(Lib)] public static extern void* rune_pending();
    [DllImport(Lib)] public static extern uint rune_take_failure(Utf8Ref* message);
    [DllImport(Lib)] public static extern bool rune_fail_callback();
    [DllImport(Lib)] public static extern void rune_waker_wake(nint waker);
    [DllImport(Lib)] public static extern nint rune_waker_clone(nint waker);
    [DllImport(Lib)] public static extern void rune_waker_free(nint waker);
}
