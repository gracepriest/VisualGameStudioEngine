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
        public Dictionary<BasicBlock, HashSet<BasicBlock>> DominatorTree { get; private set; }
        public Dictionary<BasicBlock, HashSet<BasicBlock>> PostDominatorTree { get; private set; }
        public Dictionary<BasicBlock, int> BlockDepths { get; private set; }
        public List<List<BasicBlock>> NaturalLoops { get; private set; }
        
        public ControlFlowGraph(IRFunction function)
        {
            Function = function;
            DominatorTree = new Dictionary<BasicBlock, HashSet<BasicBlock>>();
            PostDominatorTree = new Dictionary<BasicBlock, HashSet<BasicBlock>>();
            BlockDepths = new Dictionary<BasicBlock, int>();
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
                var terminator = block.GetTerminator();
                
                if (terminator is IRBranch branch)
                {
                    AddEdge(block, branch.Target);
                }
                else if (terminator is IRConditionalBranch condBranch)
                {
                    AddEdge(block, condBranch.TrueTarget);
                    AddEdge(block, condBranch.FalseTarget);
                }
                else if (terminator is IRSwitch switchInst)
                {
                    AddEdge(block, switchInst.DefaultTarget);
                    foreach (var (_, target) in switchInst.Cases)
                    {
                        AddEdge(block, target);
                    }
                }
                // IRReturn has no successors
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
            BuildDominatorTree();
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
        /// Build dominator tree from immediate dominators
        /// </summary>
        private void BuildDominatorTree()
        {
            DominatorTree.Clear();
            
            foreach (var block in Blocks)
            {
                DominatorTree[block] = new HashSet<BasicBlock>();
            }
            
            foreach (var block in Blocks)
            {
                if (block.ImmediateDominator != null)
                {
                    DominatorTree[block.ImmediateDominator].Add(block);
                }
            }
        }
        
        /// <summary>
        /// Compute dominance frontier for all blocks
        /// DF(X) is the set of blocks where X's dominance stops
        /// </summary>
        public void ComputeDominanceFrontier()
        {
            foreach (var block in Blocks)
            {
                block.DominanceFrontier.Clear();
            }
            
            foreach (var block in Blocks)
            {
                if (block.Predecessors.Count >= 2)
                {
                    foreach (var pred in block.Predecessors)
                    {
                        var runner = pred;
                        
                        while (runner != block.ImmediateDominator)
                        {
                            runner.DominanceFrontier.Add(block);
                            
                            if (runner.ImmediateDominator == null)
                                break;
                            
                            runner = runner.ImmediateDominator;
                        }
                    }
                }
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
                    // Back edge: successor dominates block
                    if (successor.Dominators.Contains(block))
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
        /// Compute depth of each block (distance from entry)
        /// </summary>
        public void ComputeBlockDepths()
        {
            BlockDepths.Clear();
            
            foreach (var block in Blocks)
            {
                BlockDepths[block] = int.MaxValue;
            }
            
            BlockDepths[EntryBlock] = 0;
            
            var queue = new Queue<BasicBlock>();
            queue.Enqueue(EntryBlock);
            
            while (queue.Count > 0)
            {
                var block = queue.Dequeue();
                int depth = BlockDepths[block];
                
                foreach (var successor in block.Successors)
                {
                    if (BlockDepths[successor] > depth + 1)
                    {
                        BlockDepths[successor] = depth + 1;
                        queue.Enqueue(successor);
                    }
                }
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
        /// </summary>
        public bool IsReducible()
        {
            // A CFG is reducible if all back edges are to loop headers
            var backEdges = FindBackEdges();
            
            foreach (var (tail, head) in backEdges)
            {
                // Check if head dominates tail (making it a proper loop header)
                if (!head.Dominators.Contains(tail))
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
