using System;
using System.Collections.Generic;
using System.Linq;

namespace BasicLang.Compiler.IR
{
    /// <summary>
    /// Control Flow Graph builder and analyzer
    /// </summary>
    public class ControlFlowGraph
    {
        public IRFunction Function { get; }
        public List<BasicBlock> Blocks => Function.Blocks;
        public BasicBlock EntryBlock => Function.EntryBlock;
        
        // Analysis results
        /// <summary>
        /// Natural loops found by <see cref="IdentifyLoops"/>, one entry per back edge.
        ///
        /// ⛔ This covers ONLY loops expressed as branches in the CFG. BasicLang has a SECOND,
        /// structurally different loop representation: `For Each` lowers to a structured
        /// <c>IRForEach</c> node and `Try` to <c>IRTryCatch</c>, whose body/continuation blocks
        /// <see cref="Build"/> wires as FORWARD edges only. Those constructs contribute no cycle,
        /// hence no back edge, hence NO ENTRY IN THIS LIST. Reporting zero loops for a function
        /// whose only loop is a `For Each` is the CORRECT answer here, not a gap to be patched by
        /// widening this analysis.
        ///
        /// Any future loop pass must therefore be correct under the premise that UNSEEN LOOPS
        /// EXIST IN THE FUNCTION, and may NOT treat "this block is in no loop" as "this block
        /// executes once." A block that is in no natural loop may still sit inside a `For Each`
        /// body and run many times; hoisting into it, or computing a trip count for it, is
        /// unsound. Membership in this list is evidence that a loop exists — absence from it is
        /// not evidence that one does not.
        /// </summary>
        public List<List<BasicBlock>> NaturalLoops { get; private set; }

        public ControlFlowGraph(IRFunction function)
        {
            Function = function;
            NaturalLoops = new List<List<BasicBlock>>();
        }
        
        /// <summary>
        /// Build CFG by analyzing branch instructions
        /// </summary>
        public void Build()
        {
            // Clear existing edges
            foreach (var block in Blocks)
            {
                block.Predecessors.Clear();
                block.Successors.Clear();
            }
            
            // Build edges from terminators
            foreach (var block in Blocks)
            {
                foreach (var successor in SuccessorsOf(block))
                {
                    AddEdge(block, successor);
                }
            }
        }

        /// <summary>
        /// The blocks control can reach directly from <paramref name="block"/>, in the order
        /// <see cref="Build"/> adds them — THE edge rule, extracted from <see cref="Build"/>
        /// unchanged so it has one definition and two consumers. <see cref="Build"/> writes these
        /// into <see cref="BasicBlock.Successors"/>/<see cref="BasicBlock.Predecessors"/>;
        /// <c>IRVerifier</c> (ADR-0004 D2) walks them WITHOUT touching any block's edge lists,
        /// because verification must not change what a backend sees and rebuilding the CFG
        /// would rewrite those lists. May repeat a target (AddEdge de-duplicates); a null target
        /// is yielded as-is, exactly as <see cref="Build"/> handed it to AddEdge before.
        ///
        /// <para>Not an analysis — it lists edges and nothing else; it revives none of the
        /// surface ADR-0003 D5 deleted.</para>
        /// </summary>
        public static IEnumerable<BasicBlock> SuccessorsOf(BasicBlock block)
        {
            var terminator = block.GetTerminator();

            if (terminator is IRBranch branch)
            {
                yield return branch.Target;
            }
            else if (terminator is IRConditionalBranch condBranch)
            {
                yield return condBranch.TrueTarget;
                yield return condBranch.FalseTarget;
            }
            else if (terminator is IRSwitch switchInst)
            {
                yield return switchInst.DefaultTarget;
                foreach (var (_, target) in switchInst.Cases)
                {
                    yield return target;
                }
                // Pattern cases (constant/range/comparison/Or/When from a Select Case)
                // carry their target block by reference just like the integral Cases. The
                // parser routes EVERY case value into PatternCases (Cases stays empty), so
                // WITHOUT these edges the case-body blocks are unreachable from entry and
                // DeadCodeEliminationPass.RemoveUnreachableBlocks() deletes them — silently
                // dropping every Select Case branch (same failure class as the For Each/Try
                // structured edges below).
                foreach (var patternCase in switchInst.PatternCases)
                {
                    yield return patternCase.Target;
                }
            }
            // IRReturn has no successors

            // Structured control-flow instructions (For Each, Try/Catch) carry their
            // body/continuation blocks by reference rather than by branch terminator, so
            // they must be wired into the CFG explicitly. Without these edges the loop
            // body, the post-loop continuation, the try/catch/finally bodies and the
            // post-try continuation are all UNREACHABLE from entry — so
            // DeadCodeEliminationPass.RemoveUnreachableBlocks() deletes them from
            // Function.Blocks. That silently dropped every statement after a For Each/Try
            // (and every temporary produced inside a For Each/Try body) from the emitted
            // code. They can appear anywhere in the block (not only as the terminator),
            // so scan all instructions.
            foreach (var inst in block.Instructions)
            {
                if (inst is IRForEach forEach)
                {
                    if (forEach.BodyBlock != null) yield return forEach.BodyBlock;
                    if (forEach.EndBlock != null) yield return forEach.EndBlock;
                }
                else if (inst is IRTryCatch tryCatch)
                {
                    if (tryCatch.TryBlock != null) yield return tryCatch.TryBlock;
                    foreach (var catchClause in tryCatch.CatchClauses)
                        if (catchClause.Block != null) yield return catchClause.Block;
                    if (tryCatch.FinallyBlock != null) yield return tryCatch.FinallyBlock;
                    if (tryCatch.EndBlock != null) yield return tryCatch.EndBlock;
                }
            }
        }

