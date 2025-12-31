using System;
using System.Text;
using System.Linq;

namespace BasicLang.Compiler.IR
{
    /// <summary>
    /// Pretty printer for IR - produces human-readable output
    /// </summary>
    public class IRPrettyPrinter : IIRVisitor
    {
        private readonly StringBuilder _output;
        private int _indent;
        
        public IRPrettyPrinter()
        {
            _output = new StringBuilder();
            _indent = 0;
        }
        
        public string GetOutput() => _output.ToString();
        
        private void WriteLine(string text)
        {
            _output.AppendLine(new string(' ', _indent * 2) + text);
        }
        
        private void Write(string text)
        {
            _output.Append(text);
        }
        
        private void Indent() => _indent++;
        private void Unindent() => _indent--;
        
        /// <summary>
        /// Print entire module
        /// </summary>
        public string Print(IRModule module)
        {
            _output.Clear();
            _indent = 0;
            
            WriteLine($"; Module: {module.Name}");
            WriteLine("");
            
            // Global variables
            if (module.GlobalVariables.Count > 0)
            {
                WriteLine("; Global Variables");
                foreach (var globalVar in module.GlobalVariables.Values)
                {
                    WriteLine($"{globalVar} : {globalVar.Type}");
                }
                WriteLine("");
            }
            
            // Functions
            foreach (var function in module.Functions)
            {
                function.Accept(this);
                WriteLine("");
            }
            
            return GetOutput();
        }
        
        public void Visit(IRFunction function)
        {
            // Function signature
            var paramStr = string.Join(", ", function.Parameters.Select(p => $"{p} : {p.Type}"));
            WriteLine($"define {function.ReturnType} @{function.Name}({paramStr}) {{");
            
            Indent();
            
            // Local variables
            if (function.LocalVariables.Count > 0)
            {
                WriteLine("; Local variables:");
                foreach (var local in function.LocalVariables)
                {
                    WriteLine($"; {local} : {local.Type}");
                }
                WriteLine("");
            }
            
            // Basic blocks
            foreach (var block in function.Blocks)
            {
                block.Accept(this);
            }
            
            Unindent();
            WriteLine("}");
        }
        
        public void Visit(BasicBlock block)
        {
            // Block label
            WriteLine($"{block.Name}:");
            Indent();
            
            // Predecessor info (as comment)
            if (block.Predecessors.Count > 0)
            {
                var preds = string.Join(", ", block.Predecessors.Select(p => p.Name));
                WriteLine($"; predecessors: {preds}");
            }
            
            // Instructions
            foreach (var instruction in block.Instructions)
            {
                instruction.Accept(this);
            }
            
            Unindent();
            WriteLine("");
        }
        
        public void Visit(IRConstant constant)
        {
            // Constants are usually printed inline, not as separate instructions
        }
        
        public void Visit(IRVariable variable)
        {
            // Variables are usually printed inline
        }
        
        public void Visit(IRBinaryOp binaryOp)
        {
            WriteLine(binaryOp.ToString());
        }
        
        public void Visit(IRUnaryOp unaryOp)
        {
            WriteLine(unaryOp.ToString());
        }
        
        public void Visit(IRCompare compare)
        {
            WriteLine(compare.ToString());
        }
        
        public void Visit(IRAssignment assignment)
        {
            WriteLine(assignment.ToString());
        }
        
        public void Visit(IRLoad load)
        {
            WriteLine(load.ToString());
        }
        
        public void Visit(IRStore store)
        {
            WriteLine(store.ToString());
        }
        
        public void Visit(IRAlloca alloca)
        {
            WriteLine(alloca.ToString());
        }
        
        public void Visit(IRGetElementPtr gep)
        {
            WriteLine(gep.ToString());
        }
        
        public void Visit(IRCast cast)
        {
            WriteLine(cast.ToString());
        }
        
        public void Visit(IRCall call)
        {
            WriteLine(call.ToString());
        }
        
        public void Visit(IRReturn ret)
        {
            WriteLine(ret.ToString());
        }
        
        public void Visit(IRBranch branch)
        {
            WriteLine(branch.ToString());
        }
        
        public void Visit(IRConditionalBranch condBranch)
        {
            WriteLine(condBranch.ToString());
        }
        
        public void Visit(IRSwitch switchInst)
        {
            WriteLine($"switch {switchInst.Value}, label {switchInst.DefaultTarget.Name} [");
            Indent();
            foreach (var (caseValue, target) in switchInst.Cases)
            {
                WriteLine($"{caseValue} => {target.Name}");
            }
            Unindent();
            WriteLine("]");
        }
        
        public void Visit(IRPhi phi)
        {
            WriteLine(phi.ToString());
        }
        
        public void Visit(IRLabel label)
        {
            Unindent();
            WriteLine(label.ToString());
            Indent();
        }
        
