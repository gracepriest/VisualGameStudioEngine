using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace BasicLang.Debugger
{
    public enum ClrBreakpointStatus { Pending, Bound, Verified, Invalid }

    public class ClrBreakpointEntry
    {
        public int Id;
        public string FilePath;
        public int RequestedLine;
        public int ActualLine;
        public ClrBreakpointStatus Status;
        public string Condition;
        public string HitCondition;
        public string LogMessage;
        public int HitCount;

        // ---------------------------------------------------------------
        // CLR breakpoint storage.
        //
        // A single source line can map to sequence points in SEVERAL methods:
        // e.g. a line containing a lambda has IL both in the enclosing method
        // and in the compiler-generated closure method (<>c__DisplayClass...),
        // and async methods split lines across the kickoff method and the
        // state-machine MoveNext. One DAP breakpoint may therefore be bound
        // to multiple ICorDebugFunctionBreakpoints.
        //
        // The ClrBreakpoint property keeps the adapter's original single-value
        // contract: the getter returns the most recently bound pointer, and
        // assigning it multiple times ACCUMULATES pointers instead of
        // overwriting. Assigning null (done by the adapter after it has
        // deactivated + released the pointer it obtained from the getter)
        // deactivates and releases all remaining pointers.
        // ---------------------------------------------------------------

        private readonly List<object> _clrBreakpoints = new();

        /// <summary>All bound CLR breakpoints (IntPtr to ICorDebugFunctionBreakpoint).</summary>
        public IReadOnlyList<object> ClrBreakpoints => _clrBreakpoints;

        public object ClrBreakpoint
        {
            get => _clrBreakpoints.Count > 0 ? _clrBreakpoints[_clrBreakpoints.Count - 1] : null;
            set
            {
                if (value == null)
                {
                    // The adapter deactivates + releases the pointer it obtained
                    // from the getter (the LAST one) before assigning null, so
                    // clean up all pointers except that last one here.
                    for (int i = 0; i < _clrBreakpoints.Count - 1; i++)
                        DeactivateAndRelease(_clrBreakpoints[i]);
                    _clrBreakpoints.Clear();
                }
                else if (!_clrBreakpoints.Contains(value))
                {
                    _clrBreakpoints.Add(value);
                }
            }
        }

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate int ActivateBreakpointDelegate(IntPtr self, int bActive);

        private static void DeactivateAndRelease(object stored)
        {
            if (stored is not IntPtr bpPtr || bpPtr == IntPtr.Zero)
                return;
            try
            {
                // ICorDebugBreakpoint::Activate — vtable slot 3 (IUnknown 0-2 + Activate 0)
                var vtable = Marshal.ReadIntPtr(bpPtr);
                var activateSlot = Marshal.ReadIntPtr(vtable, 3 * IntPtr.Size);
                var activate = Marshal.GetDelegateForFunctionPointer<ActivateBreakpointDelegate>(activateSlot);
                activate(bpPtr, 0); // deactivate
            }
            catch { /* best effort */ }
            try { Marshal.Release(bpPtr); } catch { }
        }
    }

    public class ClrBreakpointManager
    {
        private readonly Dictionary<int, ClrBreakpointEntry> _breakpoints = new();
        private int _nextId = 1;

        public int AddPendingBreakpoint(string filePath, int line,
            string condition = null, string hitCondition = null, string logMessage = null)
        {
            var id = _nextId++;
            _breakpoints[id] = new ClrBreakpointEntry
            {
                Id = id,
                FilePath = filePath,
                RequestedLine = line,
                ActualLine = line,
                Status = ClrBreakpointStatus.Pending,
                Condition = condition,
                HitCondition = hitCondition,
                LogMessage = logMessage
            };
            return id;
        }

        public ClrBreakpointEntry GetBreakpoint(int id) =>
            _breakpoints.TryGetValue(id, out var bp) ? bp : null;

        public void MarkBound(int id, int actualLine, object clrBreakpoint = null)
        {
            if (_breakpoints.TryGetValue(id, out var bp))
            {
                // Keep Verified status if a previous binding already verified this entry;
                // additional bindings (other methods on the same line) must not regress it.
                if (bp.Status != ClrBreakpointStatus.Verified)
                    bp.Status = ClrBreakpointStatus.Bound;
                bp.ActualLine = actualLine;
                if (clrBreakpoint != null)
                    bp.ClrBreakpoint = clrBreakpoint; // accumulates (see ClrBreakpointEntry)
            }
        }

        public void MarkVerified(int id)
        {
            if (_breakpoints.TryGetValue(id, out var bp))
                bp.Status = ClrBreakpointStatus.Verified;
        }

        public void MarkInvalid(int id)
        {
            // Do not invalidate an entry that already has at least one successful
            // binding — with multi-method binding, a failed bind attempt for one
            // candidate method must not regress an entry bound via another method.
            if (_breakpoints.TryGetValue(id, out var bp) &&
                bp.Status == ClrBreakpointStatus.Pending)
            {
                bp.Status = ClrBreakpointStatus.Invalid;
            }
        }

        public IReadOnlyList<ClrBreakpointEntry> GetPendingForFile(string filePath)
        {
            return _breakpoints.Values
                .Where(bp => bp.Status == ClrBreakpointStatus.Pending &&
                    string.Equals(bp.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        public IReadOnlyList<ClrBreakpointEntry> GetAllPending()
        {
            return _breakpoints.Values
                .Where(bp => bp.Status == ClrBreakpointStatus.Pending)
                .ToList();
        }

        public void ClearFile(string filePath)
        {
            var toRemove = _breakpoints.Values
                .Where(bp => string.Equals(bp.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
                .Select(bp => bp.Id)
                .ToList();
            foreach (var id in toRemove)
                _breakpoints.Remove(id);
        }

        public ClrBreakpointEntry FindByFileAndLine(string filePath, int line)
        {
            return _breakpoints.Values.FirstOrDefault(bp =>
                (bp.Status == ClrBreakpointStatus.Bound || bp.Status == ClrBreakpointStatus.Verified) &&
                bp.ActualLine == line &&
                string.Equals(bp.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Get ALL breakpoints for a file regardless of status (for deactivation before clearing)
        /// </summary>
        public IReadOnlyList<ClrBreakpointEntry> GetAllForFile(string filePath)
        {
            return _breakpoints.Values
                .Where(bp => string.Equals(bp.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        public void ClearAll() => _breakpoints.Clear();
    }
}
