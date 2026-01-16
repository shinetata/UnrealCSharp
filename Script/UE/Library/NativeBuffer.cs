using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Script.Library;

public unsafe sealed class NativeBuffer<T> : IDisposable where T : unmanaged
{
    private void* data;
    private bool disposed;

    public int Length { get; private set; }
    public int Capacity { get; private set; }

    public uint Version { get; private set; }

    public nint Ptr
    {
        get
        {
            ThrowIfDisposed();
            return (nint)data;
        }
    }

    public NativeBuffer(int length, bool clear = true)
    {
        if (length < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        Allocate(capacity: length, length: length, clear: clear);
    }

    public Span<T> AsSpan()
    {
        ThrowIfDisposed();
        return new Span<T>(data, Length);
    }

    public Span<T> AsSpan(int start, int length)
    {
        return AsSpan().Slice(start, length);
    }

    public void Resize(int newLength, bool clearNew = true)
    {
        ThrowIfDisposed();

        if (newLength < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(newLength));
        }

        if (newLength > Capacity)
        {
            EnsureCapacity(newLength);
        }

        if (clearNew && newLength > Length)
        {
            AsSpan(Length, newLength - Length).Clear();
        }

        Length = newLength;
    }

    public void EnsureCapacity(int minCapacity)
    {
        ThrowIfDisposed();

        if (minCapacity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minCapacity));
        }

        if (minCapacity <= Capacity)
        {
            return;
        }

        var newCapacity = Capacity == 0 ? 4 : Capacity;
        while (newCapacity < minCapacity)
        {
            newCapacity *= 2;
        }

        var newBytes = BytesFor(newCapacity);

        data = data == null ? NativeMemory.Alloc(newBytes) : NativeMemory.Realloc(data, newBytes);
        Version++;
        Debug.Assert(data != null);

        Capacity = newCapacity;
    }

    public override string ToString()
    {
        if (disposed)
        {
            return $"{nameof(NativeBuffer<T>)}(disposed)";
        }

        return $"{nameof(NativeBuffer<T>)}(T={typeof(T).Name}, Len={Length}, Cap={Capacity}, Ptr=0x{Ptr.ToString("X")}, Ver={Version})";
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        if (data != null)
        {
            NativeMemory.Free(data);
            data = null;
        }

        Length = 0;
        Capacity = 0;
        Version++;
    }

    private static nuint BytesFor(int elementCount)
    {
        checked
        {
            return (nuint)(sizeof(T) * elementCount);
        }
    }

    private void Allocate(int capacity, int length, bool clear)
    {
        if (capacity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        if (length < 0 || length > capacity)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        var bytes = BytesFor(capacity);
        data = bytes == 0 ? null : NativeMemory.Alloc(bytes);
        Version++;

        Capacity = capacity;
        Length = length;

        if (clear && data != null)
        {
            new Span<byte>(data, (int)bytes).Clear();
        }
    }

    private void ThrowIfDisposed()
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(NativeBuffer<T>));
        }
    }
}

