using System;
using System.Globalization;
using System.Text;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.SemanticAnalysis;

namespace BasicLang.Compiler.CodeGen.JavaScript
{
    /// <summary>
    /// Emits ES-module JavaScript from BasicLang IR.
    ///
    /// <para><b>Design principle (spec): "lowers cleanly to JS, or is rejected."</b> Only
    /// features with a 1:1 native JS construct are emitted; the emulation tail (ByRef,
    /// method overloading, Long, Char, value Structure, operator overloading, .NET BCL
    /// types) is refused at build time by <c>JsCapabilityChecker</c> with BL70xx codes.
    /// Every open C++ backend bug is a feature that LOOKED supported and was silently
    /// wrong at runtime; a build-time refusal is strictly better than a half
    /// implementation.</para>
    ///
    /// <para><b>Why this implements <see cref="IIRVisitor"/> directly instead of extending
    /// <see cref="CodeGeneratorBase"/>.</b> Two reasons, both load-bearing:</para>
    /// <list type="number">
    /// <item><description>The base class names every SSA temp (<c>t0</c>, <c>t1</c>, ...),
    /// which produces output nobody can read in devtools. Readable output is a
    /// requirement here, not a nicety — it is half of what source maps are for.
    /// <c>ImprovedCSharpCodeGenerator</c> made the same call for the same reason.</description></item>
    /// <item><description>The base class declares <c>Visit(IRThrow)</c> and
    /// <c>Visit(IRIndexerStore)</c> as <c>virtual {}</c> — silent no-ops. That is exactly
    /// how LLVM and MSIL came to silently drop collection indexed writes (see the TODO at
    /// ICodeGenerator.cs). Here every unimplemented visitor THROWS.</description></item>
    /// </list>
    /// </summary>
    public class JavaScriptCodeGenerator : ICodeGenerator
    {
        private readonly StringBuilder _output = new StringBuilder();
        private readonly CodeGenOptions _options;

        public string BackendName => "JavaScript";
        public TargetPlatform Target => TargetPlatform.JavaScript;
        public ITypeMapper TypeMapper { get; }

        /// <summary>Generated JavaScript from the last <see cref="Generate"/> call.</summary>
        public string GeneratedCode => _output.ToString();

        public JavaScriptCodeGenerator(CodeGenOptions options = null)
        {
            _options = options ?? new CodeGenOptions();
            TypeMapper = new JavaScriptTypeMapper();
        }

        // ------------------------------------------------------------------
        // Emission state
        // ------------------------------------------------------------------

        private int _indentLevel;

        private void Line(string text = "")
        {
            if (text.Length == 0) { _output.Append('\n'); return; }
            _output.Append(new string(' ', _indentLevel * _options.IndentSize)).Append(text).Append('\n');
        }

        public string Generate(IRModule module)
        {
            if (module == null) throw new ArgumentNullException(nameof(module));

            // Refuse before emitting anything. rejectCollections: false because
            // List/Dictionary DO lower here (Array/Map) — the LLVM/MSIL posture of
            // rejecting them outright would be wrong. ownInlineLanguage "javascript" lets
            // a js{} block through while a cpp{} block is still an error: an inline block
            // this backend cannot emit must fail loudly, not be dropped into a
            // do-nothing program.
            ForeignFeatureChecker.Check(module, "JavaScript", rejectCollections: false,
                ownInlineLanguage: "javascript");
            JsCapabilityChecker.Check(module);

            _output.Clear();
            _indentLevel = 0;

            // ES module: strict mode is implicit, so no "use strict" prologue.
            if (_options.GenerateComments)
            {
                Line($"// Generated from BasicLang module '{module.Name}' by the JavaScript backend.");
                Line();
            }

            // Class and interface member bodies also live in module.Functions, under their
            // UNQUALIFIED name — `Class A.Handle` and `Class B.Handle` are both "Handle".
            // Emitting them here would produce two top-level `function Handle()` declarations
            // and the second would silently win. Class emission is plan task 17; until then
            // they are skipped rather than emitted under a colliding name.
            var memberBodies = module.CollectMemberImplementations();

            foreach (var function in module.Functions)
            {
                if (function.IsExternal) continue;
                if (memberBodies.Contains(function)) continue;
                function.Accept(this);
                Line();
            }

            EmitEntryPoint(module);
            return _output.ToString();
        }

