using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace Rune;

/// <summary>An element under construction. Valid inside the view that built it; attaching it as a child consumes it.</summary>
public readonly unsafe partial struct El
{
    internal readonly uint Id;
    internal El(uint id) => Id = id;
}

/// <summary>What the generated bindings need from the runtime.</summary>
internal static unsafe class Interop
{
    // The callbacks running on this thread, innermost last. Scope ids are never reused, so a borrow
    // from a callback that has returned can't match a later one.
    [ThreadStatic] static uint[]? s_scopes;
    [ThreadStatic] static int s_depth;
    static int s_lastScope;

    /// <summary>Starts a scope for a callback's borrowed arguments.</summary>
    public static uint Enter()
    {
        var scopes = s_scopes ??= new uint[16];
        if (s_depth == scopes.Length)
        {
            Array.Resize(ref s_scopes, s_depth * 2);
        }
        var id = (uint)Interlocked.Increment(ref s_lastScope);
        s_scopes![s_depth++] = id;
        return id;
    }

    public static void Leave() => s_depth--;

    /// <summary>The scope of a handle into another object, such as a field: it lives as long as that object.</summary>
    public const uint Lent = uint.MaxValue;

    /// <summary>The handle's pointer, if it can still be used. Scope 0 is an object nothing borrowed.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static nint Check(nint p, uint scope)
    {
        if (p == 0 || (scope != 0 && scope != Lent && !Live(scope)))
        {
            Invalid(p);
        }
        return p;
    }

    /// <summary>The pointer of a handle about to be given away. A borrow isn't anyone's to give.</summary>
    public static nint Take(nint p, uint scope)
    {
        Check(p, scope);
        if (scope != 0)
        {
            throw new InvalidOperationException("This Rune object is borrowed from another, so it can't be freed or passed where Rune takes ownership.");
        }
        return p;
    }

    static bool Live(uint scope)
    {
        var scopes = s_scopes;
        for (int i = s_depth - 1; i >= 0; i--)
        {
            if (scopes![i] == scope)
            {
                return true;
            }
        }
        return false;
    }

    static void Invalid(nint p) => throw new InvalidOperationException(p == 0
        ? "This Rune object is a default struct: it was never assigned."
        : "This Rune object was borrowed by a callback that has returned. Read what you need from it inside the callback.");

    public static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s);

    /// <summary>What an `impl Display` parameter gets. Invariant, as Rust formats numbers.</summary>
    public static byte[] Shown(object? value) => Utf8(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "");

    public static string FromUtf8(byte* ptr, nuint len) => Encoding.UTF8.GetString(ptr, (int)len);

    public static string FromUtf8(Utf8Ref s) => FromUtf8(s.Ptr, s.Len);

    /// <summary>Hands a delegate to Rust, which releases it when Rune drops the closure.</summary>
    public static nint Keep(object target) => GCHandle.ToIntPtr(GCHandle.Alloc(target));

    public static object Target(nint handle) => GCHandle.FromIntPtr(handle).Target!;

    const int FnSlots = 64;
    static readonly Dictionary<(int Pool, Delegate Fn), uint> s_slots = [];
    static readonly Dictionary<int, uint> s_used = [];

    /// <summary>The pool slot for a bare Rust `fn`. Slots last for the process, like the functions they stand for, so equal delegates share one.</summary>
    public static uint Slot(int pool, Delegate fn, Func<nint> keep, Action<uint, nint> set)
    {
        lock (s_slots)
        {
            if (s_slots.TryGetValue((pool, fn), out var slot))
            {
                return slot;
            }
            s_used.TryGetValue(pool, out slot);
            if (slot == FnSlots)
            {
                throw new InvalidOperationException($"Rune takes a plain function here, and at most {FnSlots} distinct ones per signature. Pass a method group or a static lambda, not a capturing lambda.");
            }
            s_used[pool] = slot + 1;
            s_slots[(pool, fn)] = slot;
            set(slot, keep());
            return slot;
        }
    }
}