        private void AddEdge(BasicBlock from, BasicBlock to)
        {
            if (!from.Successors.Contains(to))
                from.Successors.Add(to);
            
            if (!to.Predecessors.Contains(from))
                to.Predecessors.Add(from);
        }
        
        /// <summary>
        /// Compute dominators for all blocks
        /// A block X dominates block Y if every path from entry to Y goes through X
        /// </summary>
        public void ComputeDominators()
        {
            if (Blocks.Count == 0) return;
            
            // Initialize: entry dominates itself, all others dominated by everything
            var allBlocks = new HashSet<BasicBlock>(Blocks);
            
            foreach (var block in Blocks)
            {
                if (block == EntryBlock)
                {
                    block.Dominators.Clear();
                    block.Dominators.Add(block);
                }
                else
                {
                    block.Dominators.Clear();
                    block.Dominators.UnionWith(allBlocks);
                }
            }
            
            // Iterate until fixed point
            bool changed = true;
            while (changed)
            {
                changed = false;
                
                foreach (var block in Blocks)
                {
                    if (block == EntryBlock) continue;
                    
                    // Dom(n) = {n} Ã¢Ë†Âª (Ã¢Ë†Â© Dom(p) for all predecessors p)
                    var newDominators = new HashSet<BasicBlock>(allBlocks);
                    
                    foreach (var pred in block.Predecessors)
                    {
                        newDominators.IntersectWith(pred.Dominators);
                    }
                    
                    newDominators.Add(block);
                    
                    if (!newDominators.SetEquals(block.Dominators))
                    {
                        block.Dominators = newDominators;
                        changed = true;
                    }
                }
            }
            
            ComputeImmediateDominators();
        }
        
        /// <summary>
        /// Compute immediate dominator for each block
        /// IDom(n) is the unique block that strictly dominates n and is dominated by all other dominators of n
        /// </summary>
        private void ComputeImmediateDominators()
        {
            foreach (var block in Blocks)
            {
                if (block == EntryBlock)
                {
                    block.ImmediateDominator = null;
                    continue;
                }
                
                // Find strict dominators (dominators excluding the block itself)
                var strictDoms = new HashSet<BasicBlock>(block.Dominators);
                strictDoms.Remove(block);
                
                // Find the immediate dominator - the one not dominated by any other strict dominator
                BasicBlock idom = null;
                foreach (var dom in strictDoms)
                {
                    bool isDominatedByOther = false;
                    
                    foreach (var otherDom in strictDoms)
                    {
                        if (dom != otherDom && otherDom.Dominators.Contains(dom))
                        {
                            isDominatedByOther = true;
                            break;
                        }
                    }
                    
                    if (!isDominatedByOther)
                    {
                        idom = dom;
                        break;
                    }
                }
                
                block.ImmediateDominator = idom;
            }
        }
        