        /// <summary>
        /// A module that defines Main runs it. Unlike C#, JS has no implicit entry point —
        /// without this the emitted file declares functions and does nothing.
        /// </summary>
        private void EmitEntryPoint(IRModule module)
        {
            if (!_options.GenerateMainMethod) return;

            foreach (var function in module.Functions)
            {
                if (string.Equals(function.Name, "Main", StringComparison.OrdinalIgnoreCase))
                {
                    Line($"{SanitizeName(function.Name)}();");
                    return;
                }
            }
        }

        // ------------------------------------------------------------------
        // Expression rendering
        //
        // Separate from the visitors on purpose: IIRVisitor returns void, so it can emit
        // STATEMENTS but cannot produce the string an enclosing expression needs. Same
        // split ImprovedCSharpCodeGenerator uses.
        // ------------------------------------------------------------------

        private string Expr(IRValue value)
        {
            switch (value)
            {
                case null:
                    return "null";
                case IRConstant c:
                    return Constant(c);
                case IRVariable v:
                    return SanitizeName(v.Name);

                // These are IRValues that ALSO appear as entries in block.Instructions, so
                // their visitor has already emitted `const <name> = ...`. Referencing them by
                // name keeps evaluation order intact; re-rendering the operand tree inline
                // would evaluate side effects twice and, worse, silently drop an optimizer
                // rewrite that re-pointed the node.
                case IRBinaryOp b:
                    return SanitizeName(b.Name);
                case IRCompare c2:
                    return SanitizeName(c2.Name);
                case IRUnaryOp u:
                    return SanitizeName(u.Name);
                case IRCall call:
                    return SanitizeName(call.Name);

                default:
                    throw NotYet(value.GetType().Name + " (as an expression)");
            }
        }

        /// <summary>
        /// Renders a binary operation. Every arm is deliberate — an unmapped kind THROWS
        /// rather than falling back to a plausible operator, because the failure mode of a
        /// wrong arm here is a program that runs and computes the wrong number.
        /// </summary>
        private string BinaryExpr(IRBinaryOp op)
        {
            var l = Expr(op.Left);
            var r = Expr(op.Right);

            switch (op.Operation)
            {
                case BinaryOpKind.Add: return $"({l} + {r})";
                case BinaryOpKind.Sub: return $"({l} - {r})";
                case BinaryOpKind.Mul: return $"({l} * {r})";
                case BinaryOpKind.Div: return $"({l} / {r})";

                // BasicLang `\`. JS has NO integer-division operator — `/` is always floating
                // point — and .NET truncates TOWARD ZERO. Math.floor is the tempting wrong
                // answer: it agrees for positives and gives -4 where .NET gives -3.
                case BinaryOpKind.IntDiv: return $"Math.trunc({l} / {r})";

                // .NET's Mod takes the sign of the DIVIDEND, and so does JS's %. They agree
                // exactly, so a bare operator is correct here — unlike IntDiv above.
                case BinaryOpKind.Mod: return $"({l} % {r})";

                // String concatenation is its own kind, so `+` here is never numeric addition
                // in disguise.
                case BinaryOpKind.Concat: return $"({l} + {r})";

                case BinaryOpKind.Eq: return $"({l} === {r})";
                case BinaryOpKind.Ne: return $"({l} !== {r})";
                case BinaryOpKind.Lt: return $"({l} < {r})";
                case BinaryOpKind.Le: return $"({l} <= {r})";
                case BinaryOpKind.Gt: return $"({l} > {r})";
                case BinaryOpKind.Ge: return $"({l} >= {r})";

                case BinaryOpKind.BitwiseAnd: return $"({l} & {r})";
                case BinaryOpKind.BitwiseOr: return $"({l} | {r})";
                case BinaryOpKind.Xor: return $"({l} ^ {r})";
                case BinaryOpKind.Shl: return $"({l} << {r})";
                case BinaryOpKind.Shr: return $"({l} >> {r})";

                // ⚠ And/Or are NOT mapped, deliberately. IRNodes.cs groups them under
                // "Logical (short-circuit)", but VB's `And`/`Or` are NON-short-circuit
                // (`AndAlso`/`OrElse` are the short-circuiting pair) and are bitwise over
                // integers. An open chip on the C++ backend records the same ambiguity. Since
                // `&&` and `&` differ observably — on operand evaluation AND on integers —
                // guessing would ship silently-wrong output. Throw until the semantics are
                // settled, exactly as IntDiv was left unmapped before it was measured.
                default:
                    throw NotYet($"BinaryOpKind.{op.Operation}");
            }
        }