        public void Visit(IRComment comment)
        {
            WriteLine(comment.ToString());
        }

        public void Visit(IRArrayAlloc arrayAlloc)
        {
            WriteLine(arrayAlloc.ToString());
        }

        public void Visit(IRArrayStore arrayStore)
        {
            WriteLine(arrayStore.ToString());
        }

        public void Visit(IRAwait awaitInst)
        {
            WriteLine(awaitInst.ToString());
        }

        public void Visit(IRYield yieldInst)
        {
            WriteLine(yieldInst.ToString());
        }

        public void Visit(IRNewObject newObj)
        {
            WriteLine(newObj.ToString());
        }

        public void Visit(IRInstanceMethodCall methodCall)
        {
            WriteLine(methodCall.ToString());
        }

        public void Visit(IRBaseMethodCall baseCall)
        {
            WriteLine(baseCall.ToString());
        }

        public void Visit(IRFieldAccess fieldAccess)
        {
            WriteLine(fieldAccess.ToString());
        }

        public void Visit(IRFieldStore fieldStore)
        {
            WriteLine(fieldStore.ToString());
        }

        public void Visit(IRTupleElement tupleElement)
        {
            WriteLine(tupleElement.ToString());
        }

        public void Visit(IRTryCatch tryCatch)
        {
            WriteLine(tryCatch.ToString());
        }

        public void Visit(IRInlineCode inlineCode)
        {
            WriteLine($"inline {inlineCode.Language} {{");
            foreach (var line in inlineCode.Code.Split('\n'))
            {
                WriteLine($"    {line.TrimEnd()}");
            }
            WriteLine("}");
        }
    }

    /// <summary>
    /// Pretty printer for control flow graphs
    /// </summary>
    public class CFGPrinter
    {
        public static string Print(ControlFlowGraph cfg)
        {
            var sb = new StringBuilder();
            
            sb.AppendLine($"Control Flow Graph for {cfg.Function.Name}");
            sb.AppendLine("=" + new string('=', 60));
            sb.AppendLine();
            
            foreach (var block in cfg.Blocks)
            {
                sb.AppendLine($"Block: {block.Name} (ID: {block.Id})");
                sb.AppendLine($"  Predecessors: {string.Join(", ", block.Predecessors.Select(b => b.Name))}");
                sb.AppendLine($"  Successors: {string.Join(", ", block.Successors.Select(b => b.Name))}");
                
                if (block.ImmediateDominator != null)
                {
                    sb.AppendLine($"  Immediate Dominator: {block.ImmediateDominator.Name}");
                }
                
                if (block.Dominators.Count > 0)
                {
                    sb.AppendLine($"  Dominators: {string.Join(", ", block.Dominators.Select(b => b.Name))}");
                }
                
                if (block.DominanceFrontier.Count > 0)
                {
                    sb.AppendLine($"  Dominance Frontier: {string.Join(", ", block.DominanceFrontier.Select(b => b.Name))}");
                }
                
                sb.AppendLine();
            }
            
            // Loop information
            if (cfg.NaturalLoops.Count > 0)
            {
                sb.AppendLine("Natural Loops:");
                for (int i = 0; i < cfg.NaturalLoops.Count; i++)
                {
                    var loop = cfg.NaturalLoops[i];
                    sb.AppendLine($"  Loop {i}: {string.Join(", ", loop.Select(b => b.Name))}");
                }
                sb.AppendLine();
            }
            
            return sb.ToString();
        }
    }
    
    /// <summary>
    /// Graphviz DOT format printer for visualizing CFG
    /// </summary>
    public class DotGraphPrinter
    {
        public static string PrintCFG(ControlFlowGraph cfg)
        {
            var sb = new StringBuilder();
            
            sb.AppendLine($"digraph \"{cfg.Function.Name}\" {{");
            sb.AppendLine("  node [shape=box];");
            sb.AppendLine();
            
            // Nodes
            foreach (var block in cfg.Blocks)
            {
                var label = $"{block.Name}\\n";
                label += string.Join("\\l", block.Instructions.Select(i => EscapeDot(i.ToString()))) + "\\l";
                sb.AppendLine($"  {block.Name} [label=\"{label}\"];");
            }
            
            sb.AppendLine();
            
            // Edges
            foreach (var block in cfg.Blocks)
            {
                foreach (var successor in block.Successors)
                {
                    sb.AppendLine($"  {block.Name} -> {successor.Name};");
                }
            }
            
            sb.AppendLine("}");
            
            return sb.ToString();
        }
        
        private static string EscapeDot(string text)
        {
            return text.Replace("\"", "\\\"")
                      .Replace("<", "\\<")
                      .Replace(">", "\\>")
                      .Replace("{", "\\{")
                      .Replace("}", "\\}")
                      .Replace("|", "\\|");
        }
    }
}