        /// <summary>
        /// Find back edges in CFG (edges from a node to its dominator)
        /// </summary>
        public List<(BasicBlock From, BasicBlock To)> FindBackEdges()
        {
            var backEdges = new List<(BasicBlock, BasicBlock)>();
            
            foreach (var block in Blocks)
            {
                foreach (var successor in block.Successors)
                {
                    // Back edge tail->head: the HEAD dominates the TAIL, i.e. the head is in
                    // the tail's own dominator set. `b.Dominators` holds the blocks that
                    // dominate b, so the test is block.Dominators.Contains(successor).
                    // Testing successor.Dominators.Contains(block) instead asks "does the tail
                    // dominate the head", which is the defining property of a FORWARD edge --
                    // it selects every edge that is not a back edge and never the real one.
                    // (Fixed identically on this branch, 60b7226 / ADR-0003, and on master,
                    // e063faf: MEASURED there on `For i = 1 To 5 : s = s + i * 3 : Next`,
                    // entry->for0.cond was a "back edge", every "loop" contained entry, and
                    // LoopInvariantCodeMotionPass moved the loop condition into for0.inc —
                    // every For loop printed 0 on C++ under --optimize.)
                    if (block.Dominators.Contains(successor))
                    {
                        backEdges.Add((block, successor));
                    }
                }
            }
            
            return backEdges;
        }
        
        /// <summary>
        /// Identify natural loops in the CFG
        /// </summary>
        public void IdentifyLoops()
        {
            NaturalLoops.Clear();
            var backEdges = FindBackEdges();
            
            foreach (var (tail, head) in backEdges)
            {
                var loop = new HashSet<BasicBlock> { head };
                var workList = new Queue<BasicBlock>();
                workList.Enqueue(tail);
                
                while (workList.Count > 0)
                {
                    var block = workList.Dequeue();
                    
                    if (!loop.Contains(block))
                    {
                        loop.Add(block);
                        
                        foreach (var pred in block.Predecessors)
                        {
                            workList.Enqueue(pred);
                        }
                    }
                }
                
                NaturalLoops.Add(loop.ToList());
            }
        }
        
        /// <summary>
        /// Perform depth-first traversal
        /// </summary>
        public List<BasicBlock> DepthFirstTraversal()
        {
            var visited = new HashSet<BasicBlock>();
            var result = new List<BasicBlock>();
            
            void DFS(BasicBlock block)
            {
                if (visited.Contains(block)) return;
                
                visited.Add(block);
                result.Add(block);
                
                foreach (var successor in block.Successors)
                {
                    DFS(successor);
                }
            }
            
            DFS(EntryBlock);
            return result;
        }
        
        /// <summary>
        /// Perform breadth-first traversal
        /// </summary>
        public List<BasicBlock> BreadthFirstTraversal()
        {
            var visited = new HashSet<BasicBlock>();
            var result = new List<BasicBlock>();
            var queue = new Queue<BasicBlock>();
            
            queue.Enqueue(EntryBlock);
            visited.Add(EntryBlock);
            
            while (queue.Count > 0)
            {
                var block = queue.Dequeue();
                result.Add(block);
                
                foreach (var successor in block.Successors)
                {
                    if (!visited.Contains(successor))
                    {
                        visited.Add(successor);
                        queue.Enqueue(successor);
                    }
                }
            }
            
            return result;
        }
        
        /// <summary>
        /// Get reverse post-order traversal (useful for data flow analysis)
        /// </summary>
        public List<BasicBlock> ReversePostOrder()
        {
            var visited = new HashSet<BasicBlock>();
            var postOrder = new List<BasicBlock>();
            
            void DFS(BasicBlock block)
            {
                if (visited.Contains(block)) return;
                visited.Add(block);
                
                foreach (var successor in block.Successors)
                {
                    DFS(successor);
                }
                
                postOrder.Add(block);
            }
            
            DFS(EntryBlock);
            postOrder.Reverse();
            return postOrder;
        }
        