        private string UnaryExpr(IRUnaryOp op)
        {
            var v = Expr(op.Operand);
            switch (op.Operation)
            {
                case UnaryOpKind.Neg: return $"(-{v})";
                case UnaryOpKind.Not: return $"(!{v})";
                case UnaryOpKind.BitwiseNot: return $"(~{v})";
                default:
                    throw NotYet($"UnaryOpKind.{op.Operation}");
            }
        }

        private string CompareExpr(IRCompare op)
        {
            var l = Expr(op.Left);
            var r = Expr(op.Right);
            switch (op.Comparison)
            {
                // === and !==, never == : JS's loose equality would make 0 == "" true, which
                // no BasicLang comparison ever means.
                case CompareKind.Eq: return $"({l} === {r})";
                case CompareKind.Ne: return $"({l} !== {r})";
                case CompareKind.Lt: return $"({l} < {r})";
                case CompareKind.Le: return $"({l} <= {r})";
                case CompareKind.Gt: return $"({l} > {r})";
                case CompareKind.Ge: return $"({l} >= {r})";
                default:
                    throw NotYet($"CompareKind.{op.Comparison}");
            }
        }

        private static string Constant(IRConstant constant)
        {
            var v = constant.Value;
            switch (v)
            {
                case null: return "null";
                case bool b: return b ? "true" : "false";
                case string s: return "\"" + EscapeJsString(s) + "\"";
                // InvariantCulture is load-bearing: a comma decimal separator from the host
                // locale would emit `3,14`, which inside a call becomes a SECOND ARGUMENT
                // rather than a syntax error. Pinned by Emits_DoubleConstant_InvariantCulture.
                case double d: return d.ToString("R", CultureInfo.InvariantCulture);
                case float f: return f.ToString("R", CultureInfo.InvariantCulture);
                case decimal m: return m.ToString(CultureInfo.InvariantCulture);

                // A Char or a 64-bit integer can reach the output as a BARE LITERAL with no
                // declared position anywhere, so JsCapabilityChecker's declaration walk is
                // structurally blind to it — IRConstant is never an entry in
                // block.Instructions, only ever an operand. This is the only guard on that
                // channel, and it is not theoretical: before it existed,
                // Console.WriteLine("a"c) emitted `console.log(a);` — a bare undeclared
                // identifier, a ReferenceError in the browser from a green build.
                case char: throw JsCapabilityChecker.BannedConstantRejection("Char", v);
                case long: throw JsCapabilityChecker.BannedConstantRejection("Long", v);
                case ulong: throw JsCapabilityChecker.BannedConstantRejection("ULong", v);

                default: return Convert.ToString(v, CultureInfo.InvariantCulture);
            }
        }

