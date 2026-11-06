using System;
using System.Text;
using System.Threading.Tasks;

namespace Rune;

/// <summary>A key for an `impl Hash` parameter. Each case hashes exactly as its Rust value does,
/// so a string, a number and a byte array never need turning into each other first.</summary>
public readonly struct HashKey
{
    internal readonly uint Kind;       // numbered as `hash_key` in native/rune-capi/src/lib.rs numbers them
    internal readonly ulong Number;
    internal readonly byte[]? Bytes;

    HashKey(uint kind, ulong number, byte[]? bytes)
    {
        Kind = kind;
        Number = number;
        Bytes = bytes;
    }

    public static implicit operator HashKey(string value) => new(0, 0, Encoding.UTF8.GetBytes(value));
    public static implicit operator HashKey(int value) => new(1, (ulong)(long)value, null);
    public static implicit operator HashKey(long value) => new(1, (ulong)value, null);
    public static implicit operator HashKey(uint value) => new(2, value, null);
    public static implicit operator HashKey(ulong value) => new(2, value, null);
    public static implicit operator HashKey(byte[] value) => new(3, 0, value);
}

public readonly unsafe partial struct FileDialogs
{
    /// <summary>Shows an open dialog. The result is null if the user cancels, and a failure faults the task.
    /// The dialog waits on its own thread, so this is safe from the window's thread.</summary>
    public Task<string?> OpenAsync(FileDialogOptions options)
    {
        var answer = new Answer();
        Ext.DialogsOpen(this, options, answer.Set);
        return answer.Task;
    }

    /// <inheritdoc cref="OpenAsync"/>
    public Task<string?> SaveAsync(FileDialogOptions options)
    {
        var answer = new Answer();
        Ext.DialogsSave(this, options, answer.Set);
        return answer.Task;
    }

    /// <inheritdoc cref="OpenAsync"/>
    public Task<string?> OpenFolderAsync(FileDialogOptions options)
    {
        var answer = new Answer();
        Ext.DialogsOpenFolder(this, options, answer.Set);
        return answer.Task;
    }

    sealed class Answer
    {
        // The answer lands on the dialog's thread, which shouldn't run the caller's continuation.
        readonly TaskCompletionSource<string?> _source = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<string?> Task => _source.Task;

        public void Set(string? path, string? error)
        {
            if (error is null)
            {
                _source.SetResult(path);
            }
            else
            {
                _source.SetException(new RuneException(error));
            }
        }
    }
}

public readonly unsafe partial struct AppSpec
{
    /// <summary>Sets every scene in one call. Rust keeps the list for good, so each call leaks one copy of it.</summary>
    public void SetScenes<S>(params ReadOnlySpan<(string Name, Action<S> Run)> scenes) where S : class
    {
        var names = new string[scenes.Length];
        var runs = new Action<S>[scenes.Length];
        for (int i = 0; i < scenes.Length; i++)
        {
            (names[i], runs[i]) = scenes[i];
        }
        Ext.SpecSetScenes<S>(this, names, runs);
    }
}

public readonly unsafe partial struct WgpuTextureFormat
{
    /// <summary>The one variant with data, so it's a method where the others are properties.</summary>
    public static WgpuTextureFormat Astc(WgpuAstcBlock block, WgpuAstcChannel channel) => Ext.TextureFormatAstc(block, channel);
}