/// <summary>Wakes the app from any thread. Owned: pass it on, or call <see cref="Free"/> when done.</summary>
public readonly struct Waker
{
    readonly nint _p;
    internal nint P => Interop.Check(_p, 0);
    internal nint O => P;
    internal Waker(nint p) => _p = p;

    public void Wake() => Native.rune_waker_wake(P);
    public Waker Clone() => new(Native.rune_waker_clone(P));
    public void Free() => Native.rune_waker_free(P);
}

public sealed class AppSpec<S>(string title, (float W, float H) size, Func<S> init, Func<S, Cx, El> view) where S : class
{
    public string Title { get; } = title;
    public (float W, float H) Size { get; } = size;
    public Func<S> Init { get; } = init;
    public Func<S, Cx, El> View { get; } = view;
    public (string Name, Action<S> Apply)[] Scenes { get; init; } = [];
    /// <summary>The smallest window the user can resize to, in points. Null means Rune's default.</summary>
    public (float W, float H)? MinSize { get; init; }
    /// <summary>Font files replacing the built-in Inter; empty keeps it.</summary>
    public byte[][] Fonts { get; init; } = [];
    /// <summary>The sans and mono family names, as CSS lists.</summary>
    public (string Sans, string Mono) Families { get; init; } = ("Inter", "Inter");
    /// <summary>Called once with a waker other threads use when new data arrives.</summary>
    public Action<S, Waker>? Setup { get; init; }
    /// <summary>Called at the start of every frame with seconds since start; true when state changed.</summary>
    public Func<S, double, Tx, bool>? Poll { get; init; }
    /// <summary>Keys the focused control left unhandled; true consumes the key. The event lasts for the call.</summary>
    public Func<S, KeyEvent, Tx, bool>? OnKey { get; init; }
}

