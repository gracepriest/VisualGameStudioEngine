using System;
using System.Collections.Generic;
using System.Linq;

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
        public object ClrBreakpoint; // ICorDebugFunctionBreakpoint when bound
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
                bp.Status = ClrBreakpointStatus.Bound;
                bp.ActualLine = actualLine;
                bp.ClrBreakpoint = clrBreakpoint;
            }
        }

        public void MarkVerified(int id)
        {
            if (_breakpoints.TryGetValue(id, out var bp))
                bp.Status = ClrBreakpointStatus.Verified;
        }

        public void MarkInvalid(int id)
        {
            if (_breakpoints.TryGetValue(id, out var bp))
                bp.Status = ClrBreakpointStatus.Invalid;
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

        public void ClearAll() => _breakpoints.Clear();
    }
}
