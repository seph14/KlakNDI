using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;

namespace Klak.Ndi {

//
// Frame readback entry class
//
// Stores information about single-frame readback.
//
// We need this class because:
// - Async GPU readback requires a NativeArray as a destination.
// - A readback request only carries a data pointer; We have to store other
//   information (dimensions, metadata, etc.) elsewhere.
//
// This class is reusable; You can call Allocate after Deallocate.
//
sealed class ReadbackEntry
{
    #region Private members

    NativeArray<byte> _image;
    IntPtr _metadata;
    int _width, _height;
    bool _alpha, _rgb;

    ~ReadbackEntry()
    {
        if (_image.IsCreated)
            Debug.LogWarning("ReadbackEntry leakage was detected.");
    }

    #endregion

    #region Public accessors

    public int Width => _width;
    public int Stride => _width * ((_alpha && _rgb) ? 4 : (_rgb ? 3 : 2));
    public int Height => _height;

    public IntPtr MetadataPointer => _metadata;
    public unsafe IntPtr ImagePointer
      => (IntPtr)NativeArrayUnsafeUtility
           .GetUnsafeBufferPointerWithoutChecks(_image);
    // Note: We should get the pointer without checks because we use it as an
    // identifier -- We don't care whether the content is ready or not.

    public Interop.FourCC FourCC
        => _rgb ? (_alpha ? Interop.FourCC.RGBA : Interop.FourCC.RGBX) :
                  (_alpha ? Interop.FourCC.UYVA : Interop.FourCC.UYVY);
    #endregion

    #region Resource allocation/deallocation

    public void Allocate(int width, int height, bool alpha, bool rgb, string metadata)
    {
        // Image buffer
        _image = new NativeArray<byte>
          (Util.FrameDataSize(width, height, alpha, rgb),
           Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        
        // Metadata string on heap
        if (string.IsNullOrEmpty(metadata))
            _metadata = IntPtr.Zero;
        else
            _metadata = Marshal.StringToHGlobalAnsi(metadata);

        // Frame settings
        (_width, _height, _alpha, _rgb) = (width, height, alpha, rgb);
        //Debug.Log("size: " + _image.Length + " - " + (Stride * height) + " - " + (4 * width * height));
    }

    public void Deallocate()
    {
        if (_image.IsCreated)
        {
            _image.Dispose();
            _image = default(NativeArray<byte>);
        }

        if (_metadata != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_metadata);
            _metadata = IntPtr.Zero;
        }

        (_width, _height, _alpha) = (0, 0, false);
    }

        #endregion

    #region Readback request initiators
    public void RequestReadback
      (Texture source, Action<AsyncGPUReadbackRequest> callback)
      => AsyncGPUReadback.RequestIntoNativeArray(ref _image, source, 0, callback);
        
    public void RequestReadback
      (ComputeBuffer source, Action<AsyncGPUReadbackRequest> callback)
      => AsyncGPUReadback.RequestIntoNativeArray(ref _image, source, callback);

    public void RequestReadback
      (CommandBuffer command, ComputeBuffer source, Action<AsyncGPUReadbackRequest> callback)
      => command.RequestAsyncReadbackIntoNativeArray(ref _image, source, callback);

    #endregion
}

//
// Frame readback pool class
//
// Stores ongoing readback entries (hot) and recycled entries (cold).
//
// There is a tricky part: The "Marked" entry holds an entry that has been
// completed but still under processing by the NDI async sender. It's expected
// to be freed in the next frame by calling FreeMarked.
//
sealed class ReadbackPool : IDisposable
{
    #region Private members

    List<ReadbackEntry> _hot = new List<ReadbackEntry>();
    Stack<ReadbackEntry> _cold = new Stack<ReadbackEntry>();
    ReadbackEntry _marked;

    #endregion

    #region IDisposable implementation

    public void Dispose()
    {
        foreach (var e in _hot ) e.Deallocate();
        foreach (var e in _cold) e.Deallocate();
        _hot .Clear();
        _cold.Clear();
    }

    #endregion

    #region Pool operations

    public ReadbackEntry
    NewEntry(int width, int height, bool alpha, string metadata)
    {
        var entry = _cold.Count > 0 ? _cold.Pop() : new ReadbackEntry();
        entry.Allocate(width, height, alpha, false, metadata);
        _hot.Add(entry);
        return entry;
    }

    public ReadbackEntry
    NewEntry(int width, int height, bool alpha, bool rgbChannel, string metadata)
    {
        var entry = _cold.Count > 0 ? _cold.Pop() : new ReadbackEntry();
        entry.Allocate(width, height, alpha, rgbChannel, metadata);
        _hot.Add(entry);
        return entry;
    }

    public void Free(ReadbackEntry entry)
    {
        entry.Deallocate();
        _hot.Remove(entry);
        _cold.Push(entry);
    }

    public unsafe ReadbackEntry FindEntry(in NativeArray<byte> buffer)
    {
        var p = (IntPtr)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(buffer);
        foreach (var entry in _hot) if (entry.ImagePointer == p) return entry;
        return null;
    }

    public void Mark(ReadbackEntry entry)
    {
        Debug.Assert(_marked == null, "Marked twice.");
        _marked = entry;
    }

    public void FreeMarkedEntry()
    {
        if (_marked == null) return;
        Free(_marked);
        _marked = null;
    }

    #endregion
}

} // namespace Klak.Ndi