public readonly unsafe partial struct App
{
    private static Func<object> s_init = null!;
    private static Func<object, Cx, El> s_view = null!;
    private static Action<object>[] s_scenes = [];
    private static Action<object, Waker>? s_setup;
    private static Func<object, double, Tx, bool>? s_poll;
    private static Func<object, KeyEvent, Tx, bool>? s_onKey;

    /// <summary>Must run on the process's main thread: macOS and Wayland event loops expect it.</summary>
    public static void Run<S>(AppSpec<S> spec) where S : class => Native.rune_run(Build(spec).P);

    /// <summary>The native spec, for hosts that drive Rune themselves through `App` or `Runtime`.</summary>
    public static AppSpec Build<S>(AppSpec<S> spec) where S : class
    {
        s_init = spec.Init;
        s_view = (s, cx) => spec.View((S)s, cx);
        s_scenes = Array.ConvertAll(spec.Scenes, sc => (Action<object>)(s => sc.Apply((S)s)));
        if (spec.Setup is { } setup)
        {
            s_setup = (s, w) => setup((S)s, w);
        }
        if (spec.Poll is { } poll)
        {
            s_poll = (s, t, tx) => poll((S)s, t, tx);
        }
        if (spec.OnKey is { } onKey)
        {
            s_onKey = (s, ev, tx) => onKey((S)s, ev, tx);
        }

        var pins = new List<GCHandle>();
        Utf8Ref Pin(byte[] bytes)
        {
            var pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            pins.Add(pin);
            return new Utf8Ref { Ptr = (byte*)pin.AddrOfPinnedObject(), Len = (nuint)bytes.Length };
        }

        var scenes = Array.ConvertAll(spec.Scenes, sc => Pin(Encoding.UTF8.GetBytes(sc.Name)));
        var fonts = Array.ConvertAll(spec.Fonts, Pin);
        var callbacks = new NativeCallbacks
        {
            Init = &OnInit,
            View = &OnView,
            Scene = &OnScene,
            Setup = spec.Setup is null ? null : &OnSetup,
            Poll = spec.Poll is null ? null : &OnPoll,
            OnKey = spec.OnKey is null ? null : &OnKeyEvent,
        };
        fixed (Utf8Ref* scenesPtr = scenes, fontsPtr = fonts)
        {
            var native = new NativeSpec
            {
                Title = Pin(Encoding.UTF8.GetBytes(spec.Title)),
                Width = spec.Size.W,
                Height = spec.Size.H,
                Scenes = scenesPtr,
                SceneCount = (nuint)scenes.Length,
                HasMinSize = spec.MinSize.HasValue,
                MinWidth = spec.MinSize.GetValueOrDefault().W,
                MinHeight = spec.MinSize.GetValueOrDefault().H,
                Fonts = fontsPtr,
                FontCount = (nuint)fonts.Length,
                Sans = Pin(Encoding.UTF8.GetBytes(spec.Families.Sans)),
                Mono = Pin(Encoding.UTF8.GetBytes(spec.Families.Mono)),
            };
            nint built = Native.rune_build_spec(&callbacks, &native);
            foreach (var pin in pins)
            {
                pin.Free();
            }
            if (built == 0)
            {
                throw new ArgumentException($"Rune's wrapper supports {Native.rune_scene_limit()} scenes.");
            }
            return new AppSpec(built);
        }
    }

    /// <summary>True when a Rust panic unwinds and is caught in the final link, as Rune's own catch_unwind sites need.</summary>
    public static bool SelfTestUnwind() => Native.rune_selftest_unwind() == 1;

    internal static object StateOf(nint handle) => GCHandle.FromIntPtr(handle).Target!;

    // An exception can't cross into Rust: report it and stop, as an unhandled exception would.
    internal static void Fatal(Exception e)
    {
        Console.Error.WriteLine(e);
        Environment.FailFast("Unhandled exception in a Rune callback.");
    }

    [UnmanagedCallersOnly]
    private static nint OnInit()
    {
        try
        {
            return GCHandle.ToIntPtr(GCHandle.Alloc(s_init()));
        }
        catch (Exception e)
        {
            Fatal(e);
            return 0;
        }
    }

    [UnmanagedCallersOnly]
    private static uint OnView(nint state, nint cx)
    {
        var sc = Interop.Enter();
        try
        {
            return s_view(StateOf(state), new Cx(cx, sc)).Id;
        }
        catch (Exception e)
        {
            Fatal(e);
            return 0;
        }
        finally
        {
            Interop.Leave();
        }
    }

    [UnmanagedCallersOnly]
    private static void OnScene(uint index, nint state)
    {
        try
        {
            s_scenes[index](StateOf(state));
        }
        catch (Exception e)
        {
            Fatal(e);
        }
    }

    [UnmanagedCallersOnly]
    private static void OnSetup(nint state, nint waker)
    {
        try
        {
            s_setup!(StateOf(state), new Waker(waker));
        }
        catch (Exception e)
        {
            Fatal(e);
        }
    }

    [UnmanagedCallersOnly]
    private static bool OnPoll(nint state, double seconds, nint tx)
    {
        var sc = Interop.Enter();
        try
        {
            return s_poll!(StateOf(state), seconds, new Tx(tx, sc));
        }
        catch (Exception e)
        {
            Fatal(e);
            return false;
        }
        finally
        {
            Interop.Leave();
        }
    }

    [UnmanagedCallersOnly]
    private static bool OnKeyEvent(nint state, nint key, nint tx)
    {
        var sc = Interop.Enter();
        try
        {
            return s_onKey!(StateOf(state), new KeyEvent(key, sc), new Tx(tx, sc));
        }
        catch (Exception e)
        {
            Fatal(e);
            return false;
        }
        finally
        {
            Interop.Leave();
        }
    }

    // CA2255 warns off module initializers in libraries. This one has to run before any Rune call:
    // it hands Rust the C# functions it calls back into.
#pragma warning disable CA2255
    [System.Runtime.CompilerServices.ModuleInitializer]
#pragma warning restore CA2255
    internal static void Load()
    {
        Callbacks.Register();
        Native.rune_set_release(&OnRelease);
        Native.rune_set_equals(&OnEquals);
    }

    [UnmanagedCallersOnly]
    private static byte OnEquals(nint a, nint b) => Equals(Interop.Target(a), Interop.Target(b)) ? (byte)1 : (byte)0;

    [UnmanagedCallersOnly]
    private static void OnRelease(nint handle) => GCHandle.FromIntPtr(handle).Free();
}
