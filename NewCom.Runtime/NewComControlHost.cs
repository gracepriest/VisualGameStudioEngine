using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace NewCom.Runtime;

/// <summary>
/// A Windows Forms control that hosts a native NewCOM component implementing
/// the INewComControl contract. Phase 3: lifetime + bounds/visibility marshalling.
/// Window parenting follows once components expose GetHwnd/SetParent.
/// </summary>
public class NewComControlHost : Control
{
    private NewComLoader.ComponentHandle? _component;
    private readonly string _componentName;

    // Control-contract delegates (laid out after the INewUnknown base slots).
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr GetHwndDelegate(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SetParentDelegate(IntPtr self, IntPtr parent);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SetBoundsDelegate(IntPtr self, int x, int y, int w, int h);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SetVisibleDelegate(IntPtr self, int visible);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SetHostDelegate(IntPtr self, IntPtr host);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void PaintDelegate(IntPtr self, IntPtr hdc);

    private GetHwndDelegate? _getHwnd;
    private SetParentDelegate? _setParent;
    private SetBoundsDelegate? _setBounds;
    private SetVisibleDelegate? _setVisible;
    private SetHostDelegate? _setHost;
    private PaintDelegate? _paint;

    private OnEventDelegate? _onEventDelegate;
    private IntPtr _hostVtblPtr;
    private IntPtr _hostPtr;

    public NewComControlHost(string componentName)
    {
        _componentName = componentName;
        SetStyle(ControlStyles.Opaque | ControlStyles.ResizeRedraw, true);
    }

    public string ComponentName => _componentName;
    public IntPtr NativeHandle => _component?.Native ?? IntPtr.Zero;

    /// <summary>Raised when the native component fires an event (generic; typed events are Phase 3b).</summary>
    public event EventHandler<NewComEventArgs>? EventOccurred;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void OnEventDelegate(IntPtr self, IntPtr eventNamePtr, IntPtr argsJsonPtr);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _component ??= NewComLoader.Load(_componentName);
        if (_component == null) return;

        // Read the vtable pointer (first field of INewUnknown/INewComControl).
        var vtblPtr = Marshal.ReadIntPtr(_component.Native);
        if (vtblPtr == IntPtr.Zero) return;

        // VTable layout: QI, AddRef, Release, GetHwnd, SetParent, SetBounds, SetVisible, SetHost, Paint
        // (9 slots). Slots 0-2 are base; 3-8 are control contract.
        var slots = new IntPtr[9];
        for (int i = 0; i < slots.Length; i++)
            slots[i] = Marshal.ReadIntPtr(vtblPtr, i * IntPtr.Size);

        _getHwnd = Marshal.GetDelegateForFunctionPointer<GetHwndDelegate>(slots[3]);
        _setParent = Marshal.GetDelegateForFunctionPointer<SetParentDelegate>(slots[4]);
        _setBounds = Marshal.GetDelegateForFunctionPointer<SetBoundsDelegate>(slots[5]);
        _setVisible = Marshal.GetDelegateForFunctionPointer<SetVisibleDelegate>(slots[6]);
        _setHost = Marshal.GetDelegateForFunctionPointer<SetHostDelegate>(slots[7]);
        _paint = Marshal.GetDelegateForFunctionPointer<PaintDelegate>(slots[8]);

        // Register the host callback so the native component can fire events.
        try
        {
            _onEventDelegate = (self, namePtr, argsPtr) =>
            {
                var name = namePtr == IntPtr.Zero ? "" : (Marshal.PtrToStringAnsi(namePtr) ?? "");
                var args = argsPtr == IntPtr.Zero ? "" : (Marshal.PtrToStringAnsi(argsPtr) ?? "");
                EventOccurred?.Invoke(this, new NewComEventArgs(name, args));
            };
            var fnPtr = Marshal.GetFunctionPointerForDelegate(_onEventDelegate);
            _hostVtblPtr = Marshal.AllocHGlobal(IntPtr.Size);
            Marshal.WriteIntPtr(_hostVtblPtr, fnPtr);
            _hostPtr = Marshal.AllocHGlobal(IntPtr.Size);
            Marshal.WriteIntPtr(_hostPtr, _hostVtblPtr);
            _setHost?.Invoke(_component.Native, _hostPtr);
        }
        catch { }

        try { _setParent?.Invoke(_component.Native, Handle); } catch { }
        SyncBounds();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        SyncBounds();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        try { _setVisible?.Invoke(_component!.Native, Visible ? 1 : 0); } catch { }
    }

    private void SyncBounds()
    {
        if (_component == null || _setBounds == null) return;
        try { _setBounds(_component.Native, Left, Top, Width, Height); } catch { }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _component?.Dispose();
            _component = null;
            if (_hostPtr != IntPtr.Zero) { Marshal.FreeHGlobal(_hostPtr); _hostPtr = IntPtr.Zero; }
            if (_hostVtblPtr != IntPtr.Zero) { Marshal.FreeHGlobal(_hostVtblPtr); _hostVtblPtr = IntPtr.Zero; }
            _onEventDelegate = null;
        }
        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        // Forward to the native component if it implements Paint.
        if (_component != null && _paint != null)
        {
            try
            {
                var hdc = e.Graphics.GetHdc();
                try { _paint(_component.Native, hdc); }
                finally { e.Graphics.ReleaseHdc(hdc); }
                return;
            }
            catch { }
        }
        // Placeholder rendering until the native control paints itself.
        using var b = new System.Drawing.SolidBrush(System.Drawing.SystemColors.Control);
        e.Graphics.FillRectangle(b, ClientRectangle);
        using var f = new System.Drawing.Font("Segoe UI", 9f);
        using var br = new System.Drawing.SolidBrush(System.Drawing.SystemColors.ControlText);
        e.Graphics.DrawString($"NewCOM: {_componentName}", f, br, 4, 4);
    }
}
