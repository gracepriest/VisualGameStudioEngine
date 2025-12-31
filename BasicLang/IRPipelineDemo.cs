using System;
using System.IO;
using BasicLang.Compiler;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.SemanticAnalysis;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.IR.Optimization;

namespace BasicLang.Compiler.Demo
{
    /// <summary>
    /// Demonstrates the complete IR generation and optimization pipeline
    /// </summary>
    class IRPipelineDemo
    {
        static void RunDemo(string[] args)
        {
            Console.WriteLine("=".PadRight(70, '='));
            Console.WriteLine("BasicLang IR Pipeline Demo");
            Console.WriteLine("=".PadRight(70, '='));
            Console.WriteLine();
            
            // Run various demos
            DemoSimpleFunction();
            DemoControlFlow();
            DemoLoops();
            DemoOptimizations();
            DemoCFGAnalysis();
            
            Console.WriteLine("\nDemo completed!");
        }
        
        /// <summary>
        /// Demo 1: Simple function compilation
        /// </summary>
        static void DemoSimpleFunction()
        {
            Console.WriteLine("\n" + "=".PadRight(70, '='));
            Console.WriteLine("Demo 1: Simple Function");
            Console.WriteLine("=".PadRight(70, '='));
            
            string source = @"
Function Add(a As Integer, b As Integer) As Integer
    Dim result As Integer = a + b
    Return result
End Function
";
            
            CompileAndShowIR(source, "Simple Function", false);
        }
        
        /// <summary>
        /// Demo 2: Control flow with if statements
        /// </summary>
        static void DemoControlFlow()
        {
            Console.WriteLine("\n" + "=".PadRight(70, '='));
            Console.WriteLine("Demo 2: Control Flow");
            Console.WriteLine("=".PadRight(70, '='));
            
            string source = @"
Function Max(a As Integer, b As Integer) As Integer
    If a > b Then
        Return a
    Else
        Return b
    End If
End Function
";
            
            CompileAndShowIR(source, "Control Flow", false);
        }
        
        /// <summary>
        /// Demo 3: Loops
        /// </summary>
        static void DemoLoops()
        {
            Console.WriteLine("\n" + "=".PadRight(70, '='));
            Console.WriteLine("Demo 3: Loops");
            Console.WriteLine("=".PadRight(70, '='));
            
            string source = @"
Function Factorial(n As Integer) As Integer
    Dim result As Integer = 1
    For i = 1 To n
        result = result * i
    Next i
    Return result
End Function
";
            
            CompileAndShowIR(source, "Loop", true);
        }
        
        /// <summary>
        /// Demo 4: Optimization effects
        /// </summary>
        static void DemoOptimizations()
        {
            Console.WriteLine("\n" + "=".PadRight(70, '='));
            Console.WriteLine("Demo 4: Optimization Effects");
            Console.WriteLine("=".PadRight(70, '='));
            
            string source = @"
Function Calculate() As Integer
    Dim x As Integer = 2 + 3
    Dim y As Integer = x * 4
    Dim z As Integer = 10 * 8
    Dim unused As Integer = 100 + 200
    Return y + z
End Function
";
            
            Console.WriteLine("\nSource Code:");
            Console.WriteLine(source);
            
            var (module, analyzer) = CompileToIR(source);
            
            if (module == null)
                return;
            
            // Show unoptimized
            var printer = new IRPrettyPrinter();
            Console.WriteLine("\n--- Unoptimized IR ---");
            Console.WriteLine(printer.Print(module));
            
            // Count instructions before
            int instructionsBefore = CountInstructions(module);
            
            // Optimize
            var pipeline = new OptimizationPipeline();
            pipeline.AddPass(new ConstantFoldingPass());
            pipeline.AddPass(new DeadCodeEliminationPass());
            pipeline.AddPass(new StrengthReductionPass());
            
            var result = pipeline.Run(module);
            
            Console.WriteLine($"\n--- Optimization Results ---");
            Console.WriteLine(result);
            Console.WriteLine();
            
            foreach (var passResult in result.PassResults)
            {
                Console.WriteLine($"  {passResult}");
            }
            
            // Count instructions after
            int instructionsAfter = CountInstructions(module);
            
            Console.WriteLine($"\nInstructions before: {instructionsBefore}");
            Console.WriteLine($"Instructions after: {instructionsAfter}");
            Console.WriteLine($"Reduction: {instructionsBefore - instructionsAfter} instructions " +
                            $"({(1.0 - (double)instructionsAfter / instructionsBefore) * 100:F1}%)");
            
            // Show optimized
            Console.WriteLine("\n--- Optimized IR ---");
            Console.WriteLine(printer.Print(module));
        }
        
