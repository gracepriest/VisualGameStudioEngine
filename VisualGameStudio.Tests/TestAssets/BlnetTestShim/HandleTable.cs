using System.Runtime.InteropServices;

namespace BlnetTestShim;

/// <summary>
/// Spec C2: generation-tagged table of GCHandles. Handle = {generation:high32 | index:low32}.
/// Index 0 reserved. Fresh handle refcount = 1. Generation increments when a slot is FREED
/// (refcount hits zero), not per release. Table grows without bound (amortized append).
/// </summary>
public sealed class HandleTable
{
    private struct Slot { public GCHandle Gc; public uint Generation; public int RefCount; public bool Alive; }
    private readonly object _lock = new();
    private readonly List<Slot> _slots = new() { default };  // burn index 0
    private readonly Stack<uint> _free = new();

    public ulong Create(object target)
    {
        lock (_lock)
        {
            uint index;
            if (_free.Count > 0) index = _free.Pop();
            else { _slots.Add(default); index = (uint)(_slots.Count - 1); }
            var s = _slots[(int)index];
            if (s.Generation == 0) s.Generation = 1;
            s.Gc = GCHandle.Alloc(target);
            s.RefCount = 1;
            s.Alive = true;
            _slots[(int)index] = s;
            return ((ulong)s.Generation << 32) | index;
        }
    }

    public BlnetStatus TryGet(ulong handle, out object? target)
    {
        lock (_lock)
        {
            target = null;
            if (!Validate(handle, out var index)) return BlnetStatus.BLNET_E_STALE_HANDLE;
            target = _slots[(int)index].Gc.Target;
            return BlnetStatus.BLNET_OK;
        }
    }

    public BlnetStatus AddRef(ulong handle)
    {
        lock (_lock)
        {
            if (!Validate(handle, out var index)) return BlnetStatus.BLNET_E_STALE_HANDLE;
            var s = _slots[(int)index]; s.RefCount++; _slots[(int)index] = s;
            return BlnetStatus.BLNET_OK;
        }
    }

    public BlnetStatus Release(ulong handle)
    {
        lock (_lock)
        {
            if (!Validate(handle, out var index)) return BlnetStatus.BLNET_E_STALE_HANDLE;
            var s = _slots[(int)index];
            if (--s.RefCount == 0)
            {
                s.Gc.Free();
                s.Alive = false;
                s.Generation++;          // stale detection: old handles now fail Validate
                _slots[(int)index] = s;
                _free.Push(index);
            }
            else _slots[(int)index] = s;
            return BlnetStatus.BLNET_OK;
        }
    }

    public int AliveCount { get { lock (_lock) { return _slots.Count(s => s.Alive); } } }

    private bool Validate(ulong handle, out uint index)
    {
        index = (uint)(handle & 0xFFFFFFFF);
        uint gen = (uint)(handle >> 32);
        if (index == 0 || index >= _slots.Count) return false;
        var s = _slots[(int)index];
        return s.Alive && s.Generation == gen;
    }
}