        /// <summary>
        /// Check if the CFG is reducible (structured control flow)
        ///
        /// <para>⚠ ADR-0003 D4 deleted this as dead; it is back because master's e063faf fixed
        /// its orientation and <c>LoopInvariantCodeMotionTests</c> calls it. Note that with both
        /// orientations now agreeing it cannot return false: <see cref="FindBackEdges"/> admits
        /// an edge only when its head dominates its tail, which is exactly what this re-checks.
        /// A real reducibility test compares DFS retreating edges against dominance back
        /// edges.</para>
        /// </summary>
        public bool IsReducible()
        {
            // A CFG is reducible if all back edges are to loop headers
            var backEdges = FindBackEdges();
            
            foreach (var (tail, head) in backEdges)
            {
                // Check if head dominates tail (making it a proper loop header). Same
                // orientation fix as FindBackEdges: "head dominates tail" is
                // tail.Dominators.Contains(head).
                if (!tail.Dominators.Contains(head))
                {
                    return false;
                }
            }
            
            return true;
        }
        
        /// <summary>
        /// Find unreachable blocks
        /// </summary>
        public List<BasicBlock> FindUnreachableBlocks()
        {
            var reachable = new HashSet<BasicBlock>(DepthFirstTraversal());
            return Blocks.Where(b => !reachable.Contains(b)).ToList();
        }
        
        /// <summary>
        /// Remove unreachable blocks
        /// </summary>
        public int RemoveUnreachableBlocks()
        {
            var unreachable = FindUnreachableBlocks();
            
            foreach (var block in unreachable)
            {
                // Remove from successors' predecessor lists
                foreach (var successor in block.Successors)
                {
                    successor.Predecessors.Remove(block);
                }
                
                Function.Blocks.Remove(block);
            }
            
            return unreachable.Count;
        }
    }
    
    /// <summary>
    /// Data flow analysis framework
    /// </summary>
    public abstract class DataFlowAnalysis<T>
    {
        protected ControlFlowGraph CFG { get; }
        protected Dictionary<BasicBlock, T> In { get; }
        protected Dictionary<BasicBlock, T> Out { get; }
        
        protected DataFlowAnalysis(ControlFlowGraph cfg)
        {
            CFG = cfg;
            In = new Dictionary<BasicBlock, T>();
            Out = new Dictionary<BasicBlock, T>();
        }
        
        protected abstract T InitialValue();
        protected abstract T Transfer(BasicBlock block, T input);
        protected abstract T Meet(IEnumerable<T> values);
        protected abstract bool Changed(T oldValue, T newValue);
        
        public virtual void Analyze()
        {
            // Initialize
            foreach (var block in CFG.Blocks)
            {
                In[block] = InitialValue();
                Out[block] = InitialValue();
            }
            
            // Iterate to fixed point
            var worklist = new Queue<BasicBlock>(CFG.ReversePostOrder());
            var inWorklist = new HashSet<BasicBlock>(worklist);
            
            while (worklist.Count > 0)
            {
                var block = worklist.Dequeue();
                inWorklist.Remove(block);
                
                // Compute IN = Meet(OUT of predecessors)
                if (block.Predecessors.Count > 0)
                {
                    var predOuts = block.Predecessors.Select(p => Out[p]);
                    var newIn = Meet(predOuts);
                    
                    if (Changed(In[block], newIn))
                    {
                        In[block] = newIn;
                    }
                }
                
                // Compute OUT = Transfer(block, IN)
                var newOut = Transfer(block, In[block]);
                
                if (Changed(Out[block], newOut))
                {
                    Out[block] = newOut;
                    
                    // Add successors to worklist
                    foreach (var successor in block.Successors)
                    {
                        if (!inWorklist.Contains(successor))
                        {
                            worklist.Enqueue(successor);
                            inWorklist.Add(successor);
                        }
                    }
                }
            }
        }
    }
}
