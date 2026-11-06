using System;
using System.Runtime.CompilerServices;
using System.Text;

namespace Rune;

/// <summary>An error a Rune call returned, as Rust formats it.</summary>
public sealed class RuneException(string message) : Exception(message);

/// <summary>Encodes values too rich for a C signature. native/rune-capi/src/wire.rs is the twin.</summary>
internal sealed unsafe class WireWriter
{
    [ThreadStatic] static byte[]? s_returned;
    byte[] _buf = new byte[64];
    int _len;

    Span<byte> Grow(int n)
    {
        if (_len + n > _buf.Length)
        {
            Array.Resize(ref _buf, Math.Max(_buf.Length * 2, _len + n));
        }
        var s = _buf.AsSpan(_len, n);
        _len += n;
        return s;
    }

    public void Raw<T>(T v) where T : unmanaged => Unsafe.WriteUnaligned(ref Grow(sizeof(T))[0], v);
    public void Len(int n) => Raw((ulong)n);
    public void Tag(bool on) => Raw((byte)(on ? 1 : 0));

    public void Str(string s)
    {
        int n = Encoding.UTF8.GetByteCount(s);
        Len(n);
        if (n != 0)
        {
            Encoding.UTF8.GetBytes(s, Grow(n));
        }
    }

    public void Prims<T>(T[] v) where T : unmanaged
    {
        Len(v.Length);
        if (v.Length != 0)
        {
            global::System.Runtime.InteropServices.MemoryMarshal.AsBytes(v.AsSpan()).CopyTo(Grow(v.Length * sizeof(T)));
        }
    }

    public byte[] ToArray() => _buf.AsSpan(0, _len).ToArray();

    /// <summary>For a callback's result: valid until the next one on this thread.</summary>
    public Utf8Ref Keep()
    {
        if (s_returned is null || s_returned.Length < _len)
        {
            s_returned = GC.AllocateUninitializedArray<byte>(Math.Max(_len, 256), pinned: true);
        }
        _buf.AsSpan(0, _len).CopyTo(s_returned);
        return new Utf8Ref { Ptr = (byte*)Unsafe.AsPointer(ref s_returned[0]), Len = (nuint)_len };
    }
}

internal sealed unsafe class WireReader(byte* at, byte* end)
{
    byte* _at = at;

    /// <summary>The scope of the handles this value holds.</summary>
    public uint Scope { get; init; }

    public static T Read<T>(Utf8Ref s, Func<WireReader, T> read) => read(new WireReader(s.Ptr, s.Ptr + s.Len));
    public static T Read<T>(byte* p, nuint n, Func<WireReader, T> read) => read(new WireReader(p, p + n));
    public static T Read<T>(Utf8Ref s, Func<WireReader, T> read, uint scope) => read(new WireReader(s.Ptr, s.Ptr + s.Len) { Scope = scope });
    public static T Read<T>(byte* p, nuint n, Func<WireReader, T> read, uint scope) => read(new WireReader(p, p + n) { Scope = scope });
    public static void Check(Utf8Ref s) => new WireReader(s.Ptr, s.Ptr + s.Len).Check();

    byte* Advance(int n)
    {
        if (end - _at < n)
        {
            throw new InvalidOperationException("wire value ended early");
        }
        var p = _at;
        _at += n;
        return p;
    }

    public T Raw<T>() where T : unmanaged => Unsafe.ReadUnaligned<T>(Advance(sizeof(T)));
    public int Len() => checked((int)Raw<ulong>());
    public bool Tag() => Raw<byte>() != 0;

    public string Str()
    {
        int n = Len();
        return Interop.FromUtf8(Advance(n), (nuint)n);
    }

    public T[] Prims<T>() where T : unmanaged
    {
        int n = Len();
        var v = new T[n];
        if (n != 0)
        {
            new ReadOnlySpan<byte>(Advance(n * sizeof(T)), n * sizeof(T)).CopyTo(global::System.Runtime.InteropServices.MemoryMarshal.AsBytes(v.AsSpan()));
        }
        return v;
    }

    public T[] List<T>(Func<WireReader, T> item)
    {
        var v = new T[Len()];
        for (int i = 0; i < v.Length; i++)
        {
            v[i] = item(this);
        }
        return v;
    }

    public void Check()
    {
        if (!Tag())
        {
            throw new RuneException(Str());
        }
    }

    public T Checked<T>(Func<WireReader, T> ok)
    {
        Check();
        return ok(this);
    }
}
