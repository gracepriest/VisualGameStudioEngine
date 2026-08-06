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

            // Collected before any emission: a user's own Function Len(...) shadows the
            // builtin, and the decision has to be available at every call site.
            _userFunctionNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in module.Functions)
                if (!string.IsNullOrEmpty(f.Name)) _userFunctionNames.Add(f.Name);

            // Classes BEFORE free functions: JS class declarations are not hoisted the way
            // function declarations are, so a `new Person()` reached from the entry point
            // would hit the temporal dead zone.
            foreach (var irClass in module.Classes.Values)
            {
                EmitClass(irClass, module);
                Line();
            }

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
        /// Names that resolve to <c>this.X</c> inside the method currently being emitted:
        /// the class's fields and properties, plus its bases'.
        /// </summary>
        private HashSet<string> _memberNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private void EmitClass(IRClass irClass, IRModule module)
        {
            // Events have no JS form, and RaiseEvent lowers to a call to `raise_X` that
            // nothing defines — emitting the class without the member would be silently
            // broken, so refuse.
            if (irClass.Events != null && irClass.Events.Count > 0)
                throw NotYet($"Events (on class '{irClass.Name}')");

            var header = $"class {SanitizeName(irClass.Name)}";
            if (!string.IsNullOrEmpty(irClass.BaseClass))
                header += $" extends {SanitizeName(irClass.BaseClass)}";

            // Interfaces and generic parameters ERASE — JS has neither.
            Line(header + " {");
            _indentLevel++;

            var members = MemberNames(irClass, module);

            foreach (var field in irClass.Fields ?? new List<IRField>())
            {
                // IRField.Initializer is a constant or null — IRBuilder drops every
                // non-constant initializer, so `Private items As New List(...)` arrives null
                // with nothing constructing it anywhere.
                var init = field.Initializer != null
                    ? Expr(field.Initializer)
                    : ArrayInitializer(field.Type) ?? TypeMapper.GetDefaultValue(field.Type);
                Line($"{(field.IsStatic ? "static " : "")}{SanitizeName(field.Name)} = {init};");
            }

            foreach (var prop in irClass.Properties ?? new List<IRProperty>())
                EmitProperty(prop, members);

            foreach (var ctor in irClass.Constructors ?? new List<IRConstructor>())
                EmitConstructor(irClass, ctor, members);

            foreach (var method in irClass.Methods ?? new List<IRMethod>())
                EmitMethod(irClass, method, members);

            _indentLevel--;
            Line("}");
        }

        /// <summary>
        /// Fields and properties of a class AND of its bases — the set an unqualified
        /// reference inside a method must resolve to <c>this.</c>.
        /// </summary>
        private static HashSet<string> MemberNames(IRClass irClass, IRModule module)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var current = irClass;
            while (current != null && seen.Add(current.Name))
            {
                foreach (var f in current.Fields ?? new List<IRField>())
                    if (f?.Name != null) names.Add(f.Name);
                foreach (var p in current.Properties ?? new List<IRProperty>())
                    if (p?.Name != null) names.Add(p.Name);

                if (string.IsNullOrEmpty(current.BaseClass)) break;
                module.Classes.TryGetValue(current.BaseClass, out current);
            }

            return names;
        }

        private void EmitProperty(IRProperty prop, HashSet<string> members)
        {
            // Both accessors null is an AUTO-PROPERTY: a plain class field, which makes the
            // same `obj.X` spelling work for reads and writes.
            if (prop.Getter == null && prop.Setter == null)
            {
                Line($"{(prop.IsStatic ? "static " : "")}{SanitizeName(prop.Name)} = {TypeMapper.GetDefaultValue(prop.Type)};");
                return;
            }

            if (prop.Getter != null && !prop.IsWriteOnly)
                EmitMemberBody($"get {SanitizeName(prop.Name)}()", prop.Getter, members);

            // The setter's implementation already has a parameter named `value`, so the JS
            // accessor's parameter name matches for free.
            if (prop.Setter != null && !prop.IsReadOnly)
                EmitMemberBody($"set {SanitizeName(prop.Name)}(value)", prop.Setter, members);
        }

        private void EmitConstructor(IRClass irClass, IRConstructor ctor, HashSet<string> members)
        {
            var impl = ctor.Implementation;
            if (impl == null) return;

            // ⛔ ctor.Parameters is ALWAYS EMPTY — reading it yields a zero-arg constructor
            // that ignores every argument. Parameters live on the implementation.
            var parameters = string.Join(", ", impl.Parameters.ConvertAll(p => SanitizeName(p.Name)));

            var prologue = new List<string>();
            if (!string.IsNullOrEmpty(irClass.BaseClass))
            {
                // JS forbids touching `this` before super(), and a computed base argument
                // would have emitted instructions that must run first — an ordering the IR
                // does not mark. Only constants and plain parameters are safe here.
                foreach (var arg in ctor.BaseConstructorArgs ?? new List<IRValue>())
                {
                    if (!(arg is IRConstant || arg is IRVariable))
                        throw NotYet("a computed base-constructor argument");
                }
                var baseArgs = string.Join(", ",
                    (ctor.BaseConstructorArgs ?? new List<IRValue>()).ConvertAll(Expr));
                prologue.Add($"super({baseArgs});");
            }

            EmitMemberBody($"constructor({parameters})", impl, members, prologue);
        }

        private void EmitMethod(IRClass irClass, IRMethod method, HashSet<string> members)
        {
            var impl = method.Implementation;

            // An abstract method has no body. Emitting an EMPTY one would silently override
            // the base's real implementation, so omit the member entirely.
            if (impl == null || method.IsAbstract) return;

            // ⛔ Implementation is bound with Functions.LastOrDefault(), so a method whose
            // body declares a lambda gets the LAMBDA's function instead — a pre-existing
            // compiler defect that would emit a scrambled class. Refuse rather than emit it.
            if (impl.IsLambda)
                throw NotYet($"a lambda inside method '{irClass.Name}.{method.Name}' (IRMethod.Implementation binds to the lambda)");

            var parameters = string.Join(", ", impl.Parameters.ConvertAll(p => SanitizeName(p.Name)));
            EmitMemberBody($"{(method.IsStatic ? "static " : "")}{SanitizeName(method.Name)}({parameters})",
                impl, members);
        }

        /// <summary>Emits a class member's body, with member names resolving to this.X.</summary>
        private void EmitMemberBody(string signature, IRFunction impl, HashSet<string> members,
            List<string> prologue = null)
        {
            var savedMembers = _memberNames;
            _memberNames = members;

            CollectUsedOperands(impl);
            _currentFunction = impl;
            _emitted.Clear();
            _loopEnds.Clear();
            _pendingMerges.Clear();
            _forEachEnds.Clear();
            _armDepth = 0;

            Line(signature + " {");
            _indentLevel++;

            foreach (var line in prologue ?? new List<string>()) Line(line);

            _declaredNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in impl.Parameters ?? new List<IRVariable>())
                _declaredNames.Add(p.Name);

            foreach (var local in impl.LocalVariables ?? new List<IRVariable>())
            {
                if (!_declaredNames.Add(local.Name)) continue;
                var init = ArrayInitializer(local.Type);
                Line(init == null
                    ? $"let {SanitizeName(local.Name)};"
                    : $"let {SanitizeName(local.Name)} = {init};");
            }

            EmitStructured(impl.EntryBlock ?? impl.Blocks?.FirstOrDefault());

            _indentLevel--;
            Line("}");

            _currentFunction = null;
            _memberNames = savedMembers;
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
                    return VariableRef(v);

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

                // ⛔ A gep is a POINTER, and it is rendered INLINE as an L-value rather than
                // bound to a temp. Materialising it as a value (`const t1 = a[0];`) makes a
                // following IRStore write to the TEMP and silently drops the array write —
                // this repo has already measured that exact bug, where C++ printed 0 where C#
                // printed 5. Safe to inline because a gep's operands are a base and indices,
                // with no side effects; do not extend inlining to nodes that have any.
                case IRGetElementPtr gep:
                    return ElementAccess(gep);
                case IRLoad load:
                    return Expr(load.Address);

                // `.Length` on an array OR a string. The rename to lowercase is MANDATORY:
                // JavaScript has no `.Length`, and reading it yields `undefined` with no
                // error, which then propagates as NaN through arithmetic. Matched
                // case-insensitively, as the C++ backend does.
                case IRFieldAccess fa when IsLengthAccess(fa):
                    return $"{Expr(fa.Object)}.length";

                // Fields and properties share one node — there is no IRPropertyAccess — which
                // is exactly right for JS, where a get/set accessor makes both spellings work.
                case IRFieldAccess fa:
                    return $"{Expr(fa.Object)}.{SanitizeName(fa.FieldName)}";

                case IRNewObject n:
                    return $"new {SanitizeName(n.ClassName)}({string.Join(", ", n.Arguments.ConvertAll(Expr))})";

                case IRInstanceMethodCall mc:
                    return InstanceCall(mc);

                // The ONLY non-virtual dispatch in the language. Everything else is
                // prototype dispatch, which is virtual by default and already matches
                // VB's Overridable/Overrides.
                case IRBaseMethodCall bc:
                    return $"super.{SanitizeName(bc.MethodName)}({string.Join(", ", bc.Arguments.ConvertAll(Expr))})";

                default:
                    throw NotYet(value.GetType().Name + " (as an expression)");
            }
        }

        /// <summary>
        /// Renders a value INLINE, rebuilding its operand tree instead of referring to a name.
        ///
        /// <para>Needed for <c>Select Case</c> pattern operands and <c>When</c> guards. IRBuilder
        /// builds a guard's expression with emission SUPPRESSED, so its nodes never appear in
        /// any block and no <c>const</c> was ever emitted for them — the ordinary by-name path
        /// would render an identifier that exists nowhere, giving a ReferenceError from a build
        /// that succeeded. Range bounds and case constants are NOT suppressed, but rendering
        /// them inline too costs nothing and keeps one path for all pattern operands.</para>
        ///
        /// <para>⛔ Pattern operands carry a NULL Type — the analyzer never visits case
        /// patterns — so nothing here may consult <c>value.Type</c>.</para>
        /// </summary>
        private string ExprInline(IRValue value)
        {
            switch (value)
            {
                case null: return "null";
                case IRConstant c: return Constant(c);
                case IRVariable v: return SanitizeName(v.Name);
                case IRBinaryOp b: return BinaryExprInline(b);
                case IRCompare cm: return CompareExprInline(cm);
                case IRUnaryOp u: return $"({UnaryOpToken(u.Operation)}{ExprInline(u.Operand)})";
                default:
                    // A call inside a guard WAS emitted (only the guard's own operator tree is
                    // suppressed), so the by-name path is right for it.
                    return Expr(value);
            }
        }

        /// <summary>
        /// Renders a binary operation. Every arm is deliberate — an unmapped kind THROWS
        /// rather than falling back to a plausible operator, because the failure mode of a
        /// wrong arm here is a program that runs and computes the wrong number.
        /// </summary>
        private string BinaryExpr(IRBinaryOp op) => RenderBinary(op, Expr);
        private string BinaryExprInline(IRBinaryOp op) => RenderBinary(op, ExprInline);

        private string RenderBinary(IRBinaryOp op, Func<IRValue, string> render)
        {
            var l = render(op.Left);
            var r = render(op.Right);

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

        private string UnaryExpr(IRUnaryOp op) => $"({UnaryOpToken(op.Operation)}{Expr(op.Operand)})";

        private static string UnaryOpToken(UnaryOpKind kind)
        {
            switch (kind)
            {
                case UnaryOpKind.Neg: return "-";
                case UnaryOpKind.Not: return "!";
                case UnaryOpKind.BitwiseNot: return "~";
                default:
                    throw NotYet($"UnaryOpKind.{kind}");
            }
        }

        private string CompareExpr(IRCompare op) => RenderCompare(op, Expr);
        private string CompareExprInline(IRCompare op) => RenderCompare(op, ExprInline);

        private string RenderCompare(IRCompare op, Func<IRValue, string> render)
        {
            var l = render(op.Left);
            var r = render(op.Right);
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
        /// <summary>
        /// Functions this program declares. A user's own <c>Function Len(...)</c> must WIN over
        /// the builtin — otherwise declaring it silently rebinds every call to the JS member
        /// form and the user's code never runs.
        /// </summary>
        private HashSet<string> _userFunctionNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Renders a BasicLang string builtin as its JavaScript equivalent, or returns false
        /// when the name is not one (or is shadowed by a user function).
        ///
        /// <para>Matched case-insensitively: IRBuilder only normalises casing when the call
        /// resolved to a symbol, and every stdlib provider in this repo keys its table
        /// case-insensitively for the same reason.</para>
        ///
        /// <para><b>The 1-based conversions are the whole risk here.</b> BasicLang's Mid and
        /// InStr are 1-based, JavaScript's are 0-based, and an off-by-one compiles perfectly
        /// and silently returns the wrong characters.</para>
        /// </summary>
        private bool TryStringBuiltin(string name, List<string> args, out string result)
        {
            result = null;
            if (string.IsNullOrEmpty(name) || _userFunctionNames.Contains(name)) return false;

            string Recv() => Receiver(args[0]);

            switch (name.ToLowerInvariant())
            {
                case "len" when args.Count == 1:
                    result = $"{Recv()}.length"; return true;
                case "ucase" when args.Count == 1:
                    result = $"{Recv()}.toUpperCase()"; return true;
                case "lcase" when args.Count == 1:
                    result = $"{Recv()}.toLowerCase()"; return true;
                case "trim" when args.Count == 1:
                    result = $"{Recv()}.trim()"; return true;

                // START IS 1-BASED. substr (rather than substring/slice) so the start
                // expression is evaluated ONCE — the two-argument alternatives need it twice.
                case "mid" when args.Count == 3:
                    result = $"{Recv()}.substr({args[1]} - 1, {args[2]})"; return true;
                case "mid" when args.Count == 2:
                    result = $"{Recv()}.substr({args[1]} - 1)"; return true;

                case "left" when args.Count == 2:
                    result = $"{Recv()}.substring(0, {args[1]})"; return true;

                // n == 0 must yield "", which this does; `.slice(-n)` would return the WHOLE
                // string for 0 because -0 === 0.
                case "right" when args.Count == 2:
                    result = $"{Recv()}.substring({Recv()}.length - ({args[1]}))"; return true;

                // 1-BASED, and 0 (not JS's -1) means "not found".
                case "instr" when args.Count == 2:
                    result = $"({Recv()}.indexOf({args[1]}) + 1)"; return true;

                // split/join replaces EVERY occurrence. `.replace(string, string)` changes
                // only the FIRST — a plausible-looking wrong answer rather than an error.
                case "replace" when args.Count == 3:
                    result = $"{Recv()}.split({args[1]}).join({args[2]})"; return true;

                case "chr" when args.Count == 1:
                    result = $"String.fromCharCode({args[0]})"; return true;
                case "asc" when args.Count == 1:
                    result = $"{Recv()}.charCodeAt(0)"; return true;

                default:
                    return false;
            }
        }

        /// <summary>
        /// Parenthesises a receiver unless it is already a bare identifier, so
        /// <c>Len(a &amp; b)</c> becomes <c>(a + b).length</c> rather than <c>a + b.length</c>.
        /// </summary>
        private static string Receiver(string expression)
        {
            if (string.IsNullOrEmpty(expression)) return expression;

            var simple = true;
            for (var i = 0; i < expression.Length; i++)
            {
                var c = expression[i];
                var ok = char.IsLetterOrDigit(c) || c == '_' || c == '$';
                if (i == 0) ok = char.IsLetter(c) || c == '_' || c == '$';
                if (!ok) { simple = false; break; }
            }

            return simple ? expression : $"({expression})";
        }

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

            // Already in scope — assign.
            if (_declaredNames.Contains(name)) { Line($"{js} = {expression};"); return; }

            // A CLASS MEMBER. `Count = Count + 1` produces an IRBinaryOp named `Count`
            // (IRBuilder renames a result to the variable it initialises), so a `const` here
            // would declare a fresh local and the member would never change — the method
            // would silently do nothing.
            if (_memberNames.Contains(name)) { Line($"this.{js} = {expression};"); return; }

            Line($"const {js} = {expression};");
        }

        /// <summary>
        /// The JS allocation for a sized array declaration, or null when the type is not a
        /// sized array (leave the declaration bare).
        ///
        /// <para><b>One helper, three callers.</b> Locals, module globals and class fields all
        /// declare arrays. The C# backend sized only locals, so a module-level <c>Dim g(4)</c>
        /// and a field <c>Public Cells(9)</c> emitted bare null references; the C++ backend hit
        /// the identical split. Everything routes through here.</para>
        ///
        /// <para><b><c>.fill()</c> is not cosmetic.</b> <c>new Array(4)</c> is a SPARSE array
        /// whose holes read as <c>undefined</c> and which iteration treats differently from
        /// filled slots. BasicLang expects 0 / "" / false per element type.</para>
        ///
        /// <para>⚠ <c>ArrayDimensionSizes</c> entries are ELEMENT COUNTS, not upper bounds:
        /// <c>Dim a(4)</c> is four elements here, matching the C# and C++ backends. That
        /// diverges from real VB, deliberately and consistently — do not add one.</para>
        /// </summary>
        private string ArrayInitializer(TypeInfo type)
        {
            if (type == null || type.Kind != TypeKind.Array) return null;
            if (type.ElementType == null) return null;

            var sizes = type.ArrayDimensionSizes;
            if (sizes == null || sizes.Count == 0) return null;

            // Zero is a legitimate size — `Dim a(0)` is an EMPTY array, and leaving it
            // undefined makes `For Each` throw at runtime rather than iterate zero times.
            // Only a negative size means "not a sized array".
            foreach (var s in sizes) if (s < 0) return null;

            var element = TypeMapper.GetDefaultValue(type.ElementType);
            var init = $"new Array({sizes[sizes.Count - 1]}).fill({element})";

            // Build outward from the innermost dimension. Array.from with a FACTORY, never
            // `new Array(d0).fill(new Array(d1)...)` — fill would share ONE row object across
            // every row, so a write to one row appears in all of them.
            for (var d = sizes.Count - 2; d >= 0; d--)
                init = $"Array.from({{length: {sizes[d]}}}, () => {init})";

            return init;
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

                // An array's SIZE is not in the instruction stream — Dim a(4) emits only an
                // IRAlloca with Size == 1. The element count lives on the DECLARATION, so
                // allocation belongs here.
                var init = ArrayInitializer(local.Type);
                Line(init == null
                    ? $"let {SanitizeName(local.Name)};"
                    : $"let {SanitizeName(local.Name)} = {init};");
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
        /// <c>For Each</c> exit blocks. Separate from <see cref="_loopEnds"/> because for-of
        /// is the one loop whose body fall-through and whose <c>Exit For</c> emit the SAME
        /// branch to the SAME block — the other loops fall through to a condition block
        /// instead, so their exits are unambiguous.
        /// </summary>
        private readonly Stack<BasicBlock> _forEachEnds = new Stack<BasicBlock>();

        /// <summary>
        /// How many conditional ARMS deep the current emission is, within the nearest
        /// enclosing <c>For Each</c> body. Zero means the straight-line path — where a branch
        /// to the loop end is simply the end of an iteration, not an exit.
        /// </summary>
        private int _armDepth;

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
                case IRSwitch sw:
                    EmitSelectCase(sw);
                    break;
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
            _armDepth++;
            EmitStructured(cond.TrueTarget);
            _armDepth--;
            _indentLevel--;

            // No `else` when the false path IS the merge point — that is a bare `If … End If`.
            if (cond.FalseTarget != null && cond.FalseTarget != merge)
            {
                Line("} else {");
                _indentLevel++;
                _armDepth++;
                EmitStructured(cond.FalseTarget);
                _armDepth--;
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
        /// <c>Select Case</c> lowers to an if/else-if chain over a subject temp — never a JS
        /// <c>switch</c>. JavaScript's switch has no <c>when</c> guard syntax, and ranges,
        /// comparisons, type/or/tuple patterns have no <c>case</c> form either; only bare
        /// constants could map, so one chain is both simpler and correct for every arm.
        /// </summary>
        private void EmitSelectCase(IRSwitch sw)
        {
            var subject = $"_sel{_selectDepth++}";
            Line($"const {subject} = {Expr(sw.Value)};");

            // Arms are grouped by TARGET BLOCK IDENTITY: `Case 1, 2, 3` produces three
            // separate Cases entries all pointing at ONE block, and emitting one arm each
            // would duplicate the body three times.
            var arms = new List<(BasicBlock Target, List<string> Tests)>();

            void AddTest(BasicBlock target, string test)
            {
                foreach (var arm in arms)
                {
                    if (arm.Target == target) { arm.Tests.Add(test); return; }
                }
                arms.Add((target, new List<string> { test }));
            }

            foreach (var (caseValue, target) in sw.Cases ?? new List<(IRValue, BasicBlock)>())
                AddTest(target, $"{subject} === {ExprInline(caseValue)}");

            foreach (var pattern in sw.PatternCases ?? new List<IRPatternCase>())
                AddTest(pattern.Target, PatternTest(subject, pattern));

            var first = true;
            foreach (var (target, tests) in arms)
            {
                // "} else if", not "}} else if": this is a nested string INSIDE the
                // interpolation hole, so brace-doubling does not apply and would emit two.
                Line($"{(first ? "if" : "} else if")} ({string.Join(" || ", tests)}) {{");
                first = false;
                _indentLevel++;
                _pendingMerges.Push(sw.EndBlock);
                _armDepth++;
                EmitStructured(target);
                _armDepth--;
                _pendingMerges.Pop();
                _indentLevel--;
            }

            // DefaultTarget is always non-null — IRBuilder creates `switch{N}.default` even
            // with no `Case Else` and branches it straight to `.end`. Emitting it as a real
            // `else` is therefore safe and costs one empty block at worst.
            if (arms.Count > 0)
            {
                Line("} else {");
                _indentLevel++;
                _pendingMerges.Push(sw.EndBlock);
                EmitStructured(sw.DefaultTarget);
                _pendingMerges.Pop();
                _indentLevel--;
                Line("}");
            }
            else
            {
                EmitStructured(sw.DefaultTarget);
            }

            _selectDepth--;
            EmitStructured(sw.EndBlock);
        }

        private int _selectDepth;

        /// <summary>
        /// The JS test for one pattern arm. Every operand goes through
        /// <see cref="ExprInline"/>: a <c>When</c> guard's tree was built with emission
        /// suppressed and exists in no block, so a by-name reference would be undefined.
        /// </summary>
        private string PatternTest(string subject, IRPatternCase pattern)
        {
            string test;
            switch (pattern)
            {
                case IRConstantPatternCase c:
                    test = $"{subject} === {ExprInline(c.Value)}";
                    break;

                // Inclusive at both ends, matching `Case 1 To 10`.
                case IRRangePatternCase r:
                    test = $"({subject} >= {ExprInline(r.LowerBound)} && {subject} <= {ExprInline(r.UpperBound)})";
                    break;

                // Operator is a raw STRING from the parser, not an enum.
                case IRComparisonPatternCase cmp:
                    test = $"{subject} {ComparisonPatternOperator(cmp.Operator)} {ExprInline(cmp.CompareValue)}";
                    break;

                case IRNothingPatternCase:
                    test = $"({subject} === null || {subject} === undefined)";
                    break;

                case IROrPatternCase or:
                    test = "(" + string.Join(" || ",
                        or.Alternatives.ConvertAll(a => PatternTest(subject, a))) + ")";
                    break;

                // A bare binding matches anything and binds the subject to a name.
                case IRBindingPatternCase:
                    test = "true";
                    break;

                default:
                    throw NotYet($"Select Case pattern {pattern.GetType().Name}");
            }

            if (!string.IsNullOrEmpty(pattern.BindingVariable))
                Line($"const {SanitizeName(pattern.BindingVariable)} = {subject};");

            // `Case <pattern> When <guard>` — the guard narrows the arm further.
            if (pattern.WhenGuard != null)
                test = $"({test} && {ExprInline(pattern.WhenGuard)})";

            return test;
        }

        private static string ComparisonPatternOperator(string op)
        {
            switch (op)
            {
                case ">": return ">";
                case "<": return "<";
                case ">=": return ">=";
                case "<=": return "<=";
                case "=": return "===";
                case "<>": return "!==";
                default:
                    throw NotYet($"Select Case comparison operator '{op}'");
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

            // For Each: fall-through and Exit For are the same branch to the same block, so
            // only POSITION separates them — inside a conditional arm it is an exit, on the
            // straight-line path it is the end of an iteration and needs no statement.
            if (_forEachEnds.Contains(br.Target))
            {
                if (_armDepth > 0) Line("break;");
                return;
            }

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
        // VariableRef, not SanitizeName: an assignment whose target is an unqualified class
        // member must become `this.X = …`, or it writes a fresh global instead.
        public void Visit(IRAssignment assignment) =>
            Line($"{VariableRef(assignment.Target)} = {Expr(assignment.Value)};");
        // A load produces no statement — Expr renders it at the use site.
        public void Visit(IRLoad load) { }

        public void Visit(IRStore store)
        {
            // The destination is an L-VALUE expression, not a previously-bound temp.
            Line($"{Expr(store.Address)} = {Expr(store.Value)};");
        }

        /// <summary>
        /// ⛔ Indices NEST as <c>[i][j]</c>. Joining them <c>[i, j]</c> — what the C# backend
        /// emits for its own target — is VALID JavaScript: the comma operator discards every
        /// index but the last and evaluates to <c>a[j]</c>. No syntax error, no exception, just
        /// a silently wrong element.
        /// </summary>
        private string ElementAccess(IRGetElementPtr gep)
        {
            var sb = new StringBuilder(Expr(gep.BasePointer));
            foreach (var index in gep.Indices ?? new List<IRValue>())
                sb.Append('[').Append(Expr(index)).Append(']');
            return sb.ToString();
        }
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

            var rendered = call.Arguments.ConvertAll(Expr);

            // String builtins arrive as a BARE FunctionName, which CallTarget would pass
            // straight through as if it were a user function — emitting `Len(s)`, a call to
            // something that exists nowhere in JavaScript. They also cannot be expressed as a
            // renamed callee, since `Len(s)` becomes the MEMBER expression `s.length`, so they
            // are rendered whole here.
            if (TryStringBuiltin(call.FunctionName, rendered, out var builtin))
            {
                if (IsUsed(call)) Bind(call.Name, builtin);
                else Line($"{builtin};");
                return;
            }

            var args = string.Join(", ", rendered);
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
        // The array was already allocated at its DECLARATION, where the size lives; this node
        // carries Size == 1 and nothing useful. The C# backend skips it for the same reason.
        public void Visit(IRAlloca alloca) { }

        // Emits nothing: the access is rendered inline by Expr, as an L-value. See the
        // IRGetElementPtr arm there for why binding it to a temp drops writes.
        public void Visit(IRGetElementPtr gep) { }
        public void Visit(IRCast cast) => throw NotYet(nameof(IRCast));
        public void Visit(IRCompare compare) => Bind(compare.Name, CompareExpr(compare));
        public void Visit(IRSwitch switchInst) => throw NotYet(nameof(IRSwitch));
        public void Visit(IRLabel label) => throw NotYet(nameof(IRLabel));
        public void Visit(IRComment comment) => throw NotYet(nameof(IRComment));
        public void Visit(IRArrayAlloc arrayAlloc) => throw NotYet(nameof(IRArrayAlloc));
        public void Visit(IRArrayStore arrayStore) => throw NotYet(nameof(IRArrayStore));
        public void Visit(IRAwait awaitInst) => throw NotYet(nameof(IRAwait));
        public void Visit(IRYield yieldInst) => throw NotYet(nameof(IRYield));
        // Each of these is emitted into block.Instructions AND handed back as the expression
        // result, so the statement and expression arms must agree: bind when something reads
        // the value, otherwise emit it as a bare statement so side effects still happen.
        public void Visit(IRNewObject newObj) => EmitValueOrStatement(newObj, Expr(newObj));
        public void Visit(IRInstanceMethodCall methodCall) => EmitValueOrStatement(methodCall, InstanceCall(methodCall));
        public void Visit(IRBaseMethodCall baseCall) => EmitValueOrStatement(baseCall, Expr(baseCall));

        private void EmitValueOrStatement(IRValue value, string expression)
        {
            if (IsUsed(value)) Bind(value.Name, expression);
            else Line($"{expression};");
        }
        /// <summary>
        /// Renders a variable reference, resolving an unqualified CLASS MEMBER to
        /// <c>this.X</c>.
        ///
        /// <para><b>Why this is mandatory.</b> Inside a method, <c>Count = Count + 1</c> does
        /// not produce an IRFieldAccess — IRBuilder's identifier resolution has no arm for
        /// class fields, so it yields a bare IRVariable that is neither a parameter nor a
        /// local. C# and C++ survive on implicit <c>this.</c> resolution; JavaScript does not,
        /// and an ES module is always strict, so the bare name is a ReferenceError at runtime
        /// from a build that reported success.</para>
        ///
        /// <para>Parameters and locals SHADOW members, so they are checked first — rewriting a
        /// shadowed name would silently read the wrong value.</para>
        /// </summary>
        private string VariableRef(IRVariable v)
        {
            var name = SanitizeName(v.Name);

            if (_memberNames.Count == 0) return name;
            if (v.IsGlobal || v.IsParameter) return name;
            if (_declaredNames.Contains(v.Name)) return name;   // parameter or local shadows
            if (!_memberNames.Contains(v.Name)) return name;

            return $"this.{name}";
        }

        /// <summary>
        /// ByRef is re-checked here because IRInstanceMethodCall carries its OWN
        /// ByRefArguments list — a separate channel from IRCall's, which the declaration walk
        /// cannot see for a resolved .NET target.
        /// </summary>
        private string InstanceCall(IRInstanceMethodCall mc)
        {
            if (mc.ByRefArguments != null && mc.ByRefArguments.Contains(true))
                throw JsCapabilityChecker.ByRefArgumentRejection(mc.MethodName);

            var args = string.Join(", ", mc.Arguments.ConvertAll(Expr));
            return $"{Expr(mc.Object)}.{SanitizeName(mc.MethodName)}({args})";
        }

        private static bool IsLengthAccess(IRFieldAccess fa) =>
            string.Equals(fa?.FieldName, "Length", StringComparison.OrdinalIgnoreCase);

        public void Visit(IRFieldAccess fieldAccess)
        {
            // `.Length` is pure and renders inline via Expr — no statement needed.
            if (IsLengthAccess(fieldAccess)) return;

            // A field or property READ is pure too, so it only needs a binding when something
            // consumes it; an unread read emits nothing rather than a stray statement.
            if (IsUsed(fieldAccess)) Bind(fieldAccess.Name, Expr(fieldAccess));
        }
        // Statement-only: it has no Name and can never be an operand.
        public void Visit(IRFieldStore fieldStore) =>
            Line($"{Expr(fieldStore.Object)}.{SanitizeName(fieldStore.FieldName)} = {Expr(fieldStore.Value)};");
        public void Visit(IRTupleElement tupleElement) => throw NotYet(nameof(IRTupleElement));
        public void Visit(IRTryCatch tryCatch) => throw NotYet(nameof(IRTryCatch));
        public void Visit(IRInlineCode inlineCode) => throw NotYet(nameof(IRInlineCode));
        /// <summary>
        /// <c>For Each</c> → <c>for (const v of collection)</c>.
        ///
        /// <para>Unlike every other loop this is a single INSTRUCTION, not a terminator: it
        /// carries BodyBlock and EndBlock itself, with no condition block, no increment block
        /// and no back-edge. The enclosing block continues past it, so the continuation is
        /// emitted here and EndBlock is marked emitted.</para>
        ///
        /// <para><b>Fall-through and <c>Exit For</c> are the same branch.</b> Both emit
        /// <c>IRBranch(EndBlock)</c>, so only POSITION separates them: the body block's own
        /// terminator is the natural end of an iteration, while a branch from any NESTED block
        /// is a real exit and becomes <c>break</c>. One case stays undecidable — a bare
        /// unconditional <c>Exit For</c> as the body's last statement, where the exit IS the
        /// body terminator. It lowers as fall-through, matching the other backends; that is a
        /// known limitation rather than an oversight.</para>
        /// </summary>
        public void Visit(IRForEach forEach)
        {
            var variable = SanitizeName(forEach.VariableName);
            Line($"for (const {variable} of {Expr(forEach.Collection)}) {{");
            _indentLevel++;

            var body = forEach.BodyBlock;
            if (body != null)
            {
                _emitted.Add(body);
                _forEachEnds.Push(forEach.EndBlock);

                // The body starts on the STRAIGHT-LINE path. A branch to EndBlock reached
                // without entering a conditional arm is the natural end of an iteration and
                // emits nothing; one reached from inside an arm is a real `Exit For`.
                var savedDepth = _armDepth;
                _armDepth = 0;

                EmitInstructions(body);
                switch (body.GetTerminator())
                {
                    case IRConditionalBranch cond: EmitConditional(cond); break;
                    case IRBranch br: EmitBranch(br); break;
                    case IRSwitch sw: EmitSelectCase(sw); break;
                }

                _armDepth = savedDepth;
                _forEachEnds.Pop();
            }

            _indentLevel--;
            Line("}");

            EmitStructured(forEach.EndBlock);
        }
        public void Visit(IRIndexerAccess indexer) => throw NotYet(nameof(IRIndexerAccess));
        public void Visit(IRThrow throwInst) => throw NotYet(nameof(IRThrow));
        public void Visit(IRIndexerStore indexerStore) => throw NotYet(nameof(IRIndexerStore));
    }
}
