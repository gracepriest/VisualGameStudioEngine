using System;
using System.Collections.Generic;
using System.Linq;

namespace BasicLang.Compiler
{
    /// <summary>
    /// Manages module dependencies and compilation order
    /// </summary>
    public class DependencyGraph
    {
        private readonly Dictionary<string, HashSet<string>> _dependencies;
        private readonly Dictionary<string, HashSet<string>> _dependents;

        public DependencyGraph()
        {
            _dependencies = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            _dependents = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Add a module to the graph
        /// </summary>
        public void AddModule(string moduleId)
        {
            if (!_dependencies.ContainsKey(moduleId))
            {
                _dependencies[moduleId] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
            if (!_dependents.ContainsKey(moduleId))
            {
                _dependents[moduleId] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Add a dependency: 'from' depends on 'to'
        /// </summary>
        public void AddDependency(string from, string to)
        {
            AddModule(from);
            AddModule(to);

            _dependencies[from].Add(to);
            _dependents[to].Add(from);
        }

        /// <summary>
        /// Remove a dependency
        /// </summary>
        public void RemoveDependency(string from, string to)
        {
            if (_dependencies.TryGetValue(from, out var deps))
            {
                deps.Remove(to);
            }
            if (_dependents.TryGetValue(to, out var dependents))
            {
                dependents.Remove(from);
            }
        }

        /// <summary>
        /// Get all dependencies of a module
        /// </summary>
        public IEnumerable<string> GetDependencies(string moduleId)
        {
            if (_dependencies.TryGetValue(moduleId, out var deps))
            {
                return deps;
            }
            return Enumerable.Empty<string>();
        }

        /// <summary>
        /// Get all modules that depend on a given module
        /// </summary>
        public IEnumerable<string> GetDependents(string moduleId)
        {
            if (_dependents.TryGetValue(moduleId, out var deps))
            {
                return deps;
            }
            return Enumerable.Empty<string>();
        }

        /// <summary>
        /// Get transitive dependencies (all dependencies, recursively)
        /// </summary>
        public HashSet<string> GetTransitiveDependencies(string moduleId)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var stack = new Stack<string>();

            stack.Push(moduleId);

            while (stack.Count > 0)
            {
                var current = stack.Pop();
                if (!visited.Add(current))
                    continue;

                if (current != moduleId)
                    result.Add(current);

                foreach (var dep in GetDependencies(current))
                {
                    if (!visited.Contains(dep))
                    {
                        stack.Push(dep);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Detect cycles in the dependency graph
        /// Returns the cycle path if found, null otherwise
        /// </summary>
        public List<string> DetectCycle()
        {
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var recursionStack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var path = new List<string>();

            foreach (var module in _dependencies.Keys)
            {
                var cycle = DetectCycleDFS(module, visited, recursionStack, path);
                if (cycle != null)
                    return cycle;
            }

            return null;
        }

        private List<string> DetectCycleDFS(
            string current,
            HashSet<string> visited,
            HashSet<string> recursionStack,
            List<string> path)
        {
            if (recursionStack.Contains(current))
            {
                // Found cycle - build the cycle path
                var cycleStart = path.IndexOf(current);
                var cycle = path.Skip(cycleStart).ToList();
                cycle.Add(current);
                return cycle;
            }

            if (visited.Contains(current))
                return null;

            visited.Add(current);
            recursionStack.Add(current);
            path.Add(current);

            foreach (var dep in GetDependencies(current))
            {
                var cycle = DetectCycleDFS(dep, visited, recursionStack, path);
                if (cycle != null)
                    return cycle;
            }

            path.RemoveAt(path.Count - 1);
            recursionStack.Remove(current);
            return null;
        }

        /// <summary>
        /// Get compilation order using topological sort
        /// Throws if there's a cycle
        /// </summary>
        public List<string> GetCompilationOrder()
        {
            var cycle = DetectCycle();
            if (cycle != null)
            {
                throw new CircularDependencyException(cycle);
            }

            return TopologicalSort();
        }

        /// <summary>
        /// Perform topological sort (Kahn's algorithm)
        /// </summary>
        private List<string> TopologicalSort()
        {
            var result = new List<string>();
            var inDegree = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            // Calculate in-degrees
            foreach (var module in _dependencies.Keys)
            {
                if (!inDegree.ContainsKey(module))
                    inDegree[module] = 0;

                foreach (var dep in _dependencies[module])
                {
                    if (!inDegree.ContainsKey(dep))
                        inDegree[dep] = 0;
                    inDegree[dep]++; // This is inverted - we want dependencies first
                }
            }

            // Actually we need to count dependents, not dependencies
            inDegree.Clear();
            foreach (var module in _dependencies.Keys)
            {
                inDegree[module] = _dependencies[module].Count;
            }

            // Find modules with no dependencies
            var queue = new Queue<string>();
            foreach (var kvp in inDegree)
            {
                if (kvp.Value == 0)
                    queue.Enqueue(kvp.Key);
            }

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                result.Add(current);

                // For each module that depends on current
                foreach (var dependent in GetDependents(current))
                {
                    inDegree[dependent]--;
                    if (inDegree[dependent] == 0)
                    {
                        queue.Enqueue(dependent);
                    }
                }
            }

            // If not all nodes are in result, there's a cycle
            if (result.Count != _dependencies.Count)
            {
                // This shouldn't happen since we checked for cycles already
                var missing = _dependencies.Keys.Except(result).ToList();
                throw new CircularDependencyException(missing);
            }

            return result;
        }

        /// <summary>
        /// Get the compilation order for a specific module and its dependencies
        /// </summary>
        public List<string> GetCompilationOrderFor(string moduleId)
        {
            var needed = GetTransitiveDependencies(moduleId);
            needed.Add(moduleId);

            // Create a subgraph and sort it
            var subgraph = new DependencyGraph();
            foreach (var module in needed)
            {
                subgraph.AddModule(module);
                foreach (var dep in GetDependencies(module))
                {
                    if (needed.Contains(dep))
                    {
                        subgraph.AddDependency(module, dep);
                    }
                }
            }

            return subgraph.GetCompilationOrder();
        }

        /// <summary>
        /// Clear the graph
        /// </summary>
        public void Clear()
        {
            _dependencies.Clear();
            _dependents.Clear();
        }

        /// <summary>
        /// Remove a module and its edges
        /// </summary>
        public void RemoveModule(string moduleId)
        {
            // Remove edges to this module
            if (_dependencies.TryGetValue(moduleId, out var deps))
            {
                foreach (var dep in deps)
                {
                    if (_dependents.TryGetValue(dep, out var dependents))
                    {
                        dependents.Remove(moduleId);
                    }
                }
            }

            // Remove edges from this module
            if (_dependents.TryGetValue(moduleId, out var myDependents))
            {
                foreach (var dependent in myDependents)
                {
                    if (_dependencies.TryGetValue(dependent, out var theirDeps))
                    {
                        theirDeps.Remove(moduleId);
                    }
                }
            }

            _dependencies.Remove(moduleId);
            _dependents.Remove(moduleId);
        }

        /// <summary>
        /// Get graph statistics
        /// </summary>
        public (int nodes, int edges) GetStats()
        {
            int edges = _dependencies.Values.Sum(d => d.Count);
            return (_dependencies.Count, edges);
        }
    }

    /// <summary>
    /// Exception thrown when a circular dependency is detected
    /// </summary>
    public class CircularDependencyException : Exception
    {
        public List<string> Cycle { get; }

        public CircularDependencyException(List<string> cycle)
            : base($"Circular dependency detected: {string.Join(" -> ", cycle)}")
        {
            Cycle = cycle;
        }
    }
}