        private static string EscapeJsString(string s) => s
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");

        /// <summary>
        /// JS identifiers allow letters, digits, _ and $. BasicLang's `Me` is `this`.
        /// </summary>
        private static string SanitizeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "_unnamed";
            if (name.Equals("Me", StringComparison.OrdinalIgnoreCase)) return "this";

            var sb = new StringBuilder(name.Length);
            foreach (var ch in name)
                if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '$') sb.Append(ch);

            if (sb.Length == 0) return "_unnamed";
            if (char.IsDigit(sb[0])) sb.Insert(0, '_');
            return sb.ToString();
        }

        /// <summary>
        /// Lower a call target to its JS form.
        ///
        /// <para>An UNQUALIFIED name is a user function and emits as itself. A DOTTED name
        /// is a stdlib/framework call needing a deliberate mapping — the full table lands
        /// in <c>JavaScriptStdLib</c> (plan task 24). Until then anything unmapped throws,
        /// so an unimplemented builtin cannot silently emit a call to a function that does
        /// not exist in the browser.</para>
        /// </summary>
        private static string CallTarget(string functionName)
        {
            if (string.IsNullOrEmpty(functionName)) throw NotYet("a call with no target name");

            // Conversion builtins are bare names, so they must be intercepted BEFORE the
            // user-function passthrough below — otherwise `CInt(x)` emits a call to a
            // function named CInt that exists nowhere.
            switch (functionName)
            {
                // Truncates TOWARD ZERO, matching .NET. Math.round would give 4 for 3.7 and
                // Math.floor would give -4 for -3.7; only trunc agrees with CInt.
                case "CInt": return "Math.trunc";

                // Identity under erasure — Integer/Single/Double are all one JS number.
                // Number() is the identity rename rather than a no-op, so the call shape
                // stays a call and argument evaluation order is unchanged.
                case "CDbl":
                case "CSng": return "Number";

                case "CStr": return "String";
                case "CBool": return "Boolean";

                // CLng deliberately absent: it converts TO Long, which BL7003 bans.
            }

            if (functionName.IndexOf('.') < 0) return SanitizeName(functionName);

            switch (functionName)
            {
                case "Console.WriteLine": return "console.log";
                case "Console.Write":     return "process.stdout.write";
                default:
                    throw new NotSupportedException(
                        $"JavaScript backend: no lowering for '{functionName}'. " +
                        "Add it to JavaScriptStdLib (plan task 24), or reject it in " +
                        "JsCapabilityChecker with a BL70xx code if it cannot lower cleanly.");
            }
        }

        // ------------------------------------------------------------------
        // IIRVisitor
        //
        // Task 2 implements the Hello-World subset (IRFunction, BasicBlock, IRCall,
        // IRConstant, IRReturn). Everything else throws until its own task lands.
        //
        // NEVER convert one of these to an empty body to make a test pass. A silent
        // no-op here is indistinguishable from correct codegen at build time and shows
        // up as wrong output at runtime — the exact bug class this backend exists to
        // avoid inheriting.
        // ------------------------------------------------------------------

        /// <summary>
        /// Names consumed as operands somewhere in the current function.
        ///
        /// <para>Binding a call's result is keyed on ACTUAL USE, not on its declared type: a
        /// <c>Console.WriteLine</c> IRCall carries a non-Void Type even though nothing reads
        /// it, so a type test emits <c>const t0 = console.log(x);</c> for every print. That is
        /// harmless at runtime but this backend's output is meant to be READ in devtools —
        /// readability is half of what the source maps exist for.</para>
        /// </summary>
        private HashSet<string> _usedOperandNames = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// Collects every operand name the function consumes, using the repo's shared
        /// "values an instruction CONSUMES" enumeration rather than a private copy —
        /// CLAUDE.md: change it once, not per-consumer.
        /// </summary>
        private void CollectUsedOperands(IRFunction function)
        {
            _usedOperandNames = new HashSet<string>(StringComparer.Ordinal);

            foreach (var block in function.Blocks ?? new List<BasicBlock>())
            foreach (var instruction in block.Instructions)
            {
                foreach (var operand in IROperandWalker.EnumerateOperands(instruction))
                    if (!string.IsNullOrEmpty(operand?.Name))
                        _usedOperandNames.Add(operand.Name);

                // A terminator's condition is an operand too, and EnumerateOperands is flat.
                if (instruction is IRConditionalBranch cb && !string.IsNullOrEmpty(cb.Condition?.Name))
                    _usedOperandNames.Add(cb.Condition.Name);
                if (instruction is IRReturn r && !string.IsNullOrEmpty(r.Value?.Name))
                    _usedOperandNames.Add(r.Value.Name);
            }
        }

        /// <summary>True when something later reads this value, so it must be bound.</summary>
        private bool IsUsed(IRValue value) =>
            !string.IsNullOrEmpty(value?.Name) && _usedOperandNames.Contains(value.Name);

        /// <summary>Parameters and locals — names that are already declared in scope.</summary>
        private HashSet<string> _declaredNames = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// Emits a value-producing instruction's result.
        ///
        /// <para><b>Why this is not always a <c>const</c>.</b> IRBuilder RENAMES an
        /// expression's result to the variable it initialises — <c>i = i + 1</c> produces an
        /// IRBinaryOp whose Name is literally <c>i</c>. Emitting <c>const i = (i + 1)</c>
        /// declares a NEW binding that shadows the loop variable, and reading <c>i</c> in its
        /// own initialiser is a temporal-dead-zone ReferenceError. So a result whose name is
        /// an already-declared local or parameter is an ASSIGNMENT; only genuine SSA temps
        /// get a fresh <c>const</c>.</para>
        /// </summary>
        private void Bind(string name, string expression)
        {
            var js = SanitizeName(name);
            Line(_declaredNames.Contains(name)
                ? $"{js} = {expression};"
                : $"const {js} = {expression};");
        }

        private static Exception NotYet(string node) =>
            new NotSupportedException(
                $"JavaScript backend: {node} lowering is not implemented yet. " +
                "If this construct should be REJECTED rather than lowered, add it to " +
                "JsCapabilityChecker with a BL70xx code instead of implementing it here.");

        public void Visit(IRFunction function)
        {
            var parameters = string.Join(", ", function.Parameters.ConvertAll(p => SanitizeName(p.Name)));

            // Iterators and async get their JS keywords in tasks 21/22; until then a
            // function carrying those flags would emit a plain function that silently
            // loses its semantics, so refuse it.
            if (function.IsAsync) throw NotYet("Async functions (plan task 21)");
            if (function.IsIterator) throw NotYet("Iterator functions (plan task 22)");

            CollectUsedOperands(function);

            _currentFunction = function;
            _emitted.Clear();
            _loopEnds.Clear();
            _pendingMerges.Clear();

            Line($"function {SanitizeName(function.Name)}({parameters}) {{");
            _indentLevel++;

            // Declare user locals up front. `let`, not `const`: unlike SSA temps these are
            // reassigned by IRAssignment. Declaring them here rather than at first assignment
            // matches BasicLang scoping — a `Dim` inside an If branch is visible after it,
            // whereas a JS `let` at the assignment site would not be.
            _declaredNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in function.Parameters ?? new List<IRVariable>())
                _declaredNames.Add(p.Name);

            foreach (var local in function.LocalVariables ?? new List<IRVariable>())
            {
                if (!_declaredNames.Add(local.Name)) continue;
                Line($"let {SanitizeName(local.Name)};");
            }

            // EntryBlock-rooted, following terminators — never a walk of function.Blocks.
            EmitStructured(function.EntryBlock ?? function.Blocks?.FirstOrDefault());

            _indentLevel--;
            Line("}");
            _currentFunction = null;
        }

        // ------------------------------------------------------------------
        // Structured emission
        //
        // BasicLang IR is a goto-style CFG; JavaScript has no goto. Emission therefore
        // starts at EntryBlock and follows TERMINATORS, re-deriving if/else and loops from
        // the branch graph — it never walks IRFunction.Blocks.
        //
        // ⛔ Walking Blocks linearly is not merely untidy, it is WRONG: that list is in
        // CREATION order, and `if0.end` is created BEFORE `if0.elseif0.then`, so a linear
        // walk emits the merge block before the ElseIf body. Back-edges have no linear
        // rendering at all.
        // ------------------------------------------------------------------

        private readonly HashSet<BasicBlock> _emitted = new HashSet<BasicBlock>();

        /// <summary>Loop exit blocks, innermost last. Membership means `break`.</summary>
        private readonly Stack<BasicBlock> _loopEnds = new Stack<BasicBlock>();

        /// <summary>
        /// Merge blocks an ENCLOSING construct will emit once it closes. A branch to one of
        /// these emits nothing rather than inlining the continuation into the branch body.
        /// </summary>
        private readonly Stack<BasicBlock> _pendingMerges = new Stack<BasicBlock>();

        private IRFunction _currentFunction;

        /// <summary>
        /// A block is a loop header when it is a loop's condition block. Matched on the
        /// `.cond` SUFFIX — only loops create one.
        ///
        /// <para>⛔ Not <c>StartsWith("for.")</c>: the real names are <c>for0.cond</c>, so that
        /// test is always false. The C# backend carries exactly that dead check.</para>
        /// </summary>
        private static bool IsLoopHeader(BasicBlock block) =>
            block?.Name != null && block.Name.EndsWith(".cond", StringComparison.Ordinal);

        /// <summary>
        /// The merge block an If converges on: <c>if0.then</c> and <c>if0.elseif1.then</c>
        /// both belong to <c>if0.end</c>. Derived from the prefix before the FIRST dot, so
        /// nested ElseIf blocks resolve to the outer If's end.
        /// </summary>
        private BasicBlock FindMergeBlock(BasicBlock branchTarget)
        {
            var name = branchTarget?.Name;
            if (name == null || _currentFunction?.Blocks == null) return null;

            var dot = name.IndexOf('.');
            if (dot <= 0) return null;

            var end = name.Substring(0, dot) + ".end";
            foreach (var b in _currentFunction.Blocks)
                if (b.Name == end) return b;
            return null;
        }

        private void EmitStructured(BasicBlock block)
        {
            if (block == null || _emitted.Contains(block)) return;

            if (IsLoopHeader(block))
            {
                _emitted.Add(block);
                EmitLoop(block);
                return;
            }

            _emitted.Add(block);
            EmitInstructions(block);

            switch (block.GetTerminator())
            {
                case IRConditionalBranch cond:
                    EmitConditional(cond);
                    break;
                case IRBranch br:
                    EmitBranch(br);
                    break;
                case IRReturn:
                    break;   // Visit(IRReturn) already emitted it
                case null:
                    break;
                case IRSwitch:
                    throw NotYet("Select Case (plan task 14 follow-up)");
            }
        }

        /// <summary>
        /// Emits a block's instructions, skipping terminators — control flow is reconstructed
        /// structurally and must never be emitted as a statement.
        /// </summary>
        private void EmitInstructions(BasicBlock block)
        {
            foreach (var instruction in block.Instructions)
            {
                if (instruction is IRBranch || instruction is IRConditionalBranch || instruction is IRSwitch)
                    continue;
                instruction.Accept(this);
            }
        }

        /// <summary>
        /// Loops emit as <c>while (true) { …condition…; if (!c) break; …body… }</c>.
        ///
        /// <para>The condition's INSTRUCTIONS live in the header block and must be
        /// re-evaluated every iteration. Hoisting them above the loop would run a
        /// side-effecting condition once and spin forever.</para>
        /// </summary>
        private void EmitLoop(BasicBlock header)
        {
            var cond = header.GetTerminator() as IRConditionalBranch;
            if (cond == null) throw NotYet("a loop header with no conditional terminator");

            // A post-test Do…Loop branches into the BODY first, so by the time the header is
            // reached the body is already emitted and this would produce `while (true) {}` —
            // an infinite loop at runtime from a build that succeeded. Refuse instead.
            if (_emitted.Contains(cond.TrueTarget))
                throw NotYet("post-test Do…Loop (body precedes the condition)");

            Line("while (true) {");
            _indentLevel++;

            EmitInstructions(header);
            Line($"if (!{Expr(cond.Condition)}) break;");

            _loopEnds.Push(cond.FalseTarget);
            EmitStructured(cond.TrueTarget);
            _loopEnds.Pop();

            _indentLevel--;
            Line("}");

            EmitStructured(cond.FalseTarget);
        }

        private void EmitConditional(IRConditionalBranch cond)
        {
            var merge = FindMergeBlock(cond.TrueTarget);
            if (merge != null) _pendingMerges.Push(merge);

            Line($"if ({Expr(cond.Condition)}) {{");
            _indentLevel++;
            EmitStructured(cond.TrueTarget);
            _indentLevel--;

            // No `else` when the false path IS the merge point — that is a bare `If … End If`.
            if (cond.FalseTarget != null && cond.FalseTarget != merge)
            {
                Line("} else {");
                _indentLevel++;
                EmitStructured(cond.FalseTarget);
                _indentLevel--;
            }

            Line("}");

            if (merge != null)
            {
                _pendingMerges.Pop();
                EmitStructured(merge);
            }
        }

        /// <summary>
        /// An unconditional branch is one of three things, and only IDENTITY tells them apart:
        /// a loop exit (<c>break</c>), a merge an enclosing construct owns (emit nothing), or
        /// an ordinary fall-through (emit the target here).
        ///
        /// <para>⛔ Never key this on the <c>.end</c> NAME: two sibling loops both produce
        /// `.end` blocks, and a name test would turn a jump out of one into a `break` of the
        /// other.</para>
        /// </summary>
        private void EmitBranch(IRBranch br)
        {
            if (br.Target == null) return;

            if (_loopEnds.Contains(br.Target))
            {
                Line("break;");
                return;
            }

            if (_pendingMerges.Contains(br.Target)) return;

            EmitStructured(br.Target);
        }

        /// <summary>
        /// Not the emission driver — <see cref="EmitStructured"/> is. Kept because
        /// <see cref="IIRVisitor"/> requires it.
        /// </summary>
        public void Visit(BasicBlock block) => EmitInstructions(block);

        /// <summary>
        /// A bare constant is never a statement — constants reach output through
        /// <see cref="Expr"/>. Reaching here means an enclosing node forgot to render it.
        /// </summary>
        public void Visit(IRConstant constant) => throw NotYet(nameof(IRConstant) + " as a statement");
        public void Visit(IRVariable variable) => throw NotYet(nameof(IRVariable));
        // Value-producing instructions declare their result, and every later reference is by
        // name (see Expr). `const` because the IR is SSA — each temp is assigned exactly once,
        // so a rebind would be a bug worth having the JS engine catch.
        public void Visit(IRBinaryOp binaryOp) => Bind(binaryOp.Name, BinaryExpr(binaryOp));

        public void Visit(IRUnaryOp unaryOp) => Bind(unaryOp.Name, UnaryExpr(unaryOp));
        public void Visit(IRAssignment assignment) =>
            Line($"{SanitizeName(assignment.Target.Name)} = {Expr(assignment.Value)};");
        public void Visit(IRLoad load) => throw NotYet(nameof(IRLoad));
        public void Visit(IRStore store) => throw NotYet(nameof(IRStore));
        public void Visit(IRCall call)
        {
            // DEFENCE IN DEPTH — do not delete this now that BL7002 checks declarations.
            // ByRefArguments has a second source the declaration walk cannot see: a resolved
            // .NET target carries ref/out in its descriptor (IRBuilder.cs:3585), so no
            // BasicLang parameter is marked IsByRef and JsCapabilityChecker finds nothing.
            // Until BL7007 rejects .NET types outright, this is the only thing between such
            // a call and a by-value emit that silently discards the write-back.
            if (call.ByRefArguments != null && call.ByRefArguments.Contains(true))
                throw JsCapabilityChecker.ByRefArgumentRejection(call.FunctionName);

            var args = string.Join(", ", call.Arguments.ConvertAll(Expr));
            var invocation = $"{CallTarget(call.FunctionName)}({args})";

            // A call that PRODUCES a value binds it, because Expr(IRCall) refers to the
            // result by name. Emitting a bare statement would discard the value and leave
            // every reference pointing at an undeclared identifier.
            if (IsUsed(call))
                Bind(call.Name, invocation);
            else
                Line($"{invocation};");
        }

        public void Visit(IRReturn ret) =>
            Line(ret.Value == null ? "return;" : $"return {Expr(ret.Value)};");
        public void Visit(IRBranch branch) => throw NotYet(nameof(IRBranch));
        public void Visit(IRConditionalBranch condBranch) => throw NotYet(nameof(IRConditionalBranch));
        public void Visit(IRPhi phi) => throw NotYet(nameof(IRPhi));
        public void Visit(IRAlloca alloca) => throw NotYet(nameof(IRAlloca));
        public void Visit(IRGetElementPtr gep) => throw NotYet(nameof(IRGetElementPtr));
        public void Visit(IRCast cast) => throw NotYet(nameof(IRCast));
        public void Visit(IRCompare compare) => Bind(compare.Name, CompareExpr(compare));
        public void Visit(IRSwitch switchInst) => throw NotYet(nameof(IRSwitch));
        public void Visit(IRLabel label) => throw NotYet(nameof(IRLabel));
        public void Visit(IRComment comment) => throw NotYet(nameof(IRComment));
        public void Visit(IRArrayAlloc arrayAlloc) => throw NotYet(nameof(IRArrayAlloc));
        public void Visit(IRArrayStore arrayStore) => throw NotYet(nameof(IRArrayStore));
        public void Visit(IRAwait awaitInst) => throw NotYet(nameof(IRAwait));
        public void Visit(IRYield yieldInst) => throw NotYet(nameof(IRYield));
        public void Visit(IRNewObject newObj) => throw NotYet(nameof(IRNewObject));
        public void Visit(IRInstanceMethodCall methodCall) => throw NotYet(nameof(IRInstanceMethodCall));
        public void Visit(IRBaseMethodCall baseCall) => throw NotYet(nameof(IRBaseMethodCall));
        public void Visit(IRFieldAccess fieldAccess) => throw NotYet(nameof(IRFieldAccess));
        public void Visit(IRFieldStore fieldStore) => throw NotYet(nameof(IRFieldStore));
        public void Visit(IRTupleElement tupleElement) => throw NotYet(nameof(IRTupleElement));
        public void Visit(IRTryCatch tryCatch) => throw NotYet(nameof(IRTryCatch));
        public void Visit(IRInlineCode inlineCode) => throw NotYet(nameof(IRInlineCode));
        public void Visit(IRForEach forEach) => throw NotYet(nameof(IRForEach));
        public void Visit(IRIndexerAccess indexer) => throw NotYet(nameof(IRIndexerAccess));
        public void Visit(IRThrow throwInst) => throw NotYet(nameof(IRThrow));
        public void Visit(IRIndexerStore indexerStore) => throw NotYet(nameof(IRIndexerStore));
    }
}