        /// <summary>
        /// Demo 5: CFG analysis
        /// </summary>
        static void DemoCFGAnalysis()
        {
            Console.WriteLine("\n" + "=".PadRight(70, '='));
            Console.WriteLine("Demo 5: Control Flow Graph Analysis");
            Console.WriteLine("=".PadRight(70, '='));
            
            string source = @"
Function ComplexFlow(n As Integer) As Integer
    Dim result As Integer = 0
    
    If n < 0 Then
        Return -1
    End If
    
    For i = 0 To n
        If i Mod 2 == 0 Then
            result = result + i
        Else
            result = result - i
        End If
    Next i
    
    Return result
End Function
";
            
            Console.WriteLine("\nSource Code:");
            Console.WriteLine(source);
            
            var (module, analyzer) = CompileToIR(source);
            
            if (module == null || module.Functions.Count == 0)
                return;
            
            var function = module.Functions[0];
            
            // Build and analyze CFG
            var cfg = new ControlFlowGraph(function);
            cfg.Build();
            cfg.ComputeDominators();
            cfg.ComputeDominanceFrontier();
            cfg.ComputeBlockDepths();
            cfg.IdentifyLoops();
            
            // Print CFG info
            Console.WriteLine("\n--- Control Flow Graph ---");
            Console.WriteLine(CFGPrinter.Print(cfg));
            
            // Generate DOT graph
            string dotGraph = DotGraphPrinter.PrintCFG(cfg);
            Console.WriteLine("\n--- DOT Graph (for visualization) ---");
            Console.WriteLine(dotGraph);
            Console.WriteLine("\nTo visualize: dot -Tpng output.dot -o cfg.png");
            
            // Save to file
            try
            {
                string filename = "cfg_complex_flow.dot";
                File.WriteAllText(filename, dotGraph);
                Console.WriteLine($"\nDOT graph saved to: {filename}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nCouldn't save DOT file: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Compile source code to IR and optionally optimize
        /// </summary>
        static void CompileAndShowIR(string source, string description, bool showCFG)
        {
            Console.WriteLine($"\nCompiling: {description}");
            Console.WriteLine("\nSource Code:");
            Console.WriteLine(source);
            
            var (module, analyzer) = CompileToIR(source);
            
            if (module == null)
                return;
            
            // Print IR
            var printer = new IRPrettyPrinter();
            Console.WriteLine("\n--- Generated IR ---");
            Console.WriteLine(printer.Print(module));
            
            if (showCFG && module.Functions.Count > 0)
            {
                var cfg = new ControlFlowGraph(module.Functions[0]);
                cfg.Build();
                cfg.ComputeDominators();
                
                Console.WriteLine("\n--- Control Flow Graph ---");
                Console.WriteLine(CFGPrinter.Print(cfg));
            }
        }
        
        /// <summary>
        /// Helper: Compile source to IR module
        /// </summary>
        static (IRModule module, SemanticAnalyzer analyzer) CompileToIR(string source)
        {
            try
            {
                // Lex
                var lexer = new Lexer(source);
                var tokens = lexer.Tokenize();
                
                // Parse
                var parser = new Parser(tokens);
                var program = parser.Parse();
                
                // Semantic analysis
                var analyzer = new SemanticAnalyzer();
                if (!analyzer.Analyze(program))
                {
                    Console.WriteLine("\n*** Semantic Errors ***");
                    foreach (var error in analyzer.Errors)
                    {
                        Console.WriteLine($"  {error}");
                    }
                    return (null, null);
                }
                
                // Build IR
                var irBuilder = new IRBuilder(analyzer);
                var module = irBuilder.Build(program, "Demo");
                
                return (module, analyzer);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n*** Compilation Error ***");
                Console.WriteLine($"  {ex.GetType().Name}: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"  Inner: {ex.InnerException.Message}");
                }
                return (null, null);
            }
        }
        
        /// <summary>
        /// Count total instructions in module
        /// </summary>
        static int CountInstructions(IRModule module)
        {
            int count = 0;
            
            foreach (var function in module.Functions)
            {
                foreach (var block in function.Blocks)
                {
                    count += block.Instructions.Count;
                }
            }
            
            return count;
        }
    }
    
    /// <summary>
    /// Test suite for IR components
    /// </summary>
    class IRTests
    {
        public static void RunAllTests()
        {
            Console.WriteLine("Running IR Tests...\n");
            
            TestConstantFolding();
            TestDeadCodeElimination();
            TestCopyPropagation();
            TestCFGConstruction();
            TestDominators();
            TestLoopDetection();
            
            Console.WriteLine("\nAll tests completed!");
        }
        
        static void TestConstantFolding()
        {
            Console.WriteLine("Test: Constant Folding");
            
            var module = new IRModule("Test");
            var function = module.CreateFunction("test", new TypeInfo("Integer", TypeKind.Primitive));
            var block = function.CreateBlock("entry");
            
            // Create: %result = add 2, 3
            var left = new IRConstant(2, new TypeInfo("Integer", TypeKind.Primitive));
            var right = new IRConstant(3, new TypeInfo("Integer", TypeKind.Primitive));
            var add = new IRBinaryOp("result", BinaryOpKind.Add, left, right, 
                new TypeInfo("Integer", TypeKind.Primitive));
            block.AddInstruction(add);
            
            var pass = new ConstantFoldingPass();
            bool changed = pass.Run(module);
            
            Console.WriteLine($"  Changed: {changed}");
            Console.WriteLine($"  Modifications: {pass.ModificationCount}");
            Console.WriteLine($"  Result: {block.Instructions[0]}");
            Console.WriteLine();
        }
        
        static void TestDeadCodeElimination()
        {
            Console.WriteLine("Test: Dead Code Elimination");
            
            var module = new IRModule("Test");
            var function = module.CreateFunction("test", new TypeInfo("Void", TypeKind.Void));
            var block1 = function.CreateBlock("entry");
            var block2 = function.CreateBlock("unreachable");
            
            block1.AddInstruction(new IRReturn());
            block2.AddInstruction(new IRReturn());
            
            var cfg = new ControlFlowGraph(function);
            cfg.Build();
            
            int removed = cfg.RemoveUnreachableBlocks();
            
            Console.WriteLine($"  Removed {removed} unreachable blocks");
            Console.WriteLine($"  Remaining blocks: {function.Blocks.Count}");
            Console.WriteLine();
        }
        
        static void TestCopyPropagation()
        {
            Console.WriteLine("Test: Copy Propagation");
            
            var module = new IRModule("Test");
            var function = module.CreateFunction("test", new TypeInfo("Integer", TypeKind.Primitive));
            var block = function.CreateBlock("entry");
            
            var intType = new TypeInfo("Integer", TypeKind.Primitive);
            var x = new IRVariable("x", intType);
            var y = new IRVariable("y", intType);
            
            // x = 10
            block.AddInstruction(new IRAssignment(x, new IRConstant(10, intType)));
            // y = x
            block.AddInstruction(new IRAssignment(y, x));
            // result = y + 5  (should become x + 5)
            var add = new IRBinaryOp("result", BinaryOpKind.Add, y, new IRConstant(5, intType), intType);
            block.AddInstruction(add);
            
            var pass = new CopyPropagationPass();
            bool changed = pass.Run(module);
            
            Console.WriteLine($"  Changed: {changed}");
            Console.WriteLine($"  After propagation: {add}");
            Console.WriteLine();
        }
        
        static void TestCFGConstruction()
        {
            Console.WriteLine("Test: CFG Construction");
            
            var module = new IRModule("Test");
            var function = module.CreateFunction("test", new TypeInfo("Integer", TypeKind.Primitive));
            
            var entry = function.CreateBlock("entry");
            var then = function.CreateBlock("then");
            var else_ = function.CreateBlock("else");
            var end = function.CreateBlock("end");
            
            var cond = new IRVariable("cond", new TypeInfo("Boolean", TypeKind.Primitive));
            entry.AddInstruction(new IRConditionalBranch(cond, then, else_));
            then.AddInstruction(new IRBranch(end));
            else_.AddInstruction(new IRBranch(end));
            end.AddInstruction(new IRReturn());
            
            var cfg = new ControlFlowGraph(function);
            cfg.Build();
            
            Console.WriteLine($"  Entry successors: {entry.Successors.Count}");
            Console.WriteLine($"  End predecessors: {end.Predecessors.Count}");
            Console.WriteLine($"  Blocks: {string.Join(", ", cfg.Blocks.ConvertAll(b => b.Name))}");
            Console.WriteLine();
        }
        
        static void TestDominators()
        {
            Console.WriteLine("Test: Dominator Computation");
            
            var module = new IRModule("Test");
            var function = module.CreateFunction("test", new TypeInfo("Integer", TypeKind.Primitive));
            
            var entry = function.CreateBlock("entry");
            var loop = function.CreateBlock("loop");
            var exit = function.CreateBlock("exit");
            
            var cond = new IRVariable("cond", new TypeInfo("Boolean", TypeKind.Primitive));
            entry.AddInstruction(new IRBranch(loop));
            loop.AddInstruction(new IRConditionalBranch(cond, loop, exit));
            exit.AddInstruction(new IRReturn());
            
            var cfg = new ControlFlowGraph(function);
            cfg.Build();
            cfg.ComputeDominators();
            
            Console.WriteLine($"  Entry dominates: {string.Join(", ", entry.Dominators.Select(b => b.Name))}");
            Console.WriteLine($"  Loop dominates: {string.Join(", ", loop.Dominators.Select(b => b.Name))}");
            Console.WriteLine($"  Exit idom: {exit.ImmediateDominator?.Name ?? "none"}");
            Console.WriteLine();
        }
        
        static void TestLoopDetection()
        {
            Console.WriteLine("Test: Loop Detection");
            
            var module = new IRModule("Test");
            var function = module.CreateFunction("test", new TypeInfo("Integer", TypeKind.Primitive));
            
            var entry = function.CreateBlock("entry");
            var loop = function.CreateBlock("loop");
            var exit = function.CreateBlock("exit");
            
            var cond = new IRVariable("cond", new TypeInfo("Boolean", TypeKind.Primitive));
            entry.AddInstruction(new IRBranch(loop));
            loop.AddInstruction(new IRConditionalBranch(cond, loop, exit));
            exit.AddInstruction(new IRReturn());
            
            var cfg = new ControlFlowGraph(function);
            cfg.Build();
            cfg.ComputeDominators();
            cfg.IdentifyLoops();
            
            Console.WriteLine($"  Detected {cfg.NaturalLoops.Count} loop(s)");
            for (int i = 0; i < cfg.NaturalLoops.Count; i++)
            {
                var loop_blocks = string.Join(", ", cfg.NaturalLoops[i].Select(b => b.Name));
                Console.WriteLine($"  Loop {i}: {loop_blocks}");
            }
            Console.WriteLine();
        }
    }
}
