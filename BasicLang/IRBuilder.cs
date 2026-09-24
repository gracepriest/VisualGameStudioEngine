using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using BasicLang.Compiler.AST;
using BasicLang.Compiler.SemanticAnalysis;

namespace BasicLang.Compiler.IR
{
    /// <summary>
    /// Builds IR from AST with SSA transformation
    /// </summary>
    public class IRBuilder : IASTVisitor
    {
        private readonly SemanticAnalyzer _semanticAnalyzer;
        private IRModule _module;
        private IRFunction _currentFunction;
        private int _lambdaCounter;
        private BasicBlock _currentBlock;
        private readonly Stack<LoopContext> _loopStack;
        private readonly Dictionary<string, Stack<IRVariable>> _variableVersions;
        private readonly Dictionary<string, IRVariable> _globalVariables;

        /// <summary>
        /// Module-level globals by OWNING MODULE and bare name, beside <see cref="_globalVariables"/>
        /// which keys by bare name alone and so can hold only one of two Modules' same-named
        /// globals. This is what a resolved module member reference binds through.
        /// </summary>
        private readonly Dictionary<string, IRVariable> _moduleGlobals;

        /// <summary>
        /// Bare names that more than one Module (the file scope counts as one) declares at
        /// module level, collected up front from the AST. Such a global gets an IR NAME
        /// qualified by its owner — <c>Alpha_Scale</c>, <c>Beta_Scale</c> — so that every backend,
        /// every by-name table in them, and every value the builder renames after its
        /// assignment target (<c>Scale = Scale + 1</c>) stay distinct BY CONSTRUCTION.
        ///
        /// <para>⛔ Measured before, with both declared bare: C++ "redefinition of 'int32_t Value'",
        /// JavaScript refused outright, and MSIL keyed its field table by bare name and SILENTLY
        /// kept the last one — <c>A.GetA()</c> printed B's 2. Renaming in the IR fixes all three
        /// at once and needs no per-backend collision special case, which would also have had to
        /// thread the owner through the renamed-value path or lose writes.</para>
        ///
        /// <para>⚠ Single-unit only: two FILES each declaring <c>Scale</c> meet only in
        /// CombineIRModules, after each unit's IR is built. That case keeps today's behaviour
        /// (the key is qualified, the names are not), except that MSIL now refuses it loudly
        /// instead of overwriting.</para>
        /// </summary>
        private readonly HashSet<string> _sharedGlobalNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, IRAlloca> _locals;
        private string _currentClassName;
        private string _currentNamespace;
        private string _currentModuleName;  // Track current module for constants/globals
        private string _sourceFilePath;
        private List<IRValue> _pendingBaseConstructorArgs;  // Temporary storage for base constructor args

        // For SSA construction
        private int _nextVersion = 0;

        // For unique block naming
        private int _ifCounter = 0;
        private int _switchCounter = 0;
        private int _forCounter = 0;
        private int _foreachCounter = 0;
        private int _whileCounter = 0;
        private int _doCounter = 0;
        private int _tryCounter = 0;

        // Flag to suppress instruction emission (used for When guard expressions)
        private bool _suppressEmit = false;

        // Current source line being processed (set from AST nodes)
        private int _currentSourceLine = 0;

        public IRModule Module => _module;

        public IRBuilder(SemanticAnalyzer semanticAnalyzer)
        {
            _semanticAnalyzer = semanticAnalyzer;
            _loopStack = new Stack<LoopContext>();
            _variableVersions = new Dictionary<string, Stack<IRVariable>>();
            _globalVariables = new Dictionary<string, IRVariable>();
            _moduleGlobals = new Dictionary<string, IRVariable>(StringComparer.OrdinalIgnoreCase);
            _locals = new Dictionary<string, IRAlloca>();
        }

        /// <summary>
        /// Build IR from program AST
        /// </summary>
        public IRModule Build(ProgramNode program, string moduleName = "main", string sourceFilePath = null)
        {
            _sourceFilePath = sourceFilePath;
            _module = new IRModule(moduleName);
            _currentFunction = null;
            _currentBlock = null;

            CollectSharedModuleGlobalNames(program);

            program.Accept(this);

            CanonicaliseMemberNames();

            SeparateTempsFromUserNames();

            return _module;
        }

        /// <summary>
        /// Renames any compiler temp whose <c>t{N}</c> name is also a USER name — a local,
        /// parameter, global, field, property, method or function the program declares.
        ///
        /// <para>⛔ Temps come from <see cref="IRFunction.GetNextTempName"/> as <c>t0, t1, ...</c>,
        /// while a store into a variable is the value itself renamed to the variable
        /// (<see cref="TryRenameToVariable"/>). Nothing kept the two apart, so a program with a
        /// local named <c>t1</c> gave the optimizer and the backends two unrelated values sharing
        /// one name. MEASURED on master 883fb1d, all silent:
        /// <c>Dim t1 = n + 1 : Dim t2 = n + 1 : Dim t3 = 6 \ 2 : Return t1 + t2 + t3</c>
        /// returned 10 for n = 4, not 13 — CSE merged the store to t2 into t1's, constant folding
        /// deleted the store to t3, and the C# backend emitted <c>t3 = t1 + t2</c> because the
        /// return's temp was also called t3. With Singles, the <c>Console.WriteLine</c> call's temp
        /// was called t4 and C# emitted <c>t4 = Console.WriteLine(...)</c>.</para>
        ///
        /// <para>Renaming the TEMP, never the user's name, keeps every emitted identifier the user
        /// wrote. A program without a temp-shaped name is left exactly as it was: the reserved set
        /// is empty and this returns before touching anything.</para>
        /// </summary>
        private void SeparateTempsFromUserNames()
        {
            var reserved = IRTempNames.UserOwned(_module);
            if (reserved.Count == 0) return;

            foreach (var fn in IRTempNames.AllFunctions(_module))
            {
                // Every value reachable from the body: instructions and their operand trees.
                var values = new List<IRValue>();
                var seen = new HashSet<IRInstruction>();
                var pending = new Stack<IRInstruction>(fn.Blocks.SelectMany(b => b.Instructions).Reverse());
                while (pending.Count > 0)
                {
                    var inst = pending.Pop();
                    if (inst == null || !seen.Add(inst)) continue;
                    if (inst is IRValue v) values.Add(v);
                    foreach (var operand in CodeGen.IROperandWalker.EnumerateOperands(inst))
                        pending.Push(operand);
                }

                var taken = new HashSet<string>(reserved, StringComparer.OrdinalIgnoreCase);
                foreach (var v in values)
                    if (v.Name != null) taken.Add(v.Name);

                foreach (var v in values)
                {
                    if (v is IRVariable || v is IRConstant || v.NamedAfterVariable) continue;
                    if (v.Name == null || !reserved.Contains(v.Name)) continue;

                    string fresh;
                    do { fresh = fn.GetNextTempName(); } while (taken.Contains(fresh));
                    taken.Add(fresh);
                    v.Name = fresh;
                }
            }
        }

        /// <summary>See <see cref="_sharedGlobalNames"/>.</summary>
        private void CollectSharedModuleGlobalNames(ProgramNode program)
        {
            _sharedGlobalNames.Clear();
            var owners = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

            void Record(string name, string owner)
            {
                if (string.IsNullOrEmpty(name)) return;
                if (!owners.TryGetValue(name, out var set))
                    owners[name] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                set.Add(owner ?? _module?.Name ?? "");
            }

            void Walk(ASTNode node, string owner)
            {
                switch (node)
                {
                    case ModuleNode module:
                        foreach (var m in module.Members) Walk(m, module.Name);
                        break;
                    case NamespaceNode ns:
                        foreach (var m in ns.Members) Walk(m, owner);
                        break;
                    case VariableDeclarationNode v: Record(v.Name, owner); break;
                    case ConstantDeclarationNode c: Record(c.Name, owner); break;
                    // Procedures too: `Module A / Function F` and `Module B / Function F` used
                    // to collapse into one in CombineIRModules' bare-name dedupe.
                    case FunctionNode f: Record(f.Name, owner); break;
                    case SubroutineNode s: Record(s.Name, owner); break;
                }
            }

            foreach (var decl in program?.Declarations ?? new List<ASTNode>())
                Walk(decl, null);

            foreach (var kv in owners)
                if (kv.Value.Count > 1) _sharedGlobalNames.Add(kv.Key);
        }

        /// <summary>The IR name of module-level <paramref name="name"/> owned by <paramref name="module"/>.</summary>
        private string GlobalIrName(string module, string name) =>
            _sharedGlobalNames.Contains(name) && !string.IsNullOrEmpty(module) ? $"{module}_{name}" : name;

        private static string ModuleGlobalKey(string module, string name) => $"{module}\0{name}";

        /// <summary>
        /// The IR name of a module-level or file-scope procedure being DECLARED: owner-qualified
        /// when another Module declares the same name, bare otherwise. A class member keeps its
        /// bare name — its identity is its class, and the member-body exemption in
        /// <c>CombineIRModules</c> already keeps same-named methods of different classes apart.
        /// </summary>
        private string ProcedureIrName(string name) =>
            _currentClassName != null ? name : GlobalIrName(_currentModuleName ?? _module?.Name, name);

        /// <summary>
        /// The (IR name, owning module) a call to <paramref name="callee"/> is emitted with.
        ///
        /// <para>⛔ A cross-unit import used to be spelled as the DOTTED <c>"Helpers.Twice"</c>,
        /// and that was honoured by C++ alone (see <see cref="IRCall.CalleeModule"/>). Every
        /// module procedure now goes out under its bare or owner-qualified IR name with the
        /// owner carried beside it — one wire form for a same-unit call, a cross-unit call and a
        /// qualified call alike.</para>
        /// </summary>
        private (string irName, string calleeModule) ProcedureCallTarget(Symbol callee, string writtenName)
        {
            if (callee == null) return (writtenName, null);
            var isProcedure = callee.Kind == SymbolKind.Function || callee.Kind == SymbolKind.Subroutine;
            if (isProcedure && !string.IsNullOrEmpty(callee.OwningModule))
                return (GlobalIrName(callee.OwningModule, callee.Name), callee.OwningModule);
            if (callee.IsImported && !string.IsNullOrEmpty(callee.SourceModule))
                return (callee.Name, callee.SourceModule);
            if (isProcedure && IsFileScopeProcedure(callee))
                return (GlobalIrName(_module.Name, callee.Name), _module.Name);
            return (callee.Name, null);
        }

        /// <summary>
        /// Whether <paramref name="callee"/> is a procedure declared at the FILE scope of this
        /// unit: its owner is the file's own module, and it is spelled under that owner exactly
        /// as a Module's procedure is under its Module.
        ///
        /// <para>⛔ A file-scope callee used to go out with NO owner, so the one backend that
        /// keeps each module in its own static class (C#) could not qualify it: <c>Twice(4)</c>
        /// from a class body, a constructor, a Shared method, a lambda or a <c>Module</c> block
        /// was CS0103 — measured — while the three flattening backends ran it. And its IR name
        /// was the BARE one even when <see cref="ProcedureIrName"/> had declared it owner-
        /// qualified (a file-scope <c>F</c> beside <c>Module A</c>'s <c>F</c>): a call to a
        /// function that no backend defined, broken on all four.</para>
        ///
        /// <para>⚠ What is NOT file scope, each measured: a stdlib procedure (registered at line
        /// 0 — its IR name must stay the one the backends' tables know); a <c>Declare</c>; and —
        /// the case that needs the class lookup — a method of the class being built or of a
        /// base, whichever symbol the analyzer bound the bare name to. Pass 1 flattens every
        /// method signature into the global scope by bare name, first wins, so a method declared
        /// BELOW its caller, or sharing a name with a file-scope function declared above the
        /// class, arrives here bound to a global-scope symbol; every backend resolves the bare
        /// spelling to the member (the probe printed the member's 3, not the function's 100, on
        /// all four), and this keeps that so. A Module's procedure and an import never reach
        /// here: <see cref="ProcedureCallTarget"/> answers those first.</para>
        ///
        /// <para>⚠ A check on the symbol's DECLARING SCOPE was here and is gone: it survived
        /// mutation. Every class-scope symbol the class lookup already excludes, and every
        /// module-scope one carries its owner, so nothing it refused ever reached it.</para>
        /// </summary>
        private bool IsFileScopeProcedure(Symbol callee)
        {
            if (callee.IsExtern) return false;
            if (callee.Line == 0 && callee.Column == 0) return false;
            return !IsCurrentClassProcedure(callee.Name);
        }

        /// <summary>
        /// Whether the class whose member is being built, or one of its bases, declares a
        /// method or Sub named <paramref name="name"/>. Read from the analyzer's class type,
        /// which is complete for every member once analysis has run — the IR class lists only
        /// the methods built so far, and a method declared below its caller is not among them.
        /// </summary>
        private bool IsCurrentClassProcedure(string name)
        {
            if (string.IsNullOrEmpty(_currentClassName) || string.IsNullOrEmpty(name)) return false;

            var guard = 0;
            for (var type = _semanticAnalyzer.LookupType(_currentClassName); type != null && guard++ < 64;)
            {
                if (type.Members != null && type.Members.TryGetValue(name, out var member) && member != null
                    && (member.Kind == SymbolKind.Function || member.Kind == SymbolKind.Subroutine))
                    return true;

                var baseType = type.BaseType;
                type = baseType == null ? null : (_semanticAnalyzer.LookupType(baseType.Name) ?? baseType);
            }
            return false;
        }

        /// <summary>
        /// The global that a resolved module member reference binds to: the declared one when
        /// its declaration has been visited, else a forward reference carrying the same IR name
        /// and owner — which is all any backend spells it by.
        /// </summary>
        private IRVariable GlobalReference(string name, string module, TypeInfo type)
        {
            if (_moduleGlobals.TryGetValue(ModuleGlobalKey(module, name), out var declared))
                return declared;

            return new IRVariable(GlobalIrName(module, name), type)
            {
                IsGlobal = true,
                ModuleName = module
            };
        }

        /// <summary>The module member a node resolved to, or null for anything else.</summary>
        private Symbol ModuleMemberSymbolOf(ASTNode node)
        {
            var symbol = _semanticAnalyzer?.GetNodeSymbol(node);
            return symbol != null && !string.IsNullOrEmpty(symbol.OwningModule)
                && (symbol.Kind == SymbolKind.Variable || symbol.Kind == SymbolKind.Constant)
                ? symbol : null;
        }

        /// <summary>
        /// Rewrites every member reference to the DECLARED spelling of the member it resolved
        /// to, so the call site's casing stops reaching the backends.
        ///
        /// <para><b>The bug this fixes.</b> BasicLang is case-insensitive, so the front end
        /// correctly accepts <c>b.textContent</c> against a field declared
        /// <c>TextContent</c> — and then the raw source lexeme was carried into
        /// <c>IRFieldAccess.FieldName</c> and emitted verbatim. On JavaScript that produced
        /// TWO distinct properties from one declaration, so a write and a read that disagreed
        /// on casing silently missed each other. On C# it surfaced as a late CS1061 pointing
        /// at generated code rather than at the user's source.</para>
        ///
        /// <para><b>Why a post-pass and not the three construction sites.</b> A class may be
        /// declared AFTER the function that uses it, in which case
        /// <c>_module.Classes</c> has no entry yet when the member reference is built.
        /// Running once at the end makes the result independent of source order.</para>
        ///
        /// <para><b>An unresolvable receiver is left completely alone.</b> A <c>::</c> foreign
        /// name, a .NET type, a collection, a generic type parameter — anything this module did
        /// not declare keeps the spelling it already had, because those names are correct as
        /// written and rewriting them would be the very bug this fixes, inverted.</para>
        /// </summary>
        private void CanonicaliseMemberNames()
        {
            if (_module?.Functions == null) return;

            foreach (var function in _module.Functions)
            {
                if (function?.Blocks == null) continue;
                foreach (var block in function.Blocks)
                    CanonicaliseBlock(block);
            }
        }

        private void CanonicaliseBlock(BasicBlock block)
        {
            if (block?.Instructions == null) return;

            foreach (var instruction in block.Instructions)
            {
                switch (instruction)
                {
                    case IRFieldAccess read:
                        read.FieldName = DeclaredMemberName(read.Object?.Type, read.FieldName);
                        break;

                    case IRFieldStore write:
                        write.FieldName = DeclaredMemberName(write.Object?.Type, write.FieldName);
                        break;

                    // Methods need this as much as fields do: `c.bump()` against a declared
                    // `Bump()` emitted `c.bump()`, which is a TypeError in the browser rather
                    // than a wrong value — loud, but still from a build that reported success.
                    // A collection or stdlib receiver is untouched, because DeclaredMemberName
                    // only rewrites types this module declares.
                    case IRInstanceMethodCall call:
                        call.MethodName = DeclaredMemberName(call.Object?.Type, call.MethodName);
                        break;

                    // Nested blocks are NOT entries in function.Blocks — mirrors the recursion
                    // ForeignFeatureChecker does for the same reason.
                    case IRTryCatch tryCatch:
                        CanonicaliseBlock(tryCatch.TryBlock);
                        if (tryCatch.CatchClauses != null)
                            foreach (var clause in tryCatch.CatchClauses)
                                CanonicaliseBlock(clause?.Block);
                        CanonicaliseBlock(tryCatch.FinallyBlock);
                        CanonicaliseBlock(tryCatch.EndBlock);
                        break;

                    case IRForEach forEach:
                        CanonicaliseBlock(forEach.BodyBlock);
                        CanonicaliseBlock(forEach.EndBlock);
                        break;
                }
            }
        }

        /// <summary>
        /// The declared spelling of <paramref name="written"/> on <paramref name="objectType"/>,
        /// or <paramref name="written"/> unchanged when the type or member is not one this
        /// module declares. Walks the base chain, since an inherited member is declared on an
        /// ancestor.
        /// </summary>
        private string DeclaredMemberName(TypeInfo objectType, string written)
        {
            if (string.IsNullOrEmpty(written) || string.IsNullOrEmpty(objectType?.Name))
                return written;

            var typeName = objectType.Name;
            var guard = 0;   // a malformed base chain must not spin

            while (!string.IsNullOrEmpty(typeName) && guard++ < 64)
            {
                if (!_module.Classes.TryGetValue(typeName, out var irClass) || irClass == null)
                    return DeclaredMemberNameFromSymbols(objectType, written);

                foreach (var field in irClass.Fields)
                    if (string.Equals(field?.Name, written, StringComparison.OrdinalIgnoreCase))
                        return field.Name;

                foreach (var property in irClass.Properties)
                    if (string.Equals(property?.Name, written, StringComparison.OrdinalIgnoreCase))
                        return property.Name;

                foreach (var method in irClass.Methods)
                    if (string.Equals(method?.Name, written, StringComparison.OrdinalIgnoreCase))
                        return method.Name;

                typeName = irClass.BaseClass;
            }

            return written;
        }

        /// <summary>
        /// The declared spelling from the ANALYZER's member table, for a type this unit did not
        /// declare — a class from another file of the project, or from a shipped <c>.bli</c>.
        ///
        /// <para>⛔ <c>_module.Classes</c> is THIS UNIT's classes, so the walk above cannot see a
        /// type declared elsewhere. MEASURED: <c>el.TextContent</c> against <c>Element</c> from
        /// <c>dom-core.bli</c> was emitted verbatim and printed <c>undefined</c>, while the same
        /// program with <c>Element</c> declared in the same file was canonicalised. The symbol
        /// table is shared across units and its entries carry the declared name.</para>
        /// </summary>
        private static string DeclaredMemberNameFromSymbols(TypeInfo objectType, string written)
        {
            var current = objectType;
            var guard = 0;
            while (current != null && guard++ < 64)
            {
                if (current.Members != null && current.Members.TryGetValue(written, out var symbol) &&
                    !string.IsNullOrEmpty(symbol?.Name))
                    return symbol.Name;
                current = current.BaseType;
            }
            return written;
        }

        private IRVariable CreateVariable(string name, TypeInfo type, int version = 0)
        {
            return new IRVariable(name, type, version);
        }

        /// <summary>
        /// Renames a freshly-built result to the variable it initialises or assigns — the IR
        /// then carries no separate IRAssignment — and MARKS it (<see cref="IRValue.NamedAfterVariable"/>),
        /// which is how a backend tells that result from a temp that merely shares the name.
        /// False when the value must flow through an IRAssignment instead: constants, variables,
        /// and a `::`-qualified FOREIGN call — its result is an opaque pseudo-type with no
        /// declarable C++ temp, and a renamed foreign call is emitted as a bare statement that
        /// DROPS the assignment (the declaration path always guarded this; the assignment path
        /// did not, and `v = ::next_id()` left v untouched on C++ — found by review).
        /// </summary>
        private static bool TryRenameToVariable(IRValue value, IRVariable target)
        {
            switch (value)
            {
                case IRCall call when call.Type?.Kind == TypeKind.Foreign:
                    return false;
                case IRCall:
                case IRAwait:
                case IRBinaryOp:
                case IRUnaryOp:
                case IRCompare:
                    value.Name = target.Name;
                    value.NamedAfterVariable = true;
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Whether <paramref name="name"/> is a field or property of the class whose member is
        /// being built (or of a base). Class scope is NEARER than module scope, so an unqualified
        /// name inside a method is the member before it is a same-named module-level Dim.
        /// </summary>
        private bool IsCurrentClassMember(string name)
        {
            if (string.IsNullOrEmpty(_currentClassName) || _module?.Classes == null) return false;

            var typeName = _currentClassName;
            var guard = 0;
            while (!string.IsNullOrEmpty(typeName) && guard++ < 64 &&
                   _module.Classes.TryGetValue(typeName, out var cls) && cls != null)
            {
                if (cls.Fields.Any(f => string.Equals(f?.Name, name, StringComparison.OrdinalIgnoreCase)) ||
                    cls.Properties.Any(p => string.Equals(p?.Name, name, StringComparison.OrdinalIgnoreCase)))
                    return true;
                typeName = cls.BaseClass;
            }
            return false;
        }

        private IRVariable GetOrCreateVariable(string name, TypeInfo type)
        {
            // Check for existing version
            if (_variableVersions.ContainsKey(name) && _variableVersions[name].Count > 0)
            {
                return _variableVersions[name].Peek();
            }

            // Check global — unless the name is a member of the class being built: found by
            // review, `Total = Total + n` inside Counter.Add resolved to a module-level `Total`
            // and mutated the global while the field stayed 0.
            //
            // The CURRENT Module's copy first: _globalVariables is bare-keyed and holds whichever
            // same-named global was declared last, which is the wrong one from inside the other.
            if (!IsCurrentClassMember(name))
            {
                if (_moduleGlobals.TryGetValue(ModuleGlobalKey(_currentModuleName ?? _module?.Name, name), out var own))
                    return own;
                if (_globalVariables.ContainsKey(name))
                    return _globalVariables[name];
            }

            // Create new version
            var variable = CreateVariable(name, type, _nextVersion++);

            if (!_variableVersions.ContainsKey(name))
            {
                _variableVersions[name] = new Stack<IRVariable>();
            }

            _variableVersions[name].Push(variable);

            return variable;
        }

        private void PushVariableVersion(string name, IRVariable variable)
        {
            if (!_variableVersions.ContainsKey(name))
            {
                _variableVersions[name] = new Stack<IRVariable>();
            }
            _variableVersions[name].Push(variable);
        }

        private void PopVariableVersion(string name)
        {
            if (_variableVersions.ContainsKey(name) && _variableVersions[name].Count > 0)
            {
                _variableVersions[name].Pop();
            }
        }

        private void EmitInstruction(IRInstruction instruction)
        {
            // Skip emission when building When guard expressions to prevent optimization passes
            // from modifying them (they're not part of block control flow)
            if (_suppressEmit) return;

            // Stamp source line from current AST context
            if (_currentSourceLine > 0 && instruction.SourceLine == 0)
            {
                instruction.SourceLine = _currentSourceLine;
            }

            if (_currentBlock != null)
            {
                _currentBlock.AddInstruction(instruction);
            }
        }

        /// <summary>
        /// Track the source line from an AST node before emitting instructions
        /// </summary>
        private void TrackSourceLine(ASTNode node)
        {
            if (node != null && node.Line > 0)
            {
                _currentSourceLine = node.Line;
            }
        }

        // ====================================================================
        // Program Structure
        // ====================================================================

        public void Visit(ProgramNode node)
        {
            foreach (var declaration in node.Declarations)
            {
                declaration.Accept(this);
            }
        }

        public void Visit(NamespaceNode node)
        {
            // Save current namespace
            var savedNamespace = _currentNamespace;

            // Set current namespace (support nested namespaces)
            _currentNamespace = _currentNamespace != null
                ? $"{_currentNamespace}.{node.Name}"
                : node.Name;

            // Track namespace in module
            if (!_module.Namespaces.Contains(_currentNamespace))
            {
                _module.Namespaces.Add(_currentNamespace);
            }

            // Process members
            foreach (var member in node.Members)
            {
                member.Accept(this);
            }

            // Restore namespace
            _currentNamespace = savedNamespace;
        }

        public void Visit(ModuleNode node)
        {
            // Track current module name for constants/globals
            var savedModuleName = _currentModuleName;
            _currentModuleName = node.Name;

            // Modules are organizational - process members
            foreach (var member in node.Members)
            {
                member.Accept(this);
            }

            _currentModuleName = savedModuleName;
        }

        public void Visit(UsingDirectiveNode node)
        {
            // If this is a .NET namespace, add it to the module's NetUsings
            if (node.IsNetNamespace)
            {
                _module.NetUsings.Add(new NetUsingDirective(node.Namespace, node.Alias));
            }
            // BasicLang module usings don't need IR generation
        }

        public void Visit(ImportDirectiveNode node)
        {
            // No IR generation needed
        }

        // ====================================================================
        // Functions and Subroutines
        // ====================================================================

        public void Visit(FunctionNode node)
        {
            var returnType = _semanticAnalyzer.GetNodeType(node) ?? new TypeInfo("Void", TypeKind.Void);

            _currentFunction = _module.CreateFunction(ProcedureIrName(node.Name), returnType);

            // Set module name for multi-file compilation
            _currentFunction.ModuleName = _currentModuleName ?? _module.Name;
            _currentFunction.SourceFilePath = _sourceFilePath;

            // Set access modifier (convert from AST to IR enum)
            _currentFunction.Access = (IR.AccessModifier)(int)node.Access;

            // Set async/iterator flags
            _currentFunction.IsAsync = node.IsAsync;
            _currentFunction.IsIterator = node.IsIterator;

            // Copy generic parameters and constraints
            foreach (var genericParam in node.GenericParameters)
            {
                _currentFunction.GenericParameters.Add(genericParam);
            }
            if (node.GenericTypeParams != null)
            {
                foreach (var typeParam in node.GenericTypeParams)
                {
                    _currentFunction.GenericTypeParams.Add(typeParam);
                }
            }

            // Create parameters
            foreach (var param in node.Parameters)
            {
                var paramType = _semanticAnalyzer.GetNodeType(param);
                var irParam = new IRVariable(param.Name, paramType)
                {
                    IsParameter = true,
                    IsOptional = param.IsOptional,
                    IsParamArray = param.IsParamArray,
                    IsByRef = param.IsByRef,
                    DefaultValue = BuildExpressionValue(param.DefaultValue)
                };
                _currentFunction.Parameters.Add(irParam);
                PushVariableVersion(param.Name, irParam);
            }

            // Create entry block
            _currentBlock = _currentFunction.CreateBlock("entry");

            // Process body
            if (node.Body != null)
            {
                node.Body.Accept(this);
            }

            // Ensure function ends with return
            if (!_currentBlock.IsTerminated())
            {
                if (returnType.Name == "Void")
                {
                    EmitInstruction(new IRReturn());
                }
                else
                {
                    // Return default value
                    var defaultValue = CreateDefaultValue(returnType);
                    EmitInstruction(new IRReturn(defaultValue));
                }
            }

            // Clean up variable versions
            foreach (var param in node.Parameters)
            {
                PopVariableVersion(param.Name);
            }

            _currentFunction = null;
            _currentBlock = null;
        }

        public void Visit(SubroutineNode node)
        {
            var voidType = new TypeInfo("Void", TypeKind.Void);

            _currentFunction = _module.CreateFunction(ProcedureIrName(node.Name), voidType);

            // Set module name for multi-file compilation
            _currentFunction.ModuleName = _currentModuleName ?? _module.Name;
            _currentFunction.SourceFilePath = _sourceFilePath;

            // Set access modifier (convert from AST to IR enum)
            _currentFunction.Access = (IR.AccessModifier)(int)node.Access;

            // Set async flag
            _currentFunction.IsAsync = node.IsAsync;

            // Copy generic parameters and constraints
            foreach (var genericParam in node.GenericParameters)
            {
                _currentFunction.GenericParameters.Add(genericParam);
            }
            if (node.GenericTypeParams != null)
            {
                foreach (var typeParam in node.GenericTypeParams)
                {
                    _currentFunction.GenericTypeParams.Add(typeParam);
                }
            }

            // Create parameters
            foreach (var param in node.Parameters)
            {
                var paramType = _semanticAnalyzer.GetNodeType(param);
                var irParam = new IRVariable(param.Name, paramType)
                {
                    IsParameter = true,
                    IsOptional = param.IsOptional,
                    IsParamArray = param.IsParamArray,
                    IsByRef = param.IsByRef,
                    DefaultValue = BuildExpressionValue(param.DefaultValue)
                };
                _currentFunction.Parameters.Add(irParam);
                PushVariableVersion(param.Name, irParam);
            }

            // Create entry block
            _currentBlock = _currentFunction.CreateBlock("entry");

            // Process body
            if (node.Body != null)
            {
                node.Body.Accept(this);
            }

            // Ensure function ends with return
            if (!_currentBlock.IsTerminated())
            {
                EmitInstruction(new IRReturn());
            }

            // Clean up variable versions
            foreach (var param in node.Parameters)
            {
                PopVariableVersion(param.Name);
            }

            _currentFunction = null;
            _currentBlock = null;
        }

        public void Visit(ParameterNode node)
        {
            // Handled in function visit
        }

        // ====================================================================
        // Declarations
        // ====================================================================

        public void Visit(VariableDeclarationNode node)
        {
            TrackSourceLine(node);
            var varType = _semanticAnalyzer.GetNodeType(node) ?? new TypeInfo("Object", TypeKind.Class);

            if (_currentFunction == null)
            {
                // Global variable
                // Built and registered in two steps rather than via CreateGlobalVariable:
                // AddGlobalVariable keys on ModuleName when the bare name collides, so the
                // owning module has to be set BEFORE it is registered or two Modules' globals
                // are attributed to the same key and one is lost.
                var owningModule = _currentModuleName ?? _module?.Name;
                var globalVar = new IRVariable(GlobalIrName(owningModule, node.Name), varType) { IsGlobal = true };
                globalVar.ModuleName = owningModule;
                globalVar.Access = MapAccessModifier(node.Access);
                _module.AddGlobalVariable(globalVar);
                _globalVariables[node.Name] = globalVar;
                _moduleGlobals[ModuleGlobalKey(owningModule, node.Name)] = globalVar;

                if (node.Initializer != null)
                {
                    globalVar.InitialValue = BuildModuleScopeInitializer(
                        node.Initializer, node.Name, "variable", varType);
                }
            }
            else
            {
                // Local variable - register it for declaration
                var localVar = CreateVariable(node.Name, varType, _nextVersion++);
                // Record when the type was INFERRED (`Dim x = expr` / `Auto x = expr`, no
                // `As` clause) rather than explicitly declared. The C++ backend uses this to
                // emit `auto` for opaque foreign initializers whose inferred type is a
                // synthetic member-path pseudo-type (not a real C++ type).
                localVar.IsInferredType = node.IsAuto;
                PushVariableVersion(node.Name, localVar);
                _currentFunction.LocalVariables.Add(localVar);

                // Only emit IRAlloca for variables that need memory semantics
                // (arrays, ByRef parameters, address-of operations)
                bool needsMemory = varType.Kind == TypeKind.Array;

                if (needsMemory)
                {
                    var alloca = new IRAlloca($"{node.Name}_addr", varType);
                    EmitInstruction(alloca);
                }

                // Initialize if there's an initializer
                if (node.Initializer != null)
                {
                    node.Initializer.Accept(this);

                    // ⚠ `varType` and NOT `initValue.Type`: the point is the DECLARED type.
                    // `Dim d As Integer = 7 / 2` carries a Double, and without this the rename
                    // below makes the Double temp *be* `d`, so the Integer is never honoured.
                    // An inferred declaration (`Dim x = expr`, node.IsAuto) has no declared type
                    // to disagree with — varType IS the initializer's type, so this no-ops.
                    var initValue = CoerceToDeclaredType(_expressionResult, varType);

                    // For memory-backed variables, emit a store
                    if (needsMemory)
                    {
                        var alloca = _currentBlock.Instructions
                            .OfType<IRAlloca>()
                            .LastOrDefault(a => a.Name == $"{node.Name}_addr");
                        if (alloca != null)
                        {
                            EmitInstruction(new IRStore(initValue, alloca));
                        }
                    }

                    // Optimization: If the value is a direct call or op result,
                    // rename it to the variable instead of creating a separate assignment.
                    // EXCEPT a ::-qualified foreign C++ free-function call: its result is an
                    // opaque Foreign pseudo-type with no declarable C++ temp, so it must flow
                    // through an IRAssignment (like a foreign field read) — the C++ backend
                    // then folds declaration + init into `auto x = ns::f(args);` and renders
                    // the call inline. Renaming it to the local would emit a bare statement
                    // that DROPS the assignment.
                    if (!TryRenameToVariable(initValue, localVar))
                    {
                        // For constants, variables, or other values, emit an assignment
                        EmitInstruction(new IRAssignment(localVar, initValue));
                    }
                }
                // No initializer - C# backend will use default(T), no IR needed
            }
        }

        public void Visit(TupleDeconstructionNode node)
        {
            TrackSourceLine(node);
            // Evaluate the tuple expression
            node.Initializer.Accept(this);
            var tupleValue = _expressionResult;

            // Get the tuple type
            var tupleType = _semanticAnalyzer.GetNodeType(node.Initializer);

            // Create local variables for each element and emit deconstruction
            for (int i = 0; i < node.Variables.Count; i++)
            {
                var (varName, varTypeRef) = node.Variables[i];

                // Determine the type - from explicit type or inferred from tuple
                TypeInfo varType;
                if (varTypeRef != null)
                {
                    varType = new TypeInfo(varTypeRef.Name, TypeKind.Primitive);
                }
                else if (tupleType?.TupleElementTypes != null && i < tupleType.TupleElementTypes.Count)
                {
                    varType = tupleType.TupleElementTypes[i];
                }
                else
                {
                    varType = new TypeInfo("Object", TypeKind.Class);
                }

                // Create the local variable
                var localVar = CreateVariable(varName, varType, _nextVersion++);
                PushVariableVersion(varName, localVar);
                _currentFunction.LocalVariables.Add(localVar);

                // Emit instruction to get tuple element
                var elementAccess = new IRTupleElement(tupleValue, i, varType)
                {
                    Name = localVar.Name
                };
                EmitInstruction(elementAccess);
            }
        }

        public void Visit(ConstantDeclarationNode node)
        {
            TrackSourceLine(node);
            // Store constants as global variables with IsConst = true
            if (node.Value != null)
            {
                // ⚠ Evaluated HERE only for the LOCAL case. A module-scope Const takes the
                // folding path below instead: this line lowers the expression through
                // _currentFunction, which is null at module scope, and that is the same crash
                // the global Dim branch had.
                IRValue value = null;
                if (_currentFunction != null)
                {
                    node.Value.Accept(this);
                    value = _expressionResult;
                }

                // Resolve the type
                var typeName = node.Type?.Name ?? "Integer";
                var typeInfo = _semanticAnalyzer.GetNodeType(node) ?? new TypeInfo(typeName, TypeKind.Primitive);

                // A Const declared INSIDE a procedure is local to that procedure, exactly like
                // a local Dim. Hoisting it into the module's flat, unqualified, name-keyed
                // global table silently MERGED distinct constants: the write below is
                // first-wins, so two procedures each declaring `Const Scale` collapsed into
                // one, and the second procedure read the first one's value. The build
                // succeeded and the emitted code compiled — it just computed the wrong
                // answer.
                //
                // Mirrors the local branch of Visit(VariableDeclarationNode): register the
                // local, then emit an assignment. A Const's value is a constant expression,
                // which is precisely the case that path materializes via IRAssignment.
                if (_currentFunction != null)
                {
                    var localConst = CreateVariable(node.Name, typeInfo, _nextVersion++);
                    localConst.IsConst = true;
                    PushVariableVersion(node.Name, localConst);
                    _currentFunction.LocalVariables.Add(localConst);
                    // Coerced like a local Dim's initializer. Without it `Const L As Single = 0.5`
                    // stored the Double literal as-is and C# emitted `L = 0.5;` into a float
                    // local — CS0664. (Unreachable before the SemanticAnalyzer accepted a Double
                    // literal for a Single constant; module scope already narrowed via
                    // BuildModuleScopeInitializer.)
                    EmitInstruction(new IRAssignment(localConst, CoerceToDeclaredType(value, typeInfo)));
                    return;
                }

                // Create the constant as a global variable
                var owningModule = _currentModuleName ?? _module?.Name;
                var constVar = new IRVariable(GlobalIrName(owningModule, node.Name), typeInfo)
                {
                    IsGlobal = true,
                    IsConst = true,
                    InitialValue = BuildModuleScopeInitializer(
                        node.Value, node.Name, "constant", typeInfo),
                    ModuleName = owningModule,
                    Access = MapAccessModifier(node.Access)
                };
                // Registered like a Dim so a reference binds to THIS instance rather than
                // minting a fresh local that merely shares the name.
                _globalVariables[node.Name] = constVar;
                _moduleGlobals[ModuleGlobalKey(owningModule, node.Name)] = constVar;

                // Add to the module's globals. This used to be first-wins, on the reasoning
                // that "at module scope a repeated name is a genuine redeclaration" — WRONG.
                // Separate Module blocks are separate namespaces, and this dictionary is
                // shared by all of them, so the second Module's constant was silently dropped
                // while the emitted code still referenced it. AddGlobalVariable keeps both by
                // qualifying the key on collision.
                _module?.AddGlobalVariable(constVar);
            }
        }

        /// <summary>
        /// Lowers a MODULE-SCOPE initializer to the single constant that a global's
        /// <see cref="IRVariable.InitialValue"/> is required to be.
        ///
        /// <para>⛔ Both module-scope call sites — a global <c>Dim</c> and a global <c>Const</c> —
        /// used to call <c>node.Initializer.Accept(this)</c> directly, with
        /// <c>_currentFunction</c> null by definition of the branch they sit in. Expression
        /// lowering names its temps through <c>_currentFunction.GetNextTempName()</c>, so EVERY
        /// initializer needing a temp dereferenced null and the compiler died with
        /// <c>Error at line 0: Object reference not set to an instance of an object</c>.
        /// Measured, the crashing set was wide — <c>40 + 2</c>, <c>"a" &amp; "b"</c>,
        /// <c>7 / 2</c>, <c>1 &lt; 2</c>, <c>(1 + 2) * 3</c>, <c>Helper()</c>,
        /// <c>New List(Of Integer)()</c> — and identical on all four backends, because it
        /// happened in the builder before any of them ran.</para>
        ///
        /// <para>⚠ A scratch function gives that lowering somewhere to put its instructions, and
        /// the optimizer's own <c>ConstantFoldingPass</c> then reduces them. Folding has to
        /// happen HERE and not in the optimizer: a global's initializer must be a constant for
        /// the backends to emit it at all, and the optimizer does not run on every path (the
        /// non-optimizing test helper, <c>--O0</c>). An invariant the backends depend on cannot
        /// be established by a pass that is sometimes skipped.</para>
        ///
        /// <para>⚠ The scratch function is deliberately NOT created through
        /// <c>_module.CreateFunction</c>: that registers it, and every backend would emit a
        /// stray function per initialized global.</para>
        ///
        /// <para>⛔ KNOWN GAP, measured rather than assumed: integer-literal division promoted
        /// to Double (<c>Dim G As Double = 7 / 2</c>) is refused although it is arithmetically
        /// constant. <c>/</c> promotes both operands, so the block is
        /// <c>IRCast, IRCast, IRBinaryOp</c> and <c>ConstantFoldingPass</c> does not fold a cast
        /// — the operands never become <c>IRConstant</c>, so neither does the division.
        /// <c>7.0 / 2.0</c> and <c>8 \ 2</c> both fold. Closing it means folding a cast of a
        /// constant, which is a NUMERIC-CONVERSION change and not a crash fix: widening is
        /// lossless, but narrowing has to agree with what the backends emit at run time, and VB's
        /// <c>CInt</c> rounds half-to-even where a C# cast truncates. That deserves its own
        /// characterization across the four backends rather than a ride-along here.</para>
        ///
        /// <para>⛔ What does not fold is REFUSED, not guessed at. <c>Helper()</c> and
        /// <c>New List(Of Integer)()</c> need code to run before first use, which means a module
        /// initializer no backend has — the JavaScript backend already refuses a non-constant
        /// global outright (<c>"a module-level initializer ... that is not a constant"</c>), and
        /// C# and MSIL would have emitted a temp name that is not in scope. One refusal here,
        /// where foldability is decided, is the only way the builder and the backends cannot
        /// disagree about it.</para>
        /// </summary>
        /// <param name="what">"variable" or "constant" — only to word the diagnostic.</param>
        private IRValue BuildModuleScopeInitializer(
            ExpressionNode initializer, string name, string what, TypeInfo declared)
        {
            var folded = TryFoldInitializerToConstant(initializer, name, declared);
            if (folded != null) return NarrowModuleScopeConstant(folded, declared, name, what);

            throw new Exception(
                $"Line {_currentSourceLine}: the module-level {what} '{name}' has an initializer "
                + "that cannot be computed at compile time. Only a constant expression is "
                + "supported at module scope; assign it in Main (or another procedure) instead.");
        }

        /// <summary>
        /// Lower <paramref name="initializer"/> in a throwaway function and fold it to a single
        /// compile-time value, or null when it is not constant.
        ///
        /// <para>⚠ Shared by the two declaration sites that need an initializer to BE a constant
        /// before any backend sees it: module-scope globals
        /// (<see cref="BuildModuleScopeInitializer"/>) and class fields
        /// (<see cref="BuildConstantFieldInitializer"/>). They differ in what they do when this
        /// returns null, not in what counts as constant — one place to decide foldability is the
        /// only way the two cannot drift apart.</para>
        ///
        /// <para>⚠ A scratch function gives expression lowering somewhere to put its
        /// instructions. Without one, lowering names its temps through
        /// <c>_currentFunction.GetNextTempName()</c>, so every initializer needing a temp
        /// dereferenced null and the compiler died with <c>Error at line 0: Object reference not
        /// set to an instance of an object</c>. It is deliberately NOT created through
        /// <c>_module.CreateFunction</c>: that registers it, and every backend would emit a stray
        /// function per initialized declaration.</para>
        ///
        /// <para>⚠ Folding has to happen HERE and not in the optimizer: the initializer must be a
        /// constant for the backends to emit it at all, and the optimizer does not run on every
        /// path (the non-optimizing test helper, <c>--O0</c>). An invariant the backends depend on
        /// cannot be established by a pass that is sometimes skipped.</para>
        ///
        /// <para>⛔ <c>CInt(...)</c> / <c>CDbl(...)</c> are NOT casts — measured, they lower to an
        /// IRCall, so neither pass touches them and they do not fold. That is a separate gap
        /// (constant-folding the conversion FUNCTIONS) and for CInt it is a welcome one, because
        /// the backends do not agree on what it means.</para>
        /// </summary>
        private IRValue TryFoldInitializerToConstant(
            ExpressionNode initializer, string name, TypeInfo declared)
        {
            var savedFunction = _currentFunction;
            var savedBlock = _currentBlock;

            var scratch = new IRFunction($"<init>{name}", new TypeInfo("Void", TypeKind.Void));
            _currentFunction = scratch;
            _currentBlock = scratch.CreateBlock("entry");

            try
            {
                initializer.Accept(this);

                // ⛔ THE COERCION THE GLOBAL PATH NEVER HAD, and the whole reason a module-scope
                // `Dim v As Integer = 7.9` produced GARBAGE. The LOCAL branch of
                // Visit(VariableDeclarationNode) has always coerced to the DECLARED type; this one
                // stored whatever the initializer happened to carry, so a Double literal went
                // straight into an Integer global and each backend reinterpreted its bits. Measured
                // on MSIL at module scope, `Dim v As T = 7.9` printed: Byte 154, SByte -102, Short
                // and UShort an EMPTY STRING, Integer -1717986918, UInteger 2576980378, Long and
                // ULong 4620580627691444634 — which is the IEEE-754 bit pattern of 7.9 read as an
                // integer. The same declarations as LOCALS printed 7 throughout.
                var lowered = CoerceToDeclaredType(_expressionResult, declared);
                var emitted = _currentBlock.Instructions;

                // Nothing emitted: the expression was already a self-contained value — a literal,
                // or a reference to an already-constant global. This is the shape that always
                // worked, and it must keep taking the value lowering produced rather than anything
                // folded. Note this can be a NON-constant IRValue; each caller decides whether it
                // can use one.
                if (emitted.Count == 0) return lowered;

                var scratchModule = new IRModule("<init>");
                scratchModule.Functions.Add(scratch);

                // ⚠ A FIXPOINT over both passes, because neither order alone is enough, measured
                // on two shapes that need OPPOSITE orders:
                //   `7 / 2`        -- the casts must fold first; until they do, the division's
                //                     operands are IRCast and TryFoldBinary declines them.
                //   `(1 + 2) / 4`  -- the addition must fold first; until it does, the promoting
                //                     cast's operand is an IRBinaryOp rather than a constant.
                // Alternating until nothing changes covers both without caring which it was handed.
                // Bounded so a pass reporting a modification without making progress cannot spin.
                var folding = new Optimization.ConstantFoldingPass();
                var wideningCasts = new Optimization.WideningCastFoldingPass();
                for (var round = 0; round < 16; round++)
                {
                    var changed = folding.Run(scratchModule);
                    changed |= wideningCasts.Run(scratchModule);
                    if (!changed) break;
                }

                // Folding rewrites each instruction in place, so a fully constant expression leaves
                // a list of nothing but constants, and the LAST one is the result: lowering is
                // bottom-up and the outermost operation is emitted last.
                return emitted.All(i => i is IRConstant)
                    ? (IRValue)emitted[emitted.Count - 1]
                    : null;
            }
            finally
            {
                // In a finally because a caller may turn "not constant" into a thrown diagnostic,
                // and the builder's cursor must not be left pointing at the scratch block either
                // way. The original inline version restored before folding; folding reads the
                // scratch MODULE, not the cursor, so restoring after it is equivalent.
                _currentFunction = savedFunction;
                _currentBlock = savedBlock;
            }
        }

        public void Visit(TypeDefineNode node)
        {
            // Type aliases don't generate IR
        }

        // ====================================================================
        // Classes and Types
        // ====================================================================

        public void Visit(ClassNode node)
        {
            // Create IR class structure
            var irClass = new IRClass(node.Name)
            {
                BaseClass = node.BaseClass,
                Namespace = _currentNamespace,
                IsAbstract = node.IsAbstract,
                IsExtern = node.IsExtern
            };

            // Copy generic parameters and constraints
            foreach (var genericParam in node.GenericParameters)
            {
                irClass.GenericParameters.Add(genericParam);
            }
            if (node.GenericTypeParams != null)
            {
                foreach (var typeParam in node.GenericTypeParams)
                {
                    irClass.GenericTypeParams.Add(typeParam);
                }
            }

            foreach (var iface in node.Interfaces)
            {
                irClass.Interfaces.Add(iface);
            }

            _module.Classes[node.Name] = irClass;
            _currentClassName = node.Name;

            // Process members - they will populate the IRClass
            foreach (var member in node.Members)
            {
                if (member is VariableDeclarationNode varDecl)
                {
                    // Add as field
                    var fieldType = _semanticAnalyzer.GetNodeType(varDecl);
                    var field = new IRField
                    {
                        Name = varDecl.Name,
                        Type = fieldType,
                        Access = MapAccessModifier(varDecl.Access),
                        IsStatic = varDecl.IsStatic,
                        Initializer = BuildConstantFieldInitializer(varDecl.Initializer, fieldType, varDecl.Name)
                    };
                    irClass.Fields.Add(field);
                }
                else if (member is FunctionNode funcNode)
                {
                    // Process function and add as method
                    var declaredAt = _module.Functions.Count;
                    member.Accept(this);

                    var method = new IRMethod
                    {
                        Name = funcNode.Name,
                        ReturnType = _semanticAnalyzer.GetNodeType(funcNode),
                        Access = MapAccessModifier(funcNode.Access),
                        IsStatic = funcNode.IsStatic,
                        IsVirtual = funcNode.IsVirtual,
                        IsOverride = funcNode.IsOverride,
                        IsAbstract = funcNode.IsAbstract,
                        IsSealed = funcNode.IsSealed,
                        Implementation = MemberFunctionAt(declaredAt)
                    };
                    // Copy generic parameters
                    foreach (var genericParam in funcNode.GenericParameters)
                    {
                        method.GenericParameters.Add(genericParam);
                    }
                    irClass.Methods.Add(method);
                }
                else if (member is SubroutineNode subNode)
                {
                    // Process subroutine and add as method
                    var declaredAt = _module.Functions.Count;
                    member.Accept(this);

                    var method = new IRMethod
                    {
                        Name = subNode.Name,
                        ReturnType = new TypeInfo("Void", TypeKind.Void),
                        Access = MapAccessModifier(subNode.Access),
                        IsStatic = subNode.IsStatic,
                        IsVirtual = subNode.IsVirtual,
                        IsOverride = subNode.IsOverride,
                        IsAbstract = subNode.IsAbstract,
                        IsSealed = subNode.IsSealed,
                        Implementation = MemberFunctionAt(declaredAt)
                    };
                    // Copy generic parameters
                    foreach (var genericParam in subNode.GenericParameters)
                    {
                        method.GenericParameters.Add(genericParam);
                    }
                    irClass.Methods.Add(method);
                }
                else if (member is ConstructorNode ctorNode)
                {
                    // Process constructor - this also processes base constructor args
                    _pendingBaseConstructorArgs = null;
                    var declaredAt = _module.Functions.Count;
                    member.Accept(this);

                    var ctor = new IRConstructor
                    {
                        Access = MapAccessModifier(ctorNode.Access),
                        Implementation = MemberFunctionAt(declaredAt)
                    };

                    // Use the base constructor args collected during constructor processing
                    if (_pendingBaseConstructorArgs != null)
                    {
                        ctor.BaseConstructorArgs.AddRange(_pendingBaseConstructorArgs);
                    }
                    _pendingBaseConstructorArgs = null;

                    irClass.Constructors.Add(ctor);
                }
                else if (member is PropertyNode propNode)
                {
                    // Process property
                    var prop = new IRProperty
                    {
                        Name = propNode.Name,
                        Type = propNode.PropertyType != null
                            ? _semanticAnalyzer.GetNodeType(propNode)
                            : new TypeInfo("Object", TypeKind.Class),
                        Access = MapAccessModifier(propNode.Access),
                        IsStatic = propNode.IsStatic,
                        IsReadOnly = propNode.IsReadOnly,
                        IsWriteOnly = propNode.IsWriteOnly,
                        IsVirtual = propNode.IsVirtual,
                        IsOverride = propNode.IsOverride
                    };

                    // Generate getter/setter methods
                    member.Accept(this);

                    // Find the generated getter/setter functions
                    var getterName = $"{node.Name}.get_{propNode.Name}";
                    var setterName = $"{node.Name}.set_{propNode.Name}";
                    prop.Getter = _module.Functions.FirstOrDefault(f => f.Name == getterName);
                    prop.Setter = _module.Functions.FirstOrDefault(f => f.Name == setterName);

                    irClass.Properties.Add(prop);
                }
                else if (member is ConstantDeclarationNode constNode)
                {
                    // ⛔ A class Const REACHED THE else BELOW before this arm existed, and
                    // Visit(ConstantDeclarationNode) with no _currentFunction takes its
                    // MODULE-SCOPE branch — so the constant was emitted as a global. Measured on
                    // C++: `int32_t K = 9;` landed after the class, so a method reading it failed
                    // with "use of undeclared identifier 'K'", and two classes each declaring
                    // `Const K` emitted two globals of that name — "redefinition of 'K'". A class
                    // constant is a MEMBER, and the flat global table has no room for that.
                    //
                    // ⚠ Lowered to a STATIC field carrying the folded value, which is what a VB
                    // class Const is: one per type, not per instance. That reuses the static
                    // member path every backend already has (C++'s out-of-class definition, MSIL's
                    // type initializer) rather than teaching each one a new member kind — and the
                    // initializer goes through the SAME BuildConstantFieldInitializer every other
                    // field uses, so a Const and a field agree about what a constant expression is,
                    // including refusing the same ones.
                    var constType = _semanticAnalyzer.GetNodeType(constNode)
                                    ?? new TypeInfo(constNode.Type?.Name ?? "Integer", TypeKind.Primitive);
                    irClass.Fields.Add(new IRField
                    {
                        Name = constNode.Name,
                        Type = constType,
                        Access = MapAccessModifier(constNode.Access),
                        IsStatic = true,
                        Initializer = BuildConstantFieldInitializer(
                            constNode.Value, constType, constNode.Name)
                    });
                }
                else
                {
                    member.Accept(this);
                }
            }

            SynthesizeImplicitConstructor(node, irClass);

            _currentClassName = null;
        }

        /// <summary>
        /// Gives a class that declares NO constructor a real one, when its base constructor takes
        /// <c>Optional</c> parameters the implicit base call has to fill.
        ///
        /// <para>⛔ Without it, <c>Inherits Base</c> against
        /// <c>Sub New(Optional a As Integer = 3)</c> is a legal program — the analyzer accepts it,
        /// correctly, because the base IS callable with no arguments — that EVERY backend
        /// miscompiled. There was no <c>IRConstructor</c> to carry the filled arguments, so each
        /// backend fell back to inventing a bare no-argument base call: measured, C# emitted
        /// <c>class Derived : Base</c> with no constructor at all and got <b>CS7036</b>, and MSIL
        /// threw <c>MissingMethodException: Void Base..ctor()</c>.</para>
        ///
        /// <para>⚠ The synthesized constructor is a REAL one — its own <c>IRFunction</c> with an
        /// entry block and a return — not an <c>IRConstructor</c> with a null Implementation.
        /// Deliberately: the shape a declared empty <c>Public Sub New()</c> produces is already
        /// exercised by all four backends, and only MSIL has a synthesize-a-default path at all
        /// (C#, JavaScript and C++ lean on their target language's implicit constructor). Handing
        /// them something no declared constructor ever looks like is how one of them would break in
        /// a way no test covers.</para>
        ///
        /// <para>⚠ <c>_currentFunction</c> is pointed at the synthesized function BEFORE the fill,
        /// so that a default which is an EXPRESSION rather than a literal
        /// (<c>Optional b As Integer = 2 + 3</c>) emits its instructions into this constructor's
        /// body, where they run before the base call consumes them — not into whatever function
        /// happened to be current.</para>
        ///
        /// <para>⚠ Does nothing unless there is something to fill: a base with no parameters, or no
        /// base at all, leaves <c>Constructors</c> empty exactly as before and the backends keep
        /// synthesizing their own default. That early-out is invisible at RUN time — measured, an
        /// empty synthesized constructor and the default each backend invents behave identically —
        /// so <c>BaseConstructorDiagnosticTests.NothingToFill_MeansNoSynthesizedConstructor</c>
        /// asserts the IR structurally instead, and removing either early-out fails it.</para>
        /// </summary>
        private void SynthesizeImplicitConstructor(ClassNode node, IRClass irClass)
        {
            // ⚠ `Constructors.Count > 0` is REDUNDANT with the analyzer today and kept anyway:
            // the analyzer records a class-level binding only for a class that declares no
            // constructor, so it cannot currently fire. Depending on another component's filter to
            // stay exactly as it is, rather than saying the condition here, is the coupling that
            // made the old `UnambiguousConstructorParameters` silently order-dependent. No test
            // can hold this one — it is a fail-safe that does nothing, kept with that said plainly.
            //
            // The parameter-count line IS held, structurally, by
            // BaseConstructorDiagnosticTests.NothingToFill_MeansNoSynthesizedConstructor: it is the
            // only thing stopping a class with nothing to fill from acquiring a constructor it does
            // not need. Note it carries BOTH of that test's shapes, not just the one it is named
            // for — a base declaring no constructor at all records no binding, so `implicitBase`
            // arrives null and the `?.` half of this same line is what stops it. The TryGetValue
            // line cannot be mutated away on its own (it declares `implicitBase`), so it is not
            // separately measurable; it is the lookup, not a third guard.
            //
            // ⚠ There is deliberately NO fourth guard for "the fill produced nothing". Measured by
            // mutation: with the fill emptied, every test still passed, because an empty
            // synthesized constructor behaves exactly like the default each backend invents. A
            // guard protecting nothing observable is a guard no test can hold.
            if (irClass.Constructors.Count > 0) return;
            if (!_semanticAnalyzer.ConstructorBindings.TryGetValue(node, out var implicitBase)) return;
            if (implicitBase?.Parameters == null || implicitBase.Parameters.Count == 0) return;

            _currentFunction = _module.CreateFunction(
                $"{node.Name}__ctor", new TypeInfo("Void", TypeKind.Void));
            _currentFunction.SourceFilePath = _sourceFilePath;
            _currentBlock = _currentFunction.CreateBlock("entry");

            var baseArgs = new List<IRValue>();
            AppendOmittedOptionalArguments(baseArgs, null, implicitBase);

            if (!_currentBlock.IsTerminated())
            {
                EmitInstruction(new IRReturn());
            }

            // ⚠ Cleared, not save/restored. Measured with a diagnostic: `_currentFunction` is
            // NULL every time this runs — a class is never visited while a function is current,
            // in a Module or a Namespace alike — so a save/restore pair would be restoring null
            // to null. Clearing is what `Visit(ConstructorNode)` does at its own end.
            //
            // The clear is load-bearing, and a test holds it: `Visit(VariableDeclarationNode)`
            // decides global-versus-local on `_currentFunction == null` alone, so leaking the
            // synthesized function sends the next module-level `Dim` down the LOCAL branch — no
            // static field is emitted and the program dies with InvalidProgramException. Dropping
            // these two lines fails MsilBaseConstructorTests
            // .TheSynthesizedConstructor_DoesNotLeakIntoTheNextModuleGlobal and nothing else.
            var synthesized = _currentFunction;
            _currentFunction = null;
            _currentBlock = null;

            var ctor = new IRConstructor
            {
                Access = AccessModifier.Public,
                Implementation = synthesized,
            };
            ctor.BaseConstructorArgs.AddRange(baseArgs);
            irClass.Constructors.Add(ctor);
        }

        /// <summary>
        /// The IRFunction a class member's visit DECLARED: the one <c>CreateFunction</c>
        /// appended at the index <c>Functions</c> had just before the visit.
        ///
        /// <para>⛔ Never <c>Functions.LastOrDefault()</c>. <c>Visit(FunctionNode)</c> registers
        /// the member's own function FIRST and then visits the body, so every lambda in the
        /// body is appended AFTER it — and "last" is the last lambda. MEASURED on the C# backend
        /// for a method containing <c>items.ForEach(Sub(x As Integer) Total = Total + x)</c>:
        /// the class got <c>public void AddAll(int x)</c> — the lambda's signature and body —
        /// while the real body was emitted as a stray static function, from a build that
        /// reported success. The JavaScript backend refused the shape, which is how it was found.</para>
        /// </summary>
        private IRFunction MemberFunctionAt(int declaredAt) =>
            declaredAt < _module.Functions.Count ? _module.Functions[declaredAt] : null;

        private AccessModifier MapAccessModifier(BasicLang.Compiler.AST.AccessModifier access)
        {
            return access switch
            {
                BasicLang.Compiler.AST.AccessModifier.Public => AccessModifier.Public,
                BasicLang.Compiler.AST.AccessModifier.Private => AccessModifier.Private,
                BasicLang.Compiler.AST.AccessModifier.Protected => AccessModifier.Protected,
                _ => AccessModifier.Private
            };
        }

        /// <summary>
        /// Convert a call's explicit generic type-argument references (Of T1, T2)
        /// into TypeInfo the backend can render as &lt;T1, T2&gt;.
        /// </summary>
        private List<TypeInfo> BuildGenericArgTypes(List<TypeReference> typeRefs)
        {
            var result = new List<TypeInfo>();
            if (typeRefs == null) return result;

            foreach (var typeRef in typeRefs)
            {
                if (typeRef == null || string.IsNullOrEmpty(typeRef.Name)) continue;
                var info = new TypeInfo(typeRef.Name, TypeKind.Class);
                if (typeRef.GenericArguments != null && typeRef.GenericArguments.Count > 0)
                {
                    info.GenericArguments.AddRange(BuildGenericArgTypes(typeRef.GenericArguments));
                }
                result.Add(info);
            }
            return result;
        }

        /// <summary>
        /// Build a constant IR value for a class field initializer, coercing it to the field's
        /// declared type so the backend emits a valid literal (e.g. a Single field gets a float).
        ///
        /// <para>⛔ A NON-LITERAL initializer used to be dropped SILENTLY, and the field read its
        /// type's zero. Measured before, on every shape that is arithmetically constant:
        /// <c>2 + 3</c>, <c>2 * 3 + 1</c>, <c>(1 + 2) * 3</c>, <c>8 \ 2</c>, <c>7.0 / 2.0</c> and
        /// <c>7 / 2</c> all emitted a bare <c>public int N;</c> on C# and printed <b>0</b> on
        /// JavaScript; <c>"a" &amp; "b"</c> gave <c>public string N;</c> and an empty string;
        /// <c>True And False</c> and <c>1 &lt; 2</c> gave <c>public bool N;</c> and False. Only a
        /// bare literal and unary +/- on one ever survived.</para>
        ///
        /// <para>⚠ ONE path, not two. A literal fast path (the old <c>LiteralExpressionNode</c> /
        /// unary +/- match) was kept here at first so the change would be strictly additive, and
        /// then REMOVED because it was measurably wrong: no test could tell it from the general
        /// fold, and the one shape where the two DID differ, the fast path was the broken one.
        /// <c>Public M As Decimal = 1.5</c> emitted <c>public decimal M = 1.5;</c> through it,
        /// which is not valid C# — the real C#-backend build failed with <b>CS0664</b>, "Literal
        /// of type double cannot be implicitly converted to type 'decimal'; use an 'M' suffix".
        /// The general lowering emits <c>1.5m</c> and builds. So this also fixes a PRE-EXISTING
        /// Decimal-field bug that had nothing to do with non-literal initializers.</para>
        ///
        /// <para>⚠ Everything now goes through the SAME foldability decision module-scope globals
        /// use (<see cref="TryFoldInitializerToConstant"/>), so a field and a global cannot
        /// disagree about what counts as a compile-time constant. Measured, they agree shape for
        /// shape — including where they agree to REFUSE (<c>Long = 3000000000 + 1</c> is declined
        /// by both).</para>
        ///
        /// <para>⚠ Which is also why this returns the folded constant AS IS. A second coercion
        /// here (the old <c>CoerceConstantToType</c>) went with the fast path, and the helper with
        /// it; re-stamping the field's declared type onto the result went too. The fold's own
        /// <c>CoerceToDeclaredType</c> has already done both by the time it returns — measured by
        /// diffing the emitted C# for fifteen literal shapes and seven folded ones across both
        /// removals, identical in every case, and no test could tell either apart. What is left is
        /// the whole of this method's job: ask, and refuse if the answer is no.</para>
        ///
        /// <para>⛔ What genuinely is not constant is REFUSED, not dropped. <c>Helper()</c> and
        /// <c>CInt(2.5)</c> need code to run, and a field initializer that runs code would have to
        /// be lowered into every constructor on every backend — which no backend here does. The
        /// old silent drop turned that into a field reading 0 with no diagnostic anywhere; a
        /// refusal naming the constructor is the honest answer until that lowering exists.</para>
        ///
        /// <para>⚠ A <c>Const</c> inside a class PARSES as of 2026-09-18 and lowers to a static
        /// field through this same helper, so a Const and a field agree about what is constant.
        /// Referencing that named constant from another initializer (<c>= K + 1</c>) is still
        /// refused — the folder substitutes no named constants — but that is a SHARED limit, not a
        /// class one: measured, module scope refuses the identical shape ("the module-level
        /// variable 'G' has an initializer that cannot be computed at compile time").</para>
        ///
        /// <para>⚠ A <c>Structure</c> field initializer still does not parse ("Expected member
        /// name but found Assignment"), which keeps the structure call site unreachable for
        /// initializers. PRE-EXISTING and measured.</para>
        /// </summary>
        private IRConstant BuildConstantFieldInitializer(
            ExpressionNode initializer, TypeInfo fieldType, string fieldName)
        {
            if (initializer == null) return null;

            if (TryFoldInitializerToConstant(initializer, fieldName, fieldType) is IRConstant folded)
            {
                return folded;
            }

            throw new Exception(
                $"Line {(initializer.Line > 0 ? initializer.Line : _currentSourceLine)}: the field "
                + $"'{fieldName}' has an initializer that cannot be computed at compile time. Only "
                + "a constant expression is supported here; assign it in a constructor instead.");
        }

        public void Visit(InterfaceNode node)
        {
            var irInterface = new IRInterface(node.Name)
            {
                Namespace = _currentNamespace
            };

            foreach (var method in node.Methods)
            {
                // Use the FULLY-resolved return type from semantic analysis (carries generic
                // arguments), falling back to a bare-name TypeInfo. Without the generic args the
                // C#/C++ backends emitted `List GetItems()` (dropping `<string>`), so the
                // signature no longer matched the implementing class (CS0535/CS0305), and the
                // LLVM/MSIL honesty guard (which keys on Type) never saw the collection.
                var resolvedReturn = _semanticAnalyzer.GetNodeType(method);
                var irMethod = new IRInterfaceMethod
                {
                    Name = method.Name,
                    ReturnType = resolvedReturn ?? new TypeInfo(method.ReturnType?.Name ?? "Void", TypeKind.Primitive),
                    HasDefaultImplementation = !method.IsAbstract && method.Body != null
                };

                foreach (var param in method.Parameters)
                {
                    // Populate the full resolved Type (with generic args) — not just TypeName —
                    // so both backends emit `Dictionary<string, int>` and the LLVM/MSIL guard
                    // (ModuleTypeWalker yields p.Type) sees interface collection params.
                    var resolvedParamType = _semanticAnalyzer.GetNodeType(param);
                    irMethod.Parameters.Add(new IRParameter
                    {
                        Name = param.Name,
                        TypeName = param.Type?.Name ?? "Object",
                        Type = resolvedParamType,
                        IsOptional = param.IsOptional,
                        IsParamArray = param.IsParamArray,
                        IsByRef = param.IsByRef,
                        DefaultValue = BuildExpressionValue(param.DefaultValue)
                    });
                }

                // Generate IR for default implementation if present
                if (!method.IsAbstract && method.Body != null)
                {
                    var implFunctionName = $"{node.Name}.{method.Name}_DefaultImpl";
                    var savedFunction = _currentFunction;
                    var savedBlock = _currentBlock;

                    _currentFunction = _module.CreateFunction(implFunctionName, new TypeInfo(method.ReturnType?.Name ?? "Void", TypeKind.Primitive));
                    _currentFunction.SourceFilePath = _sourceFilePath;

                    // Add parameters
                    foreach (var param in method.Parameters)
                    {
                        var paramType = _semanticAnalyzer.GetNodeType(param);
                        var irParam = new IRVariable(param.Name, paramType) { IsParameter = true };
                        _currentFunction.Parameters.Add(irParam);
                        PushVariableVersion(param.Name, irParam);
                    }

                    // Create entry block and generate body
                    _currentBlock = _currentFunction.CreateBlock("entry");
                    method.Body.Accept(this);

                    // Ensure function ends with return
                    if (!_currentBlock.IsTerminated())
                    {
                        if (method.ReturnType == null || method.ReturnType.Name == "Void")
                            EmitInstruction(new IRReturn());
                        else
                            EmitInstruction(new IRReturn(CreateDefaultValue(new TypeInfo(method.ReturnType.Name, TypeKind.Primitive))));
                    }

                    // Clean up
                    foreach (var param in method.Parameters)
                    {
                        PopVariableVersion(param.Name);
                    }

                    irMethod.DefaultImplementation = _currentFunction;

                    _currentFunction = savedFunction;
                    _currentBlock = savedBlock;
                }

                irInterface.Methods.Add(irMethod);
            }

            foreach (var prop in node.Properties)
            {
                irInterface.Properties.Add(new IRInterfaceProperty
                {
                    Name = prop.Name,
                    Type = new TypeInfo(prop.PropertyType?.Name ?? "Object", TypeKind.Class),
                    HasGetter = prop.Getter != null,
                    HasSetter = prop.Setter != null
                });
            }

            _module.Interfaces[node.Name] = irInterface;
        }

        public void Visit(EnumNode node)
        {
            var irEnum = new IREnum(node.Name)
            {
                Namespace = _currentNamespace,
                UnderlyingType = node.UnderlyingType != null
                    ? new TypeInfo(node.UnderlyingType.Name, TypeKind.Primitive)
                    : new TypeInfo("Int32", TypeKind.Primitive)
            };

            long nextValue = 0;
            foreach (var member in node.Members)
            {
                var irMember = new IREnumMember { Name = member.Name };

                // If member has explicit value, try to evaluate it
                if (member.Value != null)
                {
                    member.Value.Accept(this);
                    if (_expressionResult is IRConstant constant && constant.Value is long lval)
                    {
                        irMember.Value = lval;
                        nextValue = lval + 1;
                    }
                    else if (_expressionResult is IRConstant constant2 && constant2.Value is int ival)
                    {
                        irMember.Value = (long)ival;
                        nextValue = ival + 1;
                    }
                    else
                    {
                        irMember.Value = nextValue++;
                    }
                }
                else
                {
                    irMember.Value = nextValue++;
                }

                irEnum.Members.Add(irMember);
            }

            _module.Enums[node.Name] = irEnum;
        }

        public void Visit(EnumMemberNode node)
        {
            // Enum members are processed in EnumNode visitor
        }

        public void Visit(TypeNode node)
        {
            // User-defined types don't generate IR
        }

        public void Visit(StructureNode node)
        {
            // Structures lower to an IRClass flagged IsStruct: backends emit value types
            var irStruct = new IRClass(node.Name)
            {
                Namespace = _currentNamespace,
                IsStruct = true
            };

            foreach (var member in node.Members)
            {
                var fieldType = _semanticAnalyzer.GetNodeType(member)
                                ?? new TypeInfo(member.Type?.Name ?? "Object", TypeKind.Structure);
                irStruct.Fields.Add(new IRField
                {
                    Name = member.Name,
                    Type = fieldType,
                    Access = MapAccessModifier(member.Access),
                    IsStatic = member.IsStatic,
                    Initializer = BuildConstantFieldInitializer(member.Initializer, fieldType, member.Name)
                });
            }

            _module.Classes[node.Name] = irStruct;
        }

        public void Visit(UnionNode node)
        {
            // Unions don't generate IR directly - handled by code generators
        }

        public void Visit(TemplateDeclarationNode node)
        {
            // Templates are expanded before IR generation
            if (node.Declaration != null)
            {
                node.Declaration.Accept(this);
            }
        }

        public void Visit(DelegateDeclarationNode node)
        {
            var irDelegate = new IRDelegate(node.Name)
            {
                Namespace = _currentNamespace,
                ReturnType = node.ReturnType != null
                    ? new TypeInfo(node.ReturnType.Name, TypeKind.Primitive)
                    : new TypeInfo("Void", TypeKind.Void)
            };

            foreach (var param in node.Parameters)
            {
                irDelegate.Parameters.Add(new IRParameter
                {
                    Name = param.Name,
                    TypeName = param.Type?.Name ?? "Object",
                    IsOptional = param.IsOptional,
                    IsParamArray = param.IsParamArray,
                    IsByRef = param.IsByRef,
                    DefaultValue = BuildExpressionValue(param.DefaultValue)
                });
            }

            _module.Delegates[node.Name] = irDelegate;
        }

        public void Visit(ExtensionMethodNode node)
        {
            // Extension methods are regular functions with extension marker
            if (node.Method != null)
            {
                node.Method.Accept(this);

                // Mark the function as an extension method
                var irFunc = _module.Functions.FirstOrDefault(f => f.Name == ProcedureIrName(node.Method.Name));
                if (irFunc != null)
                {
                    irFunc.IsExtension = true;
                    irFunc.ExtendedType = node.ExtendedType;
                }
            }
        }

        public void Visit(ExternDeclarationNode node)
        {
            // Extern declarations are recorded in the module's extern table
            // They don't generate code themselves - they're used when the extern is called
            var externInfo = new IRExternDeclaration
            {
                Name = node.Name,
                IsFunction = node.IsFunction,
                ReturnType = new TypeInfo(node.ReturnType?.Name ?? "Void", node.ReturnType?.Name == "Void" || node.ReturnType == null ? TypeKind.Void : TypeKind.Primitive),
                PlatformImplementations = new Dictionary<string, string>(node.PlatformImplementations)
            };

            // Add parameters
            foreach (var param in node.Parameters)
            {
                externInfo.Parameters.Add(new IRParameter
                {
                    Name = param.Name,
                    TypeName = param.Type?.Name ?? "Object",
                    IsOptional = param.IsOptional,
                    IsParamArray = param.IsParamArray,
                    IsByRef = param.IsByRef,
                    DefaultValue = BuildExpressionValue(param.DefaultValue)
                });
            }

            _module.ExternDeclarations.Add(node.Name, externInfo);
        }

        public void Visit(ConstructorNode node)
        {
            TrackSourceLine(node);
            // Generate constructor as a special method
            var constructorName = _currentClassName != null ? $"{_currentClassName}__ctor" : "Constructor";
            var returnType = new TypeInfo("Void", TypeKind.Void);

            _currentFunction = _module.CreateFunction(constructorName, returnType);
            _currentFunction.SourceFilePath = _sourceFilePath;
            _currentBlock = _currentFunction.CreateBlock("entry");

            // Add parameters
            foreach (var param in node.Parameters)
            {
                var paramType = _semanticAnalyzer.GetNodeType(param);
                var irParam = new IRVariable(param.Name, paramType) { IsParameter = true };
                _currentFunction.Parameters.Add(irParam);
                PushVariableVersion(param.Name, irParam);
            }

            // Process base constructor arguments and store them for the IRConstructor
            _pendingBaseConstructorArgs = new List<IRValue>();
            if (node.BaseConstructorArgs.Count > 0)
            {
                // The base constructor the analyzer bound this `MyBase.New(…)` to — the third
                // construction site, and it needs the same coercion and the same Optional fill as
                // the other two. Measured before: `MyBase.New(7)` against
                // `Sub New(a As Integer, Optional b As Integer = 5)` was refused outright by the
                // analyzer ("No constructor for base class 'Base' takes 1 argument(s)").
                var baseCtor = _semanticAnalyzer.ConstructorBindings.TryGetValue(node, out var boundBase)
                    ? boundBase
                    : null;

                foreach (var arg in node.BaseConstructorArgs)
                {
                    arg.Accept(this);
                    if (_expressionResult != null)
                    {
                        _pendingBaseConstructorArgs.Add(CoerceToParameterType(
                            _expressionResult, baseCtor, _pendingBaseConstructorArgs.Count));
                    }
                }

                AppendOmittedOptionalArguments(_pendingBaseConstructorArgs, null, baseCtor);
            }
            else if (_semanticAnalyzer.ConstructorBindings.TryGetValue(node, out var implicitBase))
            {
                // ⚠ No MyBase.New written, but the base constructor the analyzer bound this to may
                // still take OPTIONAL parameters — the implicit call has to fill them exactly as an
                // explicit one would. The analyzer records a binding here only when the base IS
                // callable with no arguments, so reaching this means filling is all that is left.
                AppendOmittedOptionalArguments(_pendingBaseConstructorArgs, null, implicitBase);
            }

            // Generate body
            if (node.Body != null)
            {
                node.Body.Accept(this);
            }

            // Add return if not terminated
            if (!_currentBlock.IsTerminated())
            {
                EmitInstruction(new IRReturn());
            }

            _currentFunction = null;
            _currentBlock = null;
        }

        /// <summary>
        /// Lowers a property's accessors into <c>get_X</c>/<c>set_X</c> functions on the module.
        ///
        /// <para>⚠ <b>Saves and restores the build context rather than nulling it</b>, matching
        /// every other site here that creates a nested function (the interface default-impl,
        /// lambda and operator paths all do). This method used to end with
        /// <c>_currentFunction = _currentBlock = null</c>, which is only safe because nothing
        /// today visits a property while a function is being built — BasicLang has no top-level
        /// statements and no function-local classes, so <c>Visit(ClassNode)</c> is only reached
        /// from the declaration walk.</para>
        ///
        /// <para>⛔ That is a thin guarantee for a SILENT failure mode: <see cref="EmitInstruction"/>
        /// discards instructions outright when <c>_currentBlock</c> is null — no error, no
        /// warning — so the day either of those grammar facts changes, everything after a
        /// property would vanish from the output with a clean build. Restoring costs two locals
        /// and removes the trap. <b>This is hardening, not a bug fix: no reachable input
        /// misbehaves today</b> (measured across full/auto properties, classes in Modules, .mod
        /// files, and the multi-file project route, on the C#, C++ and JavaScript backends).</para>
        /// </summary>
        public void Visit(PropertyNode node)
        {
            var savedFunction = _currentFunction;
            var savedBlock = _currentBlock;

            var propertyType = node.PropertyType != null
                ? _semanticAnalyzer.GetNodeType(node) ?? new TypeInfo("Object", TypeKind.Class)
                : new TypeInfo("Object", TypeKind.Class);

            // Generate getter method
            if (node.Getter != null)
            {
                var getterName = _currentClassName != null
                    ? $"{_currentClassName}.get_{node.Name}"
                    : $"get_{node.Name}";

                _currentFunction = _module.CreateFunction(getterName, propertyType);
                _currentFunction.SourceFilePath = _sourceFilePath;
                _currentBlock = _currentFunction.CreateBlock("entry");

                node.Getter.Accept(this);

                if (!_currentBlock.IsTerminated())
                {
                    EmitInstruction(new IRReturn());
                }
            }

            // Generate setter method
            if (node.Setter != null)
            {
                var setterName = _currentClassName != null
                    ? $"{_currentClassName}.set_{node.Name}"
                    : $"set_{node.Name}";

                var voidType = new TypeInfo("Void", TypeKind.Void);
                _currentFunction = _module.CreateFunction(setterName, voidType);
                _currentFunction.SourceFilePath = _sourceFilePath;
                _currentBlock = _currentFunction.CreateBlock("entry");

                // Add value parameter
                var valueParam = new IRVariable("value", propertyType) { IsParameter = true };
                _currentFunction.Parameters.Add(valueParam);
                PushVariableVersion("value", valueParam);

                node.Setter.Accept(this);

                if (!_currentBlock.IsTerminated())
                {
                    EmitInstruction(new IRReturn());
                }
            }

            _currentFunction = savedFunction;
            _currentBlock = savedBlock;
        }

        public void Visit(MyBaseExpressionNode node)
        {
            // MyBase is the SAME OBJECT seen as its base class — an inherited field lives on this
            // instance, so the receiver is `Me`, carrying the BASE type so a backend that names
            // the declaring class in the access (MSIL's ldfld token) names the right one.
            //
            // ⛔ It used to lower to a variable literally named `__base`, which nothing declares:
            // "use of undeclared identifier '__base'" on C++, "__base is not defined" on
            // JavaScript, CS0103 on C# and an InvalidProgramException on MSIL — `MyBase.Field`
            // was broken on all four, measured. Only `MyBase.Method(...)` escaped it, through the
            // IRBaseMethodCall arm that intercepts the call before the receiver is ever visited.
            var baseType = _semanticAnalyzer.GetNodeType(node);
            _expressionResult = new IRVariable("Me", baseType);
        }

        public void Visit(LambdaExpressionNode node)
        {
            // Generate a unique name for the lambda function.
            // NOTE: must use a dedicated counter, not _module.Functions.Count — a nested
            // lambda is visited before its enclosing lambda is added to the module, so
            // both would otherwise get the same name and the backend would inline the
            // wrong function at the call site.
            var lambdaName = $"__lambda_{_lambdaCounter++}";

            // Determine return type from semantic analysis
            var lambdaType = _semanticAnalyzer.GetNodeType(node);
            var returnType = node.IsFunction
                ? (_semanticAnalyzer.GetNodeType(node.Body) ?? new TypeInfo("Object", TypeKind.Class))
                : new TypeInfo("Void", TypeKind.Primitive);

            // If explicit return type specified, use it
            if (node.ReturnType != null)
            {
                returnType = new TypeInfo(node.ReturnType.Name, TypeKind.Class);
            }

            // Create the lambda function
            var lambdaFunc = new IRFunction(lambdaName, returnType);
            lambdaFunc.IsLambda = true;

            // A lambda belongs to the module it is written in. With ModuleName null the C#
            // backend grouped it under its DEFAULT class name ("Program") — a phantom module
            // that, for a file named Main.bas (whose own module class is renamed Program),
            // produced two `static class Program` declarations and CS0101.
            lambdaFunc.ModuleName = _currentModuleName ?? _module?.Name;

            // Detect captured variables (variables from outer scopes)
            var capturedVars = new List<(string name, TypeInfo type)>();

            // Add parameters
            foreach (var param in node.Parameters)
            {
                var paramSymbol = _semanticAnalyzer.GetNodeSymbol(param);
                var paramType = paramSymbol?.Type ?? new TypeInfo("Object", TypeKind.Class);
                var paramVar = new IRVariable(param.Name, paramType)
                {
                    IsParameter = true
                };
                lambdaFunc.Parameters.Add(paramVar);
            }

            // Save current context
            var savedFunction = _currentFunction;
            var savedBlock = _currentBlock;
            var savedLocals = new Dictionary<string, IRAlloca>(_locals);

            // Set up lambda function context
            _currentFunction = lambdaFunc;
            _currentFunction.SourceFilePath = _sourceFilePath;
            _currentBlock = lambdaFunc.CreateBlock("entry");
            _locals.Clear();

            // Make parameters visible to the lambda body (they're declared by the
            // lambda itself, so no allocas are emitted for them)
            foreach (var paramVar in lambdaFunc.Parameters)
            {
                PushVariableVersion(paramVar.Name, paramVar);
            }

            // Generate body
            if (node.Body != null)
            {
                node.Body.Accept(this);
                // Return the expression result (Sub lambdas don't return a value)
                EmitInstruction(new IRReturn(node.IsFunction ? _expressionResult : null));
            }
            else if (node.StatementBody != null)
            {
                node.StatementBody.Accept(this);
                // Ensure we have a return for void lambdas
                if (!_currentBlock.IsTerminated())
                {
                    EmitInstruction(new IRReturn(null));
                }
            }

            // Detect captured variables by checking which outer scope variables were accessed
            // This is a simplified approach - in a full implementation, we'd track this during body generation
            foreach (var kvp in savedLocals)
            {
                if (!_locals.ContainsKey(kvp.Key))
                {
                    // This variable from outer scope was potentially captured
                    // We'll let the C# backend handle this via closure conversion
                    capturedVars.Add((kvp.Key, kvp.Value.Type));
                }
            }

            // Store captured variables in the function metadata
            lambdaFunc.CapturedVariables = capturedVars;

            // Add lambda function to module
            _module.Functions.Add(lambdaFunc);

            // Restore context
            _currentFunction = savedFunction;
            _currentBlock = savedBlock;
            _locals.Clear();
            foreach (var kvp in savedLocals)
            {
                _locals[kvp.Key] = kvp.Value;
            }

            // Remove lambda parameters from the visible variable versions
            foreach (var paramVar in lambdaFunc.Parameters)
            {
                PopVariableVersion(paramVar.Name);
            }

            // The result is a reference to the lambda function (delegate)
            _expressionResult = new IRVariable(lambdaName, lambdaType ?? new TypeInfo("Delegate", TypeKind.Delegate));
        }

        public void Visit(CollectionInitializerNode node)
        {
            // Get the element type from semantic analysis
            var arrayType = _semanticAnalyzer.GetNodeType(node) as TypeInfo;
            var elementType = arrayType?.ElementType ?? new TypeInfo("Object", TypeKind.Class);

            // Create a list to hold the element values
            var elements = new List<IRValue>();

            foreach (var element in node.Elements)
            {
                element.Accept(this);
                elements.Add(_expressionResult);
            }

            // Create an array allocation IR
            var tempName = _currentFunction.GetNextTempName();
            var arrayAlloc = new IRArrayAlloc(tempName, elementType, elements.Count);
            EmitInstruction(arrayAlloc);

            // Store each element
            for (int i = 0; i < elements.Count; i++)
            {
                var indexConst = new IRConstant(i, new TypeInfo("Integer", TypeKind.Primitive));
                var store = new IRArrayStore(arrayAlloc, indexConst, elements[i]);
                EmitInstruction(store);
            }

            _expressionResult = arrayAlloc;
        }

        public void Visit(TupleLiteralNode node)
        {
            // Get the tuple type from semantic analysis
            var tupleType = _semanticAnalyzer.GetNodeType(node) as TypeInfo;

            // Evaluate all tuple elements
            var elementValues = new List<IRValue>();
            foreach (var element in node.Elements)
            {
                element.Accept(this);
                elementValues.Add(_expressionResult);
            }

            // Create a tuple IR node (represented as a call to tuple constructor)
            var tempName = _currentFunction.GetNextTempName();
            var tupleCall = new IRCall(
                tempName,
                "ValueTuple.Create",
                tupleType ?? new TypeInfo("Object", TypeKind.Class)
            );
            foreach (var arg in elementValues)
            {
                tupleCall.Arguments.Add(arg);
            }
            EmitInstruction(tupleCall);
            _expressionResult = tupleCall;
        }

        public void Visit(OperatorDeclarationNode node)
        {
            // Generate operator as a static method with special naming
            var opMethodName = $"op_{GetOperatorMethodName(node.OperatorSymbol)}";
            var returnType = new TypeInfo(node.ReturnType?.Name ?? "Object", TypeKind.Class);

            // Create the function with class-qualified name for the module
            var funcName = _currentClassName != null
                ? $"{_currentClassName}.{opMethodName}"
                : opMethodName;
            var opFunc = _module.CreateFunction(funcName, returnType);
            // Operators are always static (handled at code generation)

            // Add parameters
            foreach (var param in node.Parameters)
            {
                var paramType = new TypeInfo(param.Type?.Name ?? "Object", TypeKind.Class);
                var paramVar = new IRVariable(param.Name, paramType) { IsParameter = true };
                opFunc.Parameters.Add(paramVar);
                PushVariableVersion(param.Name, paramVar);
            }

            // Save context and switch to operator function
            var savedFunction = _currentFunction;
            var savedBlock = _currentBlock;
            var savedLocals = new Dictionary<string, IRAlloca>(_locals);

            _currentFunction = opFunc;
            _currentFunction.SourceFilePath = _sourceFilePath;
            _currentBlock = opFunc.CreateBlock("entry");
            _locals.Clear();

            // Generate body
            if (node.Body != null)
            {
                node.Body.Accept(this);
            }

            // Ensure return
            if (!_currentBlock.IsTerminated())
            {
                EmitInstruction(new IRReturn(null));
            }

            // Clean up parameter versions
            foreach (var param in node.Parameters)
            {
                PopVariableVersion(param.Name);
            }

            // If inside a class, also add as an IRMethod
            if (_currentClassName != null && _module.Classes.TryGetValue(_currentClassName, out var irClass))
            {
                var irMethod = new IRMethod
                {
                    Name = opMethodName,
                    ReturnType = returnType,
                    Access = MapAccessModifier(node.Access),
                    IsStatic = true,  // Operators are always static
                    IsVirtual = false,
                    IsOverride = false,
                    Implementation = opFunc
                };
                irClass.Methods.Add(irMethod);
            }

            // Restore context
            _currentFunction = savedFunction;
            _currentBlock = savedBlock;
            _locals.Clear();
            foreach (var kvp in savedLocals)
            {
                _locals[kvp.Key] = kvp.Value;
            }
        }

        private string GetOperatorMethodName(string operatorSymbol)
        {
            return operatorSymbol switch
            {
                "+" => "Addition",
                "-" => "Subtraction",
                "*" => "Multiply",
                "/" => "Division",
                "\\" => "IntegerDivision",
                "Mod" => "Modulus",
                "^" => "Exponent",
                "=" => "Equality",
                "<>" => "Inequality",
                "<" => "LessThan",
                ">" => "GreaterThan",
                "<=" => "LessThanOrEqual",
                ">=" => "GreaterThanOrEqual",
                "&" => "Concatenate",
                "And" => "BitwiseAnd",
                "Or" => "BitwiseOr",
                "Xor" => "ExclusiveOr",
                "Not" => "OnesComplement",
                "IsTrue" => "True",
                "IsFalse" => "False",
                "CType" => "Implicit",
                _ => operatorSymbol.Replace(" ", "")
            };
        }

        public void Visit(EventDeclarationNode node)
        {
            // Add event to the current class
            if (_currentClassName != null && _module.Classes.TryGetValue(_currentClassName, out var irClass))
            {
                var eventType = _semanticAnalyzer.GetNodeType(node);
                var irEvent = new IREvent
                {
                    Name = node.Name,
                    Access = MapAccessModifier(node.Access),
                    DelegateType = eventType?.Name ?? node.EventType?.Name ?? "EventHandler",
                    Type = eventType,
                    IsStatic = false
                };
                irClass.Events.Add(irEvent);
            }
        }

        public void Visit(RaiseEventStatementNode node)
        {
            TrackSourceLine(node);
            // Evaluate arguments
            var args = new List<IRValue>();
            foreach (var arg in node.Arguments)
            {
                arg.Accept(this);
                args.Add(_expressionResult);
            }

            // Generate call to event invocation
            var eventCall = new IRCall(
                _currentFunction.GetNextTempName(),
                $"raise_{node.EventName}",
                new TypeInfo("Void", TypeKind.Void)
            );
            foreach (var arg in args)
            {
                eventCall.Arguments.Add(arg);
            }
            EmitInstruction(eventCall);
        }

        public void Visit(AddHandlerStatementNode node)
        {
            TrackSourceLine(node);
            // Evaluate event and handler expressions
            node.EventExpression?.Accept(this);
            var eventExpr = _expressionResult;

            node.HandlerExpression?.Accept(this);
            var handlerExpr = _expressionResult;

            // Generate delegate combination call
            var addCall = new IRCall(
                _currentFunction.GetNextTempName(),
                "Delegate.Combine",
                new TypeInfo("Delegate", TypeKind.Delegate)
            );
            if (eventExpr != null) addCall.Arguments.Add(eventExpr);
            if (handlerExpr != null) addCall.Arguments.Add(handlerExpr);
            EmitInstruction(addCall);
        }

        public void Visit(RemoveHandlerStatementNode node)
        {
            TrackSourceLine(node);
            // Evaluate event and handler expressions
            node.EventExpression?.Accept(this);
            var eventExpr = _expressionResult;

            node.HandlerExpression?.Accept(this);
            var handlerExpr = _expressionResult;

            // Generate delegate removal call
            var removeCall = new IRCall(
                _currentFunction.GetNextTempName(),
                "Delegate.Remove",
                new TypeInfo("Delegate", TypeKind.Delegate)
            );
            if (eventExpr != null) removeCall.Arguments.Add(eventExpr);
            if (handlerExpr != null) removeCall.Arguments.Add(handlerExpr);
            EmitInstruction(removeCall);
        }

        public void Visit(TypePatternNode node)
        {
            // Type patterns are handled in the Select Case code generation
            // The pattern generates a type check: If TypeOf expr Is Type Then
            // For now, just ensure the When guard is evaluated if present
            if (node.WhenGuard != null)
            {
                node.WhenGuard.Accept(this);
            }
        }

        public void Visit(ConstantPatternNode node)
        {
            // Constant patterns generate equality checks
            // Evaluate the constant value
            node.Value?.Accept(this);

            if (node.WhenGuard != null)
            {
                node.WhenGuard.Accept(this);
            }
        }

        public void Visit(RangePatternNode node)
        {
            // Range patterns generate: lower <= expr AndAlso expr <= upper
            node.LowerBound?.Accept(this);
            var lowerValue = _expressionResult;

            node.UpperBound?.Accept(this);
            var upperValue = _expressionResult;

            if (node.WhenGuard != null)
            {
                node.WhenGuard.Accept(this);
            }
        }

        public void Visit(ComparisonPatternNode node)
        {
            // Comparison patterns generate: expr op value
            node.Value?.Accept(this);

            if (node.WhenGuard != null)
            {
                node.WhenGuard.Accept(this);
            }
        }

        public void Visit(NothingPatternNode node)
        {
            // Nothing patterns generate: expr Is Nothing
            // The actual code generation is handled in Select Case
            if (node.WhenGuard != null)
            {
                node.WhenGuard.Accept(this);
            }
        }

        public void Visit(OrPatternNode node)
        {
            // Or patterns generate: pattern1 OrElse pattern2 OrElse ...
            foreach (var alt in node.Alternatives)
            {
                alt.Accept(this);
            }

            if (node.WhenGuard != null)
            {
                node.WhenGuard.Accept(this);
            }
        }

        public void Visit(TuplePatternNode node)
        {
            // Tuple patterns generate deconstruction checks
            foreach (var element in node.Elements)
            {
                element.Accept(this);
            }

            if (node.WhenGuard != null)
            {
                node.WhenGuard.Accept(this);
            }
        }

        public void Visit(BindingPatternNode node)
        {
            // Binding pattern captures the value with a variable name
            // The When guard provides the actual matching condition
            if (node.WhenGuard != null)
            {
                node.WhenGuard.Accept(this);
            }
        }

        public void Visit(AwaitExpressionNode node)
        {
            TrackSourceLine(node);
            IRValue taskExpr = null;

            // Special handling for CallExpressionNode - don't emit it separately
            // Instead, create the IRCall and embed it in the IRAwait
            if (node.Expression is CallExpressionNode callNode)
            {
                string functionName = null;
                if (callNode.Callee is IdentifierExpressionNode idExpr)
                {
                    functionName = idExpr.Name;
                }
                else if (callNode.Callee is MemberAccessExpressionNode memberExpr &&
                         memberExpr.Object is IdentifierExpressionNode objExpr)
                {
                    // Qualified call like Task.Delay(10)
                    functionName = $"{objExpr.Name}.{memberExpr.MemberName}";
                }

                if (functionName != null)
                {
                    var returnType = _semanticAnalyzer.GetNodeType(callNode);
                    var tempName = _currentFunction.GetNextTempName();
                    var call = new IRCall(tempName, functionName, returnType);

                    // Evaluate arguments
                    foreach (var arg in callNode.Arguments)
                    {
                        arg.Accept(this);
                        call.Arguments.Add(_expressionResult);
                    }

                    // Don't emit the call - it will be part of the await
                    taskExpr = call;
                }
            }

            if (taskExpr == null)
            {
                // For other expressions (complex member calls, variables), evaluate normally
                node.Expression?.Accept(this);
                taskExpr = _expressionResult;
            }

            // Generate IRAwait instruction
            // Await unwraps Task(Of T) to T - the semantic analyzer records the awaited type
            var resultName = _currentFunction.GetNextTempName();
            var resultType = _semanticAnalyzer.GetNodeType(node)
                             ?? taskExpr?.Type
                             ?? new TypeInfo("Object", TypeKind.Class);
            var awaitInst = new IRAwait(resultName, taskExpr, resultType);
            EmitInstruction(awaitInst);

            _expressionResult = awaitInst;
        }

        public void Visit(YieldStatementNode node)
        {
            TrackSourceLine(node);
            if (node.IsBreak)
            {
                // Yield Break - generate IRYield with IsBreak = true
                EmitInstruction(new IRYield(null, isBreak: true));
            }
            else
            {
                // Yield Return - yield a value
                node.Value?.Accept(this);
                var yieldValue = _expressionResult;

                // Generate IRYield instruction
                EmitInstruction(new IRYield(yieldValue, isBreak: false));
            }
        }

        public void Visit(LinqQueryExpressionNode node)
        {
            TrackSourceLine(node);
            // LINQ queries are converted to method chain calls
            // Store the query as a special IR node for code generation
            IRValue result = null;

            foreach (var clause in node.Clauses)
            {
                switch (clause)
                {
                    case FromClause from:
                        from.Collection?.Accept(this);
                        result = _expressionResult;
                        break;

                    case WhereClause where:
                        where.Condition?.Accept(this);
                        var whereCondition = _expressionResult;
                        var whereCall = new IRCall(
                            _currentFunction.GetNextTempName(),
                            "Where",
                            new TypeInfo("IEnumerable", TypeKind.Interface));
                        if (result != null) whereCall.Arguments.Add(result);
                        whereCall.Arguments.Add(whereCondition);
                        EmitInstruction(whereCall);
                        result = whereCall;
                        break;

                    case SelectClause select:
                        select.Selector?.Accept(this);
                        var selectExpr = _expressionResult;
                        var selectCall = new IRCall(
                            _currentFunction.GetNextTempName(),
                            "Select",
                            new TypeInfo("IEnumerable", TypeKind.Interface));
                        if (result != null) selectCall.Arguments.Add(result);
                        selectCall.Arguments.Add(selectExpr);
                        EmitInstruction(selectCall);
                        result = selectCall;
                        break;

                    case OrderByClause orderBy:
                        orderBy.KeySelector?.Accept(this);
                        var orderKey = _expressionResult;
                        var orderMethod = orderBy.Descending ? "OrderByDescending" : "OrderBy";
                        var orderCall = new IRCall(
                            _currentFunction.GetNextTempName(),
                            orderMethod,
                            new TypeInfo("IOrderedEnumerable", TypeKind.Interface));
                        if (result != null) orderCall.Arguments.Add(result);
                        orderCall.Arguments.Add(orderKey);
                        EmitInstruction(orderCall);
                        result = orderCall;
                        break;

                    case GroupByClause groupBy:
                        groupBy.KeySelector?.Accept(this);
                        var groupKey = _expressionResult;

                        var groupCall = new IRCall(
                            _currentFunction.GetNextTempName(),
                            "GroupBy",
                            new TypeInfo("IEnumerable", TypeKind.Interface));
                        if (result != null) groupCall.Arguments.Add(result);
                        groupCall.Arguments.Add(groupKey);

                        // If there's an element selector
                        if (groupBy.ElementSelector != null)
                        {
                            groupBy.ElementSelector.Accept(this);
                            groupCall.Arguments.Add(_expressionResult);
                        }

                        EmitInstruction(groupCall);
                        result = groupCall;
                        break;

                    case JoinClause join:
                        join.Collection?.Accept(this);
                        var innerCollection = _expressionResult;

                        join.OuterKeySelector?.Accept(this);
                        var outerKey = _expressionResult;

                        join.InnerKeySelector?.Accept(this);
                        var innerKey = _expressionResult;

                        var joinMethod = !string.IsNullOrEmpty(join.IntoVariable) ? "GroupJoin" : "Join";
                        var joinCall = new IRCall(
                            _currentFunction.GetNextTempName(),
                            joinMethod,
                            new TypeInfo("IEnumerable", TypeKind.Interface));
                        if (result != null) joinCall.Arguments.Add(result);
                        joinCall.Arguments.Add(innerCollection);
                        joinCall.Arguments.Add(outerKey);
                        joinCall.Arguments.Add(innerKey);

                        EmitInstruction(joinCall);
                        result = joinCall;
                        break;

                    case AggregateClause aggregate:
                        aggregate.Collection?.Accept(this);
                        var aggCollection = _expressionResult;

                        var aggCall = new IRCall(
                            _currentFunction.GetNextTempName(),
                            "Aggregate",
                            new TypeInfo("Object", TypeKind.Class));
                        if (result != null) aggCall.Arguments.Add(result);
                        aggCall.Arguments.Add(aggCollection);

                        if (aggregate.Selector != null)
                        {
                            aggregate.Selector.Accept(this);
                            aggCall.Arguments.Add(_expressionResult);
                        }

                        EmitInstruction(aggCall);
                        result = aggCall;
                        break;

                    case LetClause let:
                        // Let clauses create projection with additional property
                        // We'll represent this as a Select that creates an anonymous type
                        let.Value?.Accept(this);
                        var letValue = _expressionResult;

                        var letCall = new IRCall(
                            _currentFunction.GetNextTempName(),
                            "Select",
                            new TypeInfo("IEnumerable", TypeKind.Interface));
                        if (result != null) letCall.Arguments.Add(result);
                        letCall.Arguments.Add(letValue);

                        EmitInstruction(letCall);
                        result = letCall;
                        break;

                    case TakeClause take:
                        take.Count?.Accept(this);
                        var takeCount = _expressionResult;
                        var takeCall = new IRCall(
                            _currentFunction.GetNextTempName(),
                            "Take",
                            new TypeInfo("IEnumerable", TypeKind.Interface));
                        if (result != null) takeCall.Arguments.Add(result);
                        takeCall.Arguments.Add(takeCount);
                        EmitInstruction(takeCall);
                        result = takeCall;
                        break;

                    case SkipClause skip:
                        skip.Count?.Accept(this);
                        var skipCount = _expressionResult;
                        var skipCall = new IRCall(
                            _currentFunction.GetNextTempName(),
                            "Skip",
                            new TypeInfo("IEnumerable", TypeKind.Interface));
                        if (result != null) skipCall.Arguments.Add(result);
                        skipCall.Arguments.Add(skipCount);
                        EmitInstruction(skipCall);
                        result = skipCall;
                        break;

                    case DistinctClause:
                        var distinctCall = new IRCall(
                            _currentFunction.GetNextTempName(),
                            "Distinct",
                            new TypeInfo("IEnumerable", TypeKind.Interface));
                        if (result != null) distinctCall.Arguments.Add(result);
                        EmitInstruction(distinctCall);
                        result = distinctCall;
                        break;
                }
            }

            _expressionResult = result;
        }

        public void Visit(InlineCodeNode node)
        {
            TrackSourceLine(node);
            // Create an inline code instruction that the code generator will handle
            var inlineInstr = new IRInlineCode(node.Language, node.Code);
            EmitInstruction(inlineInstr);
        }

        // ====================================================================
        // Preprocessor Directives
        // ====================================================================
        // Note: Preprocessor directives are typically processed before IR generation.
        // These methods handle cases where preprocessor nodes reach the IR builder.

        public void Visit(PreprocessorDefineNode node)
        {
            // Preprocessor #Define is typically handled during preprocessing
            _currentBlock.AddInstruction(new IRComment($"#Define {node.Name}" + (node.Value != null ? $" = {node.Value}" : "")));
        }

        public void Visit(PreprocessorUndefineNode node)
        {
            _currentBlock.AddInstruction(new IRComment($"#Undefine {node.Name}"));
        }

        public void Visit(PreprocessorIfNode node)
        {
            // In a true preprocessor, this would conditionally include/exclude code
            // For now, emit all branches with comments
            _currentBlock.AddInstruction(new IRComment("#If block"));
            foreach (var stmt in node.ThenBody)
            {
                stmt.Accept(this);
            }

            foreach (var elseIf in node.ElseIfClauses)
            {
                _currentBlock.AddInstruction(new IRComment("#ElseIf block"));
                foreach (var stmt in elseIf.Body)
                {
                    stmt.Accept(this);
                }
            }

            if (node.ElseBody.Count > 0)
            {
                _currentBlock.AddInstruction(new IRComment("#Else block"));
                foreach (var stmt in node.ElseBody)
                {
                    stmt.Accept(this);
                }
            }
            _currentBlock.AddInstruction(new IRComment("#EndIf"));
        }

        public void Visit(PreprocessorIncludeNode node)
        {
            _currentBlock.AddInstruction(new IRComment($"#Include \"{node.FilePath}\""));
        }

        public void Visit(PreprocessorConstNode node)
        {
            _currentBlock.AddInstruction(new IRComment($"#Const {node.Name}"));
        }

        public void Visit(PreprocessorRegionNode node)
        {
            _currentBlock.AddInstruction(new IRComment($"#Region {node.Name}"));
            foreach (var stmt in node.Body)
            {
                stmt.Accept(this);
            }
            _currentBlock.AddInstruction(new IRComment("#End Region"));
        }

        public void Visit(DeclareNode node)
        {
            TrackSourceLine(node);
            // Create an extern declaration in the IR module
            var externDecl = new IRExternDeclaration
            {
                Name = node.Name,
                IsFunction = node.IsFunction,
                LibraryName = node.LibraryName,
                AliasName = node.AliasName,
                CallingConvention = node.Convention.ToString()
            };

            // Add parameters
            foreach (var param in node.Parameters)
            {
                externDecl.Parameters.Add(new IRParameter
                {
                    Name = param.Name,
                    TypeName = param.Type?.Name ?? "Object",
                    Type = new TypeInfo(param.Type?.Name ?? "Object", TypeKind.Primitive),
                    IsOptional = param.IsOptional,
                    IsParamArray = param.IsParamArray,
                    IsByRef = param.IsByRef,
                    DefaultValue = BuildExpressionValue(param.DefaultValue)
                });
            }

            // Set return type
            if (node.IsFunction && node.ReturnType != null)
            {
                externDecl.ReturnType = new TypeInfo(node.ReturnType.Name, TypeKind.Primitive);
            }
            else
            {
                externDecl.ReturnType = new TypeInfo("Void", TypeKind.Void);
            }

            _module.ExternDeclarations[node.Name] = externDecl;
        }

        // ====================================================================
        // Statements
        // ====================================================================

        public void Visit(BlockNode node)
        {
            foreach (var statement in node.Statements)
            {
                TrackSourceLine(statement);
                statement.Accept(this);

                // Stop if we hit a terminator
                if (_currentBlock.IsTerminated())
                    break;
            }
        }

        public void Visit(IfStatementNode node)
        {
            TrackSourceLine(node);
            // Evaluate condition
            node.Condition.Accept(this);
            var condition = _expressionResult;

            // Create blocks with unique names to handle nested ifs
            var ifId = _ifCounter++;
            var thenBlock = _currentFunction.CreateBlock($"if{ifId}.then");
            var elseBlock = node.ElseBlock != null || node.ElseIfClauses.Count > 0
                ? _currentFunction.CreateBlock($"if{ifId}.else")
                : null;
            var mergeBlock = _currentFunction.CreateBlock($"if{ifId}.end");

            // Emit conditional branch
            var branchTarget = elseBlock ?? mergeBlock;
            EmitInstruction(new IRConditionalBranch(condition, thenBlock, branchTarget));

            // Generate then block
            _currentBlock = thenBlock;
            node.ThenBlock.Accept(this);
            if (!_currentBlock.IsTerminated())
            {
                EmitInstruction(new IRBranch(mergeBlock));
            }

            // Generate else/elseif blocks
            if (node.ElseIfClauses.Count > 0 || node.ElseBlock != null)
            {
                _currentBlock = elseBlock;

                // Handle elseif chain. Reuse this If's id plus a per-clause index so each
                // ElseIf gets a UNIQUE label: without it, two ElseIf clauses (in one chain or
                // across two If statements) both emit `elseif_then:`/`elseif_else:` C++ labels
                // -> C2045 "label redefined" (same class of bug as the loop/Try labels).
                int elseIfIdx = 0;
                foreach (var (elseIfCond, elseIfBlock) in node.ElseIfClauses)
                {
                    elseIfCond.Accept(this);
                    var elseIfCondition = _expressionResult;

                    var elseIfThen = _currentFunction.CreateBlock($"if{ifId}.elseif{elseIfIdx}.then");
                    var elseIfNext = _currentFunction.CreateBlock($"if{ifId}.elseif{elseIfIdx}.else");
                    elseIfIdx++;

                    EmitInstruction(new IRConditionalBranch(elseIfCondition, elseIfThen, elseIfNext));

                    _currentBlock = elseIfThen;
                    elseIfBlock.Accept(this);
                    if (!_currentBlock.IsTerminated())
                    {
                        EmitInstruction(new IRBranch(mergeBlock));
                    }

                    _currentBlock = elseIfNext;
                }

                // Final else block
                if (node.ElseBlock != null)
                {
                    node.ElseBlock.Accept(this);
                }

                if (!_currentBlock.IsTerminated())
                {
                    EmitInstruction(new IRBranch(mergeBlock));
                }
            }

            // Continue with merge block
            _currentBlock = mergeBlock;
        }

        public void Visit(SelectStatementNode node)
        {
            TrackSourceLine(node);
            // Evaluate switch expression
            node.Expression.Accept(this);
            var switchValue = _expressionResult;

            // Use unique prefix for this switch statement
            var switchId = _switchCounter++;

            // Create blocks with unique names
            var defaultBlock = _currentFunction.CreateBlock($"switch{switchId}.default");
            var endBlock = _currentFunction.CreateBlock($"switch{switchId}.end");

            var switchInst = new IRSwitch(switchValue, defaultBlock);
            switchInst.EndBlock = endBlock;  // Store reference to end block

            // Generate case blocks
            var caseBlocks = new List<BasicBlock>();
            int caseIndex = 0;
            foreach (var caseClause in node.Cases)
            {
                if (caseClause.IsElse)
                {
                    // Default case
                    continue;
                }

                var caseBlock = _currentFunction.CreateBlock($"switch{switchId}_case_{caseIndex++}");
                caseBlocks.Add(caseBlock);

                // Add case values (simple constant matching)
                foreach (var caseValue in caseClause.Values)
                {
                    caseValue.Accept(this);
                    var value = _expressionResult;
                    switchInst.Cases.Add((value, caseBlock));
                }

                // Add pattern cases
                foreach (var pattern in caseClause.Patterns)
                {
                    var patternCase = ConvertPatternToIR(pattern, caseBlock);
                    if (patternCase != null)
                    {
                        switchInst.PatternCases.Add(patternCase);
                    }
                }
            }

            EmitInstruction(switchInst);

            // Generate case bodies
            int caseBlockIndex = 0;
            for (int i = 0; i < node.Cases.Count; i++)
            {
                var caseClause = node.Cases[i];

                if (caseClause.IsElse)
                {
                    _currentBlock = defaultBlock;
                }
                else
                {
                    _currentBlock = caseBlocks[caseBlockIndex++];
                }

                caseClause.Body.Accept(this);

                if (!_currentBlock.IsTerminated())
                {
                    EmitInstruction(new IRBranch(endBlock));
                }
            }

            // Default block (if no else case was provided)
            _currentBlock = defaultBlock;
            if (!_currentBlock.IsTerminated())
            {
                EmitInstruction(new IRBranch(endBlock));
            }

            _currentBlock = endBlock;
        }

        private IRPatternCase ConvertPatternToIR(PatternNode pattern, BasicBlock target)
        {
            IRPatternCase result = null;

            switch (pattern)
            {
                case TypePatternNode typePattern:
                    var typeCase = new IRTypePatternCase(
                        typePattern.MatchType?.Name ?? "Object",
                        target
                    );
                    typeCase.BindingVariable = typePattern.VariableName;
                    result = typeCase;
                    break;

                case RangePatternNode rangePattern:
                    rangePattern.LowerBound.Accept(this);
                    var lower = _expressionResult;
                    rangePattern.UpperBound.Accept(this);
                    var upper = _expressionResult;
                    result = new IRRangePatternCase(lower, upper, target);
                    break;

                case ComparisonPatternNode compPattern:
                    compPattern.Value.Accept(this);
                    var compValue = _expressionResult;
                    result = new IRComparisonPatternCase(compPattern.Operator, compValue, target);
                    break;

                case ConstantPatternNode constPattern:
                    constPattern.Value.Accept(this);
                    var constValue = _expressionResult;
                    result = new IRConstantPatternCase(constValue, target);
                    break;

                case NothingPatternNode:
                    result = new IRNothingPatternCase(target);
                    break;

                case OrPatternNode orPattern:
                    var orCase = new IROrPatternCase(target);
                    foreach (var alt in orPattern.Alternatives)
                    {
                        var altCase = ConvertPatternToIR(alt, target);
                        if (altCase != null)
                        {
                            orCase.Alternatives.Add(altCase);
                        }
                    }
                    result = orCase;
                    break;

                case TuplePatternNode tuplePattern:
                    var tupleCase = new IRTuplePatternCase(target);
                    foreach (var elem in tuplePattern.Elements)
                    {
                        var elemCase = ConvertPatternToIR(elem, target);
                        if (elemCase != null)
                        {
                            tupleCase.Elements.Add(elemCase);
                        }
                    }
                    result = tupleCase;
                    break;

                case BindingPatternNode bindingPattern:
                    var bindingCase = new IRBindingPatternCase(target);
                    bindingCase.BindingVariable = bindingPattern.VariableName;
                    result = bindingCase;
                    break;

                default:
                    return null;
            }

            // Handle When guard
            // Suppress instruction emission so optimization passes won't modify the When guard
            if (result != null && pattern.WhenGuard != null)
            {
                _suppressEmit = true;
                pattern.WhenGuard.Accept(this);
                _suppressEmit = false;
                result.WhenGuard = _expressionResult;
            }

            return result;
        }

        public void Visit(CaseClauseNode node)
        {
            // Handled in SelectStatementNode
        }

        /// <summary>
        /// Gets TypeInfo from a type name string for built-in types
        /// </summary>
        private TypeInfo GetTypeInfoFromName(string typeName)
        {
            return typeName?.ToLower() switch
            {
                "integer" => new TypeInfo("Integer", TypeKind.Primitive),
                "int" => new TypeInfo("Integer", TypeKind.Primitive),
                "int32" => new TypeInfo("Integer", TypeKind.Primitive),
                "long" => new TypeInfo("Long", TypeKind.Primitive),
                "int64" => new TypeInfo("Long", TypeKind.Primitive),
                "short" => new TypeInfo("Short", TypeKind.Primitive),
                "int16" => new TypeInfo("Short", TypeKind.Primitive),
                "byte" => new TypeInfo("Byte", TypeKind.Primitive),
                "single" => new TypeInfo("Single", TypeKind.Primitive),
                "float" => new TypeInfo("Single", TypeKind.Primitive),
                "double" => new TypeInfo("Double", TypeKind.Primitive),
                "decimal" => new TypeInfo("Decimal", TypeKind.Primitive),
                "boolean" => new TypeInfo("Boolean", TypeKind.Primitive),
                "bool" => new TypeInfo("Boolean", TypeKind.Primitive),
                "string" => new TypeInfo("String", TypeKind.Class),
                "object" => new TypeInfo("Object", TypeKind.Class),
                _ => null
            };
        }

        /// <summary>
        /// Tries to determine if a For loop step expression is negative at compile time.
        /// </summary>
        private bool IsNegativeStep(ExpressionNode stepExpr)
        {
            if (stepExpr == null)
                return false; // Default step is 1 (positive)

            // Check for unary minus on a literal: -2, -1, etc.
            if (stepExpr is UnaryExpressionNode unary && unary.Operator == "-")
            {
                // -<positive literal> is negative
                if (unary.Operand is LiteralExpressionNode innerLit)
                {
                    if (innerLit.Value is int i && i > 0) return true;
                    if (innerLit.Value is long l && l > 0) return true;
                    if (innerLit.Value is double d && d > 0) return true;
                    if (innerLit.Value is float f && f > 0) return true;
                }
                return true; // Assume negative for unary minus on unknown expression
            }

            // Check for negative literal directly (parser might create this)
            if (stepExpr is LiteralExpressionNode lit)
            {
                if (lit.Value is int i && i < 0) return true;
                if (lit.Value is long l && l < 0) return true;
                if (lit.Value is double d && d < 0) return true;
                if (lit.Value is float f && f < 0) return true;
            }

            return false; // Assume positive for unknown expressions
        }

        public void Visit(ForLoopNode node)
        {
            TrackSourceLine(node);
            // Create loop blocks (suffix keeps labels unique per loop — see ForEachLoopNode).
            var forId = _forCounter++;
            var condBlock = _currentFunction.CreateBlock($"for{forId}.cond");
            var bodyBlock = _currentFunction.CreateBlock($"for{forId}.body");
            var incBlock = _currentFunction.CreateBlock($"for{forId}.inc");
            var endBlock = _currentFunction.CreateBlock($"for{forId}.end");

            // Initialize loop variable
            node.Start.Accept(this);
            var startValue = _expressionResult;

            // Determine loop variable type - use inline type if specified, otherwise use start value type
            TypeInfo loopVarType = startValue.Type;
            if (!string.IsNullOrEmpty(node.VariableType))
            {
                loopVarType = GetTypeInfoFromName(node.VariableType) ?? startValue.Type;
                // Add to local variables since this is an inline declaration
                var localVar = new IRVariable(node.Variable, loopVarType, 0);
                if (!_currentFunction.LocalVariables.Any(v => v.Name == node.Variable))
                {
                    _currentFunction.LocalVariables.Add(localVar);
                }
            }

            var loopVar = GetOrCreateVariable(node.Variable, loopVarType);
            EmitInstruction(new IRAssignment(loopVar, startValue));

            // Jump to condition
            EmitInstruction(new IRBranch(condBlock));

            // Condition block
            _currentBlock = condBlock;
            node.End.Accept(this);
            var endValue = _expressionResult;

            var tempName = _currentFunction.GetNextTempName();
            var compareKind = IsNegativeStep(node.Step) ? CompareKind.Ge : CompareKind.Le;
            var cond = new IRCompare(tempName, compareKind, loopVar, endValue,
                new TypeInfo("Boolean", TypeKind.Primitive));
            EmitInstruction(cond);

            EmitInstruction(new IRConditionalBranch(cond, bodyBlock, endBlock));

            // Push loop context
            _loopStack.Push(new LoopContext(condBlock, endBlock));

            // Body block
            _currentBlock = bodyBlock;
            node.Body.Accept(this);

            if (!_currentBlock.IsTerminated())
            {
                EmitInstruction(new IRBranch(incBlock));
            }

            // Increment block
            _currentBlock = incBlock;
            IRValue stepValue;
            if (node.Step != null)
            {
                node.Step.Accept(this);
                stepValue = _expressionResult;
            }
            else
            {
                stepValue = new IRConstant(1, new TypeInfo("Integer", TypeKind.Primitive));
            }

            var incTemp = _currentFunction.GetNextTempName();
            var inc = new IRBinaryOp(incTemp, BinaryOpKind.Add, loopVar, stepValue, loopVar.Type);
            EmitInstruction(inc);

            var newLoopVar = CreateVariable(node.Variable, loopVar.Type, _nextVersion++);
            EmitInstruction(new IRAssignment(newLoopVar, inc));
            PushVariableVersion(node.Variable, newLoopVar);

            EmitInstruction(new IRBranch(condBlock));

            // Pop loop context
            _loopStack.Pop();
            PopVariableVersion(node.Variable);

            // Continue with end block
            _currentBlock = endBlock;
        }

        // ------------------------------------------------------------------------------
        // P2a-2 Task 9 (spec §8.5) — the two indexer producers. Both ask
        // SemanticAnalyzer.NetIndexerFor for the descriptor rather than deriving one: the
        // analyzer holds the resolver, and two derivations of one descriptor drift into two
        // mangled export names, which breaks §12.4's slots ≡ exports by construction.
        //
        // NetCategory is Unknown on purpose. An arbitrary resolved .NET type is Unknown to
        // BoundaryTypeRegistry BY DESIGN (§11.4), and Unknown is exactly what makes the
        // lowering's IsNativelyHandledCategory gate answer "route through the shim". Naming
        // ManagedOwned here would be a lie for every type outside the registry's five.
        // ------------------------------------------------------------------------------

        private bool TryEmitNetIndexerAccess(
            TypeInfo collectionType, ExpressionNode collectionExpr, List<ExpressionNode> indices)
        {
            var target = _semanticAnalyzer?.NetIndexerFor(collectionType, forWrite: false);
            if (target == null) return false;

            collectionExpr.Accept(this);
            var collection = _expressionResult;

            // Typed from the DESCRIPTOR's result type, never from the legacy element inference:
            // an Object-typed destination lowers to void*, and assigning an int32_t proxy
            // result into it is invalid C++ (and for a handle element, unsound).
            var elementType = _semanticAnalyzer.NetTypeInfoForResult(target.TypeFullName)
                              ?? _semanticAnalyzer.GetNodeType(collectionExpr)?.ElementType
                              ?? new TypeInfo("Object", TypeKind.Class);
            var access = new IRIndexerAccess(
                _currentFunction.GetNextTempName(), collection, elementType)
            {
                ResolvedNetTarget = target,
                ResolvedNetTargetIsExact = true,
                NetCategory = BoundaryTypeCategory.Unknown,
            };
            foreach (var index in indices)
            {
                index.Accept(this);
                access.Indices.Add(_expressionResult);
            }
            EmitInstruction(access);
            _expressionResult = access;
            return true;
        }

        private bool TryEmitNetIndexerStore(
            TypeInfo collectionType, ExpressionNode collectionExpr,
            List<ExpressionNode> indices, IRValue value)
        {
            var target = _semanticAnalyzer?.NetIndexerFor(collectionType, forWrite: true);
            if (target == null) return false;

            collectionExpr.Accept(this);
            var store = new IRIndexerStore(_expressionResult, value)
            {
                ResolvedNetTarget = target,
                ResolvedNetTargetIsExact = true,
                NetCategory = BoundaryTypeCategory.Unknown,
            };
            foreach (var index in indices)
            {
                index.Accept(this);
                store.Indices.Add(_expressionResult);
            }
            EmitInstruction(store);
            return true;
        }

        public void Visit(ForEachLoopNode node)
        {
            TrackSourceLine(node);
            // Evaluate collection expression
            node.Collection.Accept(this);
            var collection = _expressionResult;

            // Get element type from semantic analysis
            var elemType = _semanticAnalyzer.GetNodeType(node) ?? new TypeInfo("Object", TypeKind.Class);

            // Note: Don't add loop variable to LocalVariables - the foreach statement declares it

            // Create body and end blocks. The suffix makes labels unique per loop: without it,
            // two For Each loops in one function both emit a `foreach_end:` C++ label (C2045
            // "label redefined"). Backends that emit labels from block names (e.g. C++) rely on
            // this uniqueness; block Id alone isn't part of the emitted label.
            var suffix = _foreachCounter++;
            var bodyBlock = _currentFunction.CreateBlock($"foreach{suffix}.body");
            var endBlock = _currentFunction.CreateBlock($"foreach{suffix}.end");

            // Emit IRForEach instruction
            var forEach = new IRForEach(node.Variable, elemType, collection, bodyBlock, endBlock);

            // P2a-2 Task 9 (§8.5): a For Each over a HANDLE-represented .NET collection carries
            // the four IEnumerable<T>/IEnumerator<T> members the analyzer resolved. Absent for
            // every native collection, which is every program that existed before this.
            if (_semanticAnalyzer?.NetEnumerations != null
                && _semanticAnalyzer.NetEnumerations.TryGetValue(node, out var enumeration))
            {
                forEach.NetEnumeration = enumeration;
            }

            EmitInstruction(forEach);

            // Body block
            _currentBlock = bodyBlock;

            _loopStack.Push(new LoopContext(endBlock, endBlock));  // Continue goes to end (next iteration handled by foreach)
            node.Body.Accept(this);
            _loopStack.Pop();

            // Branch back to foreach (will be handled by C# foreach semantics)
            if (!_currentBlock.IsTerminated())
            {
                EmitInstruction(new IRBranch(endBlock));
            }

            // Continue with end block
            _currentBlock = endBlock;
        }

        // Stack of With object variables for nested With blocks
        private Stack<IRVariable> _withObjectStack = new Stack<IRVariable>();

        public void Visit(WithStatementNode node)
        {
            TrackSourceLine(node);
            // Evaluate the With object expression
            node.Object.Accept(this);
            var withObject = _expressionResult;

            // Store the object in a temporary variable for use in the body
            var objType = _semanticAnalyzer.GetNodeType(node.Object) ?? new TypeInfo("Object", TypeKind.Class);
            var withVar = CreateVariable("__with", objType, _nextVersion++);
            EmitInstruction(new IRAssignment(withVar, withObject));

            // Push the With variable for implicit member access
            _withObjectStack.Push(withVar);

            EmitInstruction(new IRComment("With block"));

            // Process the body
            node.Body.Accept(this);

            _withObjectStack.Pop();
            EmitInstruction(new IRComment("End With"));
        }

        public void Visit(ImplicitWithMemberNode node)
        {
            if (_withObjectStack.Count == 0)
            {
                // Error already reported by semantic analyzer
                _expressionResult = new IRConstant(null, new TypeInfo("Object", TypeKind.Class));
                return;
            }

            var withVar = _withObjectStack.Peek();
            var memberType = _semanticAnalyzer.GetNodeType(node) ?? new TypeInfo("Object", TypeKind.Class);

            // Create a field access for the implicit member
            var tempName = _currentFunction.GetNextTempName();
            var fieldAccess = new IRFieldAccess(tempName, withVar, node.MemberName, memberType);

            _expressionResult = fieldAccess;
        }

        public void Visit(WhileLoopNode node)
        {
            TrackSourceLine(node);
            var whileId = _whileCounter++;
            var condBlock = _currentFunction.CreateBlock($"while{whileId}.cond");
            var bodyBlock = _currentFunction.CreateBlock($"while{whileId}.body");
            var endBlock = _currentFunction.CreateBlock($"while{whileId}.end");

            EmitInstruction(new IRBranch(condBlock));

            // Condition block
            _currentBlock = condBlock;
            node.Condition.Accept(this);
            var condition = _expressionResult;
            EmitInstruction(new IRConditionalBranch(condition, bodyBlock, endBlock));

            // Body block
            _currentBlock = bodyBlock;
            _loopStack.Push(new LoopContext(condBlock, endBlock));
            node.Body.Accept(this);
            _loopStack.Pop();

            if (!_currentBlock.IsTerminated())
            {
                EmitInstruction(new IRBranch(condBlock));
            }

            // Continue with end block
            _currentBlock = endBlock;
        }

        public void Visit(DoLoopNode node)
        {
            TrackSourceLine(node);
            var doId = _doCounter++;
            var condBlock = _currentFunction.CreateBlock($"do{doId}.cond");
            var bodyBlock = _currentFunction.CreateBlock($"do{doId}.body");
            var endBlock = _currentFunction.CreateBlock($"do{doId}.end");

            if (node.IsConditionAtStart && node.Condition != null)
            {
                // Do While/Until ... Loop - condition at start (like a while loop)
                EmitInstruction(new IRBranch(condBlock));

                // Condition block
                _currentBlock = condBlock;
                node.Condition.Accept(this);
                var condition = _expressionResult;

                // For Until, swap true/false branches
                if (node.IsWhile)
                    EmitInstruction(new IRConditionalBranch(condition, bodyBlock, endBlock));
                else
                    EmitInstruction(new IRConditionalBranch(condition, endBlock, bodyBlock));

                // Body block
                _currentBlock = bodyBlock;
                _loopStack.Push(new LoopContext(condBlock, endBlock));
                node.Body.Accept(this);
                _loopStack.Pop();

                if (!_currentBlock.IsTerminated())
                {
                    EmitInstruction(new IRBranch(condBlock));
                }
            }
            else
            {
                // Do ... Loop While/Until - condition at end (or infinite loop)
                EmitInstruction(new IRBranch(bodyBlock));

                // Body block
                _currentBlock = bodyBlock;
                _loopStack.Push(new LoopContext(condBlock, endBlock));
                node.Body.Accept(this);
                _loopStack.Pop();

                if (!_currentBlock.IsTerminated())
                {
                    EmitInstruction(new IRBranch(condBlock));
                }

                // Condition block
                _currentBlock = condBlock;
                if (node.Condition != null)
                {
                    node.Condition.Accept(this);
                    var condition = _expressionResult;

                    // For Until, swap true/false branches
                    if (node.IsWhile)
                        EmitInstruction(new IRConditionalBranch(condition, bodyBlock, endBlock));
                    else
                        EmitInstruction(new IRConditionalBranch(condition, endBlock, bodyBlock));
                }
                else
                {
                    // Infinite loop
                    EmitInstruction(new IRBranch(bodyBlock));
                }
            }

            // Continue with end block
            _currentBlock = endBlock;
        }

        public void Visit(TryStatementNode node)
        {
            TrackSourceLine(node);
            // Create all blocks first (suffix keeps labels unique per Try — see ForEachLoopNode).
            var tryId = _tryCounter++;
            var tryBlock = _currentFunction.CreateBlock($"try{tryId}.body");
            var endBlock = _currentFunction.CreateBlock($"try{tryId}.end");

            // Create catch blocks and build IRCatchClause list
            var catchClauses = new List<IRCatchClause>();
            var catchBlockList = new List<(CatchClauseNode clause, BasicBlock block)>();
            int catchIdx = 0;
            foreach (var catchClause in node.CatchClauses)
            {
                var catchBlock = _currentFunction.CreateBlock($"try{tryId}.catch{catchIdx++}");
                var exceptionType = catchClause.ExceptionType != null
                    ? new TypeInfo(catchClause.ExceptionType.Name, TypeKind.Class)
                    : new TypeInfo("Exception", TypeKind.Class);
                var irClause = new IRCatchClause(exceptionType, catchClause.ExceptionVariable, catchBlock);

                // P2a-2 Task 4 (§11.1 ladder-trigger completion): the analyzer resolved this
                // clause's exception type as a .NET exception OUTSIDE the 12-name set — carry
                // the FQ name so the C++ ladder can emit its Matches(...) arm. Absent (null) for
                // the known names, user types, and resolver-less compilations.
                if (_semanticAnalyzer.NetResolvedExceptionTypes.TryGetValue(catchClause, out var netExceptionName))
                    irClause.NetExceptionFullName = netExceptionName;

                catchClauses.Add(irClause);
                catchBlockList.Add((catchClause, catchBlock));
            }

            // Create finally block if present
            BasicBlock finallyBlock = null;
            if (node.FinallyBlock != null)
            {
                finallyBlock = _currentFunction.CreateBlock($"try{tryId}.finally");
            }

            // Emit the try-catch instruction in the current block
            EmitInstruction(new IRTryCatch(tryBlock, catchClauses, finallyBlock, endBlock));

            // Generate try block body
            _currentBlock = tryBlock;
            node.TryBlock.Accept(this);
            if (!_currentBlock.IsTerminated())
            {
                EmitInstruction(new IRBranch(endBlock));
            }

            // Generate catch block bodies
            foreach (var (catchClause, catchBlock) in catchBlockList)
            {
                _currentBlock = catchBlock;

                // Declare the exception variable in scope if present
                var exceptionType = catchClause.ExceptionType != null
                    ? new TypeInfo(catchClause.ExceptionType.Name, TypeKind.Class)
                    : new TypeInfo("Exception", TypeKind.Class);
                if (!string.IsNullOrEmpty(catchClause.ExceptionVariable))
                {
                    // Create a local variable for the exception
                    var exVar = new IRVariable(catchClause.ExceptionVariable, exceptionType);
                    // Push onto variable versions stack so it's accessible in the catch block
                    if (!_variableVersions.ContainsKey(catchClause.ExceptionVariable))
                    {
                        _variableVersions[catchClause.ExceptionVariable] = new Stack<IRVariable>();
                    }
                    _variableVersions[catchClause.ExceptionVariable].Push(exVar);
                }

                catchClause.Body.Accept(this);

                // Pop the exception variable
                if (!string.IsNullOrEmpty(catchClause.ExceptionVariable) &&
                    _variableVersions.ContainsKey(catchClause.ExceptionVariable))
                {
                    _variableVersions[catchClause.ExceptionVariable].Pop();
                }

                if (!_currentBlock.IsTerminated())
                {
                    EmitInstruction(new IRBranch(endBlock));
                }
            }

            // Generate finally block body if present
            if (finallyBlock != null)
            {
                _currentBlock = finallyBlock;
                node.FinallyBlock.Accept(this);
                if (!_currentBlock.IsTerminated())
                {
                    EmitInstruction(new IRBranch(endBlock));
                }
            }

            _currentBlock = endBlock;
        }

        public void Visit(CatchClauseNode node)
        {
            // Handled in TryStatementNode
        }

        public void Visit(ThrowStatementNode node)
        {
            TrackSourceLine(node);
            if (node.Exception != null)
            {
                node.Exception.Accept(this);
                EmitInstruction(new IRThrow(_expressionResult));
            }
            else
            {
                // Bare Throw inside a Catch block: rethrow
                EmitInstruction(new IRThrow(null));
            }
        }

        public void Visit(ReturnStatementNode node)
        {
            TrackSourceLine(node);
            if (node.Value != null)
            {
                node.Value.Accept(this);
                EmitInstruction(new IRReturn(CoerceToDeclaredReturnType(_expressionResult)));
            }
            else
            {
                EmitInstruction(new IRReturn());
            }
        }

        /// <summary>
        /// Narrows or widens a returned value to the function's DECLARED return type, when both
        /// are numeric and disagree.
        ///
        /// <para>⛔ VB's <c>/</c> is always floating-point division, so <c>Return v / 2</c> from a
        /// <c>Function … As Integer</c> hands back a Double where an Integer was promised. Nothing
        /// inserted the conversion, and what each backend then did with it was measured, not
        /// assumed:</para>
        /// <list type="bullet">
        /// <item>C# emitted <c>return (double)(v) / (double)(2);</c> from an <c>int</c> method —
        /// <b>CS0266, does not compile</b>. The BasicLang build still reported success, because it
        /// only writes the source; nothing invokes csc.</item>
        /// <item>MSIL emitted <c>ret</c> with a float64 on the stack from an int32 method and
        /// returned <b>0</b> — a silent wrong answer.</item>
        /// <item>C++ and JavaScript happened to be RIGHT, and neither because the compiler did
        /// anything: C++ narrows implicitly on return, and JavaScript has no types to disagree
        /// about. Do not read their passing as evidence this seam was ever correct.</item>
        /// </list>
        ///
        /// <para><see cref="IRCast"/> is the seam for exactly the reason
        /// <see cref="WidenDivisionOperand"/> gives: every backend, both interpreters,
        /// <c>IROperandWalker</c>, <c>IRPrettyPrinter</c> and <c>CppCapabilityChecker</c> already
        /// handle it, so one insertion here moves every consumer instead of repeating the coercion
        /// four times.</para>
        ///
        /// <para>⚠ The ROUNDING MODE is left to each backend's existing cast rendering, and
        /// measured rather than assumed: <c>Return v / 2</c> from an <c>As Integer</c> function
        /// now gives 3 for v=7 on ALL FOUR backends — C# renders the cast as <c>(int)</c>, C++ as
        /// a narrowing conversion, JS as <c>Math.trunc</c>, MSIL as <c>conv.i4</c>. Uniform
        /// truncation.</para>
        ///
        /// <para>⛔ That does NOT match VB.NET, and it does not even match this compiler's own
        /// <c>CInt</c> everywhere. VB narrows with banker's rounding, so <c>Return 7 / 2</c>
        /// should be 4. And C# is internally inconsistent: <c>CInt(3.5)</c> emits
        /// <c>Convert.ToInt32</c> and gives 4 while an implicit return gives 3, where C++, JS and
        /// MSIL give 3 for both. Reconciling the two is a decision about the whole narrowing
        /// surface — every <c>IRCast</c> rendering on four backends — and is deliberately NOT made
        /// here. What this fixes is that an implicit narrowing happens AT ALL; before it, the same
        /// program did not compile on C# and returned 0 on MSIL.</para>
        ///
        /// <para>Restricted to numeric primitives on both sides, so a <c>Task(Of Integer)</c>, an
        /// <c>IEnumerable(Of T)</c>, <c>Object</c>, a String or a class type is never touched —
        /// boxing and generic returns keep whatever handling they already had.</para>
        /// </summary>
        private IRValue CoerceToDeclaredReturnType(IRValue value) =>
            CoerceToDeclaredType(value, _currentFunction?.ReturnType);

        /// <summary>
        /// The shared numeric coercion: narrows or widens <paramref name="value"/> to
        /// <paramref name="declared"/> when both are numeric and disagree, and returns it
        /// unchanged otherwise.
        ///
        /// <para>⛔ Used at STORE sites as well as returns, and the store half is not a mirror of
        /// the return half — it was measured separately. <c>Dim d As Integer = 7 / 2</c>,
        /// <c>e = 7 / 2</c>, <c>a(0) = 7 / 2</c> and a module-level <c>G = 7 / 2</c> each failed
        /// differently: five CS0266s on C# (it does not build), <c>3.5</c> at every site on
        /// JavaScript, and on MSIL <c>dim=1074528256 asn=0 arr=0 glob=0</c> followed by a
        /// SEGFAULT — raw float64 bit patterns read as int32. C++ alone was right, by narrowing
        /// implicitly.</para>
        ///
        /// <para>⚠ At an assignment the declared type comes from the TARGET NODE, not from the
        /// target variable: <c>GetOrCreateVariable</c> is handed <c>value.Type</c>, and
        /// <see cref="TryRenameToVariable"/> then renames the Double temp to the target outright,
        /// so the local's declared Integer never enters the picture. Coercing first is what puts
        /// it back — and it also stops the rename, because an <c>IRCast</c> is not one of the
        /// node kinds that helper will rename.</para>
        /// </summary>
        private IRValue CoerceToDeclaredType(IRValue value, TypeInfo declared)
        {
            var actual = value?.Type;

            // ⚠ ONE guard, not two. An earlier version also tested a broad
            // `IsNumericPrimitive` (any integral or floating type) before this; it is redundant,
            // because every type this admits is one that would admit — and a mutation removing it
            // killed nothing. Narrow numeric targets (Byte/SByte/Short/unsigned) fall out here and
            // keep exactly what they did before this coercion existed: see TryConvertConstant for
            // why the optimizer cannot be handed their CLR types, and note that an IRCast is no
            // safer for them — it would change the emission of shapes that already work
            // (`Dim b As Byte = 65` is a plain literal today) to buy a case nothing measured as
            // broken. String, Object, class and generic targets fall out here too.
            if (!IsFoldableNumeric(declared) || !IsFoldableNumeric(actual)) return value;
            if (string.Equals(declared.Name, actual.Name, StringComparison.Ordinal)) return value;

            // ⛔ A LITERAL is re-typed in place rather than wrapped. Wrapping regressed
            // PropertySet_LowersToTheSynthesizedSetterSlot: `st.Position = 5` writes an Integer
            // literal to a Long property — a legal widening — and the cast turned the pinned
            // proxy call `…(st, 5)` into a call on a cast temp. Converting the constant is also
            // strictly better code: there is no run-time conversion to perform, and on MSIL it
            // makes `Dim w As Double = 7` emit `ldc.r8` instead of an int32 bit pattern that
            // needs a `conv.r8` to rescue it.
            if (value is IRConstant constant && TryConvertConstant(constant.Value, declared) is object converted)
            {
                return new IRConstant(converted, declared);
            }

            var castName = _currentFunction.GetNextTempName();
            var cast = new IRCast(castName, value, actual, declared,
                                  DetermineCastKind(actual, declared));
            EmitInstruction(cast);
            return cast;
        }

        /// <summary>
        /// Coerces one ARGUMENT to the declared type of the parameter it fills.
        ///
        /// <para>⛔ Measured: <c>Take(7 / 2)</c> where the parameter is Integer was <b>CS1503</b>
        /// on C# (does not build), <c>MissingMethodException: Take(Double)</c> on MSIL — the call
        /// site spells its signature from the ARGUMENT's type — and <c>3.5</c> on JavaScript. Same
        /// four backends, same split as the return and store cases; C++ alone was right.</para>
        ///
        /// <para>⚠ <b>ByRef is skipped.</b> A coerced argument is a NEW value, so a
        /// <c>ByRef</c> parameter would write back into a temporary and the caller's variable
        /// would never change — trading a build error for a silently dropped mutation. An index
        /// past the parameter list (an omitted Optional) has no declared type to read at all.</para>
        ///
        /// <para>⛔ There is deliberately NO ParamArray clause, though one was written here first.
        /// <c>ParamArray</c> does not parse in either spelling — <c>ParamArray xs() As Integer</c>
        /// and <c>ParamArray xs As Integer()</c> are both syntax errors — so the clause could not
        /// be tested; and it would be redundant even if it could, because an array-typed parameter
        /// is already rejected by <see cref="IsFoldableNumeric"/>.</para>
        ///
        /// <para>The callee's parameters come from the symbol the analyzer ALREADY resolved for
        /// this call — the same list both loops read <c>IsByRef</c> from. No overload selection is
        /// re-done here; if the analyzer picked an overload, this coerces to that overload's
        /// parameter, which is also what makes the emitted C# re-select the same one.</para>
        /// </summary>
        private IRValue CoerceToParameterType(IRValue value, Symbol callee, int index)
        {
            var parameters = callee?.Parameters;
            if (parameters == null || index < 0 || index >= parameters.Count) return value;

            var parameter = parameters[index];
            if (parameter.IsByRef) return value;

            return CoerceToDeclaredType(value, parameter.Type);
        }

        /// <summary>
        /// Appends the declared defaults for any trailing <c>Optional</c> parameters the call did
        /// not supply.
        ///
        /// <para>⛔ Only the C# backend handled an omitted Optional, and only because it emits the
        /// default into the SIGNATURE (<c>int b = 5</c>) and lets csc fill it. Measured for
        /// <c>Sub One(a As Integer, Optional b As Integer = 5)</c> called as <c>One(1)</c>:
        /// JavaScript printed <c>one:1,undefined</c> (the function is <c>function One(a, b)</c>,
        /// no default), C++ did not compile ("no matching function for call to 'One'"), and MSIL
        /// could not bind — <c>MissingMethodException: One(Int32)</c>. Filling at the CALL is what
        /// moves all three at once; the emitted C# then passes the value explicitly, which is the
        /// same program.</para>
        ///
        /// <para>⚠ TRAILING only, which is all the language can express: BasicLang has no
        /// named-argument or skipped-argument syntax, so an omitted Optional is always at the end.
        /// The defaults are appended in declaration order and coerced like any other argument, so
        /// <c>Optional d As Double = 7</c> arrives as a Double rather than an int32 bit
        /// pattern.</para>
        ///
        /// <para>⚠ The early returns are FAIL-SAFE and none can be killed by a test today, which
        /// is why they are returns and not <c>continue</c>s. <c>DefaultValueExpression</c> is
        /// populated only from a <c>ParameterNode</c>, and the parser marks a parameter Optional
        /// whenever it parses a default — the sole exception, a <c>ParamArray</c> WITH a default,
        /// does not parse at all ("Expected 'As' but found LeftParen"). So a parameter that is not
        /// Optional never has one recorded, and both guards are reached only if the front end
        /// changes. Kept in that form deliberately: stopping makes this do NOTHING on a shape it
        /// has not been taught, where skipping ahead would silently misalign the argument
        /// list.</para>
        /// </summary>
        private void AppendOmittedOptionalArguments(
            List<IRValue> arguments, List<bool> byRefFlags, Symbol callee)
        {
            var parameters = callee?.Parameters;
            if (parameters == null) return;

            for (var i = arguments.Count; i < parameters.Count; i++)
            {
                var parameter = parameters[i];
                if (!parameter.IsOptional) return;
                if (!(parameter.DefaultValueExpression is ExpressionNode expression)) return;

                var value = BuildExpressionValue(expression);
                if (value == null) return;

                arguments.Add(CoerceToDeclaredType(value, parameter.Type));

                // ⚠ Always by VALUE. A filled default is a fresh temporary, so there is nothing
                // for a callee to write back into — and IRCall documents ByRefArguments as indexed
                // in lockstep with Arguments, so the entry has to exist either way.
                //
                // ⚠ NULL for a construction: IRNewObject and IRConstructor.BaseConstructorArgs
                // carry no by-ref list at all, so there is no lockstep to keep.
                byRefFlags?.Add(false);
            }
        }

        /// <summary>
        /// Narrows a module-scope constant to a NARROW numeric declared type — Byte, SByte,
        /// Short, UShort, UInteger — that <see cref="CoerceToDeclaredType"/> deliberately leaves
        /// alone.
        ///
        /// <para>⛔ Without it, `Dim v As Byte = 7.9` at module scope compiled clean and printed
        /// <b>154</b>. <c>CoerceToDeclaredType</c> declines these types on purpose: admitting them
        /// there would put an <c>IRCast</c> in front of LOCAL declarations that already work
        /// (`Dim b As Byte = 65` is a plain literal today), and its <c>TryConvertConstant</c>
        /// cannot return their CLR types at all — measured and still true,
        /// <c>IROptimizer.CompareLt</c> answers <b>false</b> for any pair outside
        /// int/long/float/double, so folding `lo &lt; hi` on two <c>sbyte</c>s silently drops the
        /// branch. So the narrowing happens HERE, on the module-scope path only, where the value
        /// must already be a constant and no local emission can change.</para>
        ///
        /// <para>⚠ The result keeps an <c>int</c> or <c>long</c> CLR value and carries the narrow
        /// type in its <c>TypeInfo</c>. That is what makes it safe: the folders stay inside the
        /// four types they handle, while the backends see the declared width.</para>
        ///
        /// <para>⚠ WRAPS rather than refusing out of range, because that is what the LOCAL path
        /// already does — measured: `Dim v As Byte = 300` prints <b>44</b>, `= -1` prints
        /// <b>255</b>, `Dim v As SByte = 200` prints <b>-56</b>. Real VB rejects those
        /// (BC30439); this compiler does not, and a module-scope declaration disagreeing with the
        /// identical local one would be a worse bug than either answer.</para>
        ///
        /// <para>⛔ ULong is NOT narrowed and is refused instead: its range does not fit the
        /// <c>long</c> the folders can carry, and there is no representation that is both correct
        /// and safe for them. A clean refusal beats the 4620580627691444634 it printed before.</para>
        /// </summary>
        private IRValue NarrowModuleScopeConstant(
            IRValue value, TypeInfo declared, string name, string what)
        {
            if (!(value is IRConstant constant) || declared?.Name == null) return value;
            if (string.Equals(declared.Name, value.Type?.Name, StringComparison.Ordinal)) return value;

            double asDouble;
            switch (constant.Value)
            {
                case int i: asDouble = i; break;
                case long l: asDouble = l; break;
                case short sh: asDouble = sh; break;
                case byte b: asDouble = b; break;
                case sbyte sb: asDouble = sb; break;
                case float f: asDouble = f; break;
                case double d: asDouble = d; break;
                default: return value;
            }

            var truncated = (long)Math.Round(asDouble, MidpointRounding.ToEven);
            object narrowed;
            switch (declared.Name)
            {
                case "Byte": narrowed = (int)unchecked((byte)truncated); break;
                case "SByte": narrowed = (int)unchecked((sbyte)truncated); break;
                case "Short": narrowed = (int)unchecked((short)truncated); break;
                case "UShort": narrowed = (int)unchecked((ushort)truncated); break;
                // ⚠ int WHEN IT FITS, long only when it must. UInteger's range needs a long in
                // general, but handing the JavaScript backend a Long literal makes it refuse the
                // whole program (BL7003: a JS number is exact only to 2^53) — measured, that
                // turned `Dim v As UInteger = 7.9` into a build failure on JS. The narrowest
                // folder-safe type that represents the value exactly keeps every backend able to
                // emit the ordinary cases.
                case "UInteger":
                    var unsigned = unchecked((uint)truncated);
                    narrowed = unsigned <= int.MaxValue ? (object)(int)unsigned : (long)unsigned;
                    break;
                case "ULong":
                    throw new Exception(
                        $"Line {_currentSourceLine}: the module-level {what} '{name}' is declared "
                        + "ULong, whose range cannot be represented in a compile-time constant "
                        + "here. Declare it Long, or assign it in Main (or another procedure).");
                default: return value;
            }

            return new IRConstant(narrowed, declared);
        }

        /// <summary>
        /// A numeric literal converted to <paramref name="declared"/> at COMPILE time, or null
        /// when it cannot be.
        ///
        /// <para>⚠ Narrowing ROUNDS HALF-TO-EVEN, which is what VB does. This note used to say it
        /// TRUNCATES "to match what the run-time cast does on all four backends" — that reasoning
        /// was right and its premise has changed: the run-time cast now rounds on all four too, so
        /// truncating here would recreate exactly the split it was avoiding. The constant-folded
        /// and non-folded paths of one expression must agree, and they now agree on 8 for
        /// <c>Dim d As Integer = 7.9</c>.</para>
        /// </summary>
        private static object TryConvertConstant(object value, TypeInfo declared)
        {
            if (value == null) return null;

            double asDouble;
            switch (value)
            {
                case int i: asDouble = i; break;
                case long l: asDouble = l; break;
                case short sh: asDouble = sh; break;
                case byte b: asDouble = b; break;
                case sbyte sb: asDouble = sb; break;
                case float f: asDouble = f; break;
                case double d: asDouble = d; break;
                default: return null;
            }

            // ⛔ ONLY these four CLR types. `IROptimizer`'s folders — FoldAdd, CompareLt,
            // CompareGt and friends — are written against int/long/float/double and nothing else,
            // and its own comment says CompareLt/CompareGt "blindly report FALSE for type pairs
            // outside int/long/float/double". Handing them an `sbyte` is therefore not a missing
            // optimization, it is a MISCOMPILE: measured, retyping `Dim lo As SByte = -3` folded
            // `lo < hi` to `if (false)` and silently dropped the branch body.
            //
            // A Byte/SByte/Short/unsigned target keeps whatever it did before this coercion
            // existed — nothing. That leaves `Dim b As Byte = 7.9` unnarrowed, which is a real
            // gap; closing it means teaching the optimizer's folders every numeric CLR type,
            // which is its own change with its own blast radius.
            var truncated = Math.Round(asDouble, MidpointRounding.ToEven);
            switch (declared.Name)
            {
                case "Double": return asDouble;
                case "Single": return (float)asDouble;
                case "Long": return (long)truncated;
                case "Integer": return (int)truncated;
                default: return null;
            }
        }

        /// <summary>
        /// A numeric type whose CLR representation <c>IROptimizer</c>'s constant folders handle.
        /// Everything narrower is left uncoerced rather than miscompiled — see
        /// <see cref="TryConvertConstant"/>.
        /// </summary>
        private static bool IsFoldableNumeric(TypeInfo type) => type?.Name switch
        {
            "Integer" or "Long" or "Single" or "Double" => true,
            _ => false,
        };

        public void Visit(ExitStatementNode node)
        {
            TrackSourceLine(node);
            switch (node.Kind)
            {
                case ExitKind.For:
                case ExitKind.Do:
                case ExitKind.While:
                    // Jump to the break target of the current loop
                    if (_loopStack.Count == 0)
                    {
                        throw new Exception($"Exit {node.Kind} outside of loop");
                    }
                    var loopContext = _loopStack.Peek();
                    // ⛔ IsLoopExit is the whole point. This branch and the one that ends an
                    // ordinary iteration both target BreakTarget and are otherwise identical, so
                    // this is the ONLY place the distinction still exists. Dropping it forced
                    // every backend to guess it back from block position — and the C++ backend
                    // guessed `continue;`, turning Exit For into Continue For (task_4cc381f1).
                    EmitInstruction(new IRBranch(loopContext.BreakTarget) { IsLoopExit = true });
                    break;

                case ExitKind.Sub:
                case ExitKind.Function:
                    // Exit Sub/Function is like Return (without value for Sub)
                    EmitInstruction(new IRReturn());
                    break;
            }
        }

        public void Visit(AssignmentStatementNode node)
        {
            TrackSourceLine(node);
            // Evaluate right-hand side
            node.Value.Accept(this);
            var value = _expressionResult;

            // Handle compound assignments
            if (node.Operator != "=")
            {
                // Load current value
                node.Target.Accept(this);
                var currentValue = _expressionResult;

                // Determine operation
                BinaryOpKind op = node.Operator switch
                {
                    "+=" or "=+" => BinaryOpKind.Add,
                    "-=" or "=-" => BinaryOpKind.Sub,
                    "*=" => BinaryOpKind.Mul,
                    "/=" => BinaryOpKind.Div,
                    "\\=" => BinaryOpKind.IntDiv,        // Integer division assignment
                    "%=" or "Mod=" => BinaryOpKind.Mod,  // Modulo assignment
                    "&=" => BinaryOpKind.Concat,         // String concatenation assignment
                    // NON-short-circuit, which is right for a compound assignment: `a And= b`
                    // must evaluate b. (The old "Bitwise" comments were stale — SemanticAnalyzer
                    // rejects integral operands for And/Or, so these are Boolean-only here.)
                    "And=" => BinaryOpKind.And,
                    "Or=" => BinaryOpKind.Or,
                    "Xor=" => BinaryOpKind.Xor,          // Bitwise XOR assignment
                    "<<=" => BinaryOpKind.Shl,           // Left shift assignment
                    ">>=" => BinaryOpKind.Shr,           // Right shift assignment
                    _ => throw new Exception($"Unknown assignment operator: {node.Operator}")
                };

                // ⛔ `/=` is FLOATING division, exactly as binary `/` is, and typing the result
                // from the target (`currentValue.Type`) made it Integer — so the coercion below
                // saw no mismatch and did nothing, while the optimizer later constant-folded
                // `n /= 4` on an Integer 10 to the Double 2.5. Measured: C# emitted `n = 2.5;`
                // (CS0266) and JavaScript printed 2.5 from a variable declared As Integer.
                // Widening the OPERANDS is the same fix, and the same reasoning, as
                // WidenDivisionOperand in Visit(BinaryExpressionNode) — a Double-typed result
                // over two Integer operands still divides as integers on the C-family backends.
                // `\=` (IntDiv) is excluded there and is excluded here: it must keep truncating.
                var resultType = currentValue.Type;
                if (op == BinaryOpKind.Div)
                {
                    resultType = new TypeInfo("Double", TypeKind.Primitive);
                    currentValue = WidenDivisionOperand(currentValue, resultType);
                    value = WidenDivisionOperand(value, resultType);
                }

                var tempName = _currentFunction.GetNextTempName();
                var result = new IRBinaryOp(tempName, op, currentValue, value, resultType);
                EmitInstruction(result);
                value = result;
            }

            // ⛔ AFTER the compound fold, so `n /= 2` on an Integer is narrowed too, and BEFORE
            // every target arm, so the identifier, field, array-element and indexer stores all
            // get it from one place. The target NODE's type is the declared one for all four.
            value = CoerceToDeclaredType(value, _semanticAnalyzer.GetNodeType(node.Target));

            // Store to target
            if (node.Target is IdentifierExpressionNode idExpr && idExpr.IsForeignQualified)
            {
                // A `::`-qualified foreign GLOBAL is an opaque target: no local is created for
                // it, it is typed Foreign so every backend renders the name verbatim, and the
                // value always flows through an IRAssignment. Reachable since the analyzer
                // admits a Foreign target (plan 2 Task 7); found by review — it used to become
                // an Integer-typed local named "::counter", which the C++ backend sanitised to
                // `counter = 5;` and so wrote a same-named LOCAL instead of the global.
                var foreignTarget = new IRVariable(idExpr.Name, new TypeInfo(idExpr.Name, TypeKind.Foreign));
                EmitInstruction(new IRAssignment(foreignTarget, value));
            }
            else if (node.Target is IdentifierExpressionNode idExpr2)
            {
                // Check if this identifier is an imported symbol from another module
                var symbol = _semanticAnalyzer.GetNodeSymbol(idExpr2);

                IRVariable targetVar;
                if (ModuleMemberSymbolOf(idExpr2) is Symbol moduleMember)
                {
                    // A Module's variable, its own or another's — the same global the read binds.
                    targetVar = GlobalReference(moduleMember.Name, moduleMember.OwningModule, value.Type);
                }
                else if (symbol != null && symbol.IsImported && !string.IsNullOrEmpty(symbol.SourceModule))
                {
                    // This is an imported variable from another module
                    targetVar = new IRVariable(idExpr2.Name, value.Type);
                    targetVar.IsGlobal = true;
                    targetVar.ModuleName = symbol.SourceModule;
                }
                else
                {
                    targetVar = GetOrCreateVariable(idExpr2.Name, value.Type);
                }

                // Optimization: rename a fresh result to the target instead of a separate
                // assignment — with the same guards as the declaration path.
                if (!TryRenameToVariable(value, targetVar))
                {
                    // For constants, variables, or other values, emit an assignment
                    EmitInstruction(new IRAssignment(targetVar, value));
                }
            }
            else if (node.Target is MemberAccessExpressionNode moduleMemberExpr
                     && ModuleMemberSymbolOf(moduleMemberExpr) is Symbol moduleMemberTarget)
            {
                // `Helpers.Value = 13`: a write to a Module's variable is an assignment to that
                // global — the SAME one the read binds, so the two cannot disagree about where it
                // lives. Never an IRFieldStore on a phantom receiver ("cannot use arrow operator
                // on a type" was the C++ reading of that).
                var targetVar = GlobalReference(moduleMemberTarget.Name, moduleMemberTarget.OwningModule, value.Type);
                if (!TryRenameToVariable(value, targetVar))
                {
                    EmitInstruction(new IRAssignment(targetVar, value));
                }
            }
            else if (node.Target is MemberAccessExpressionNode memberExpr)
            {
                // Handle member assignment (both properties and fields use field store syntax in C#)
                memberExpr.Object.Accept(this);
                var obj = _expressionResult;

                var fieldStore = new IRFieldStore(obj, memberExpr.MemberName, value);

                // P2a-2 Task 7a: a .NET PROPERTY/FIELD write carries the SYNTHESIZED set_X
                // accessor-method descriptor (NetAccessorSynthesis — the single synthesis
                // point; the surface collector and the C++ lowering both mangle exactly this
                // stamp, which is what keeps §12.4's slots ≡ exports). The analyzer records
                // property/field annotations as EXACT (no overload axis), so a non-exact
                // record here can only be a foreign shape — left unstamped.
                // HasSynthesizableSetter keeps INDEXERS away from the synthesis point (which
                // refuses them): §8.5's get_Item/set_Item pair is Task 9's. ONE copy of that
                // predicate — the analyzer's refusal and §11.3's attribution ask the same
                // question, and the three disagreeing is a shim that fails to compile.
                if (_semanticAnalyzer.NetMemberAnnotations.TryGetValue(memberExpr, out var netWrite)
                    && netWrite.Exact
                    && BasicLang.Net.NetAccessorSynthesis.HasSynthesizableSetter(netWrite.Member))
                {
                    fieldStore.ResolvedNetTarget =
                        BasicLang.Net.NetAccessorSynthesis.SetterFor(netWrite.Member);
                    fieldStore.ResolvedNetTargetIsExact = true;
                    var receiverTypeName = _semanticAnalyzer.GetNodeType(memberExpr.Object)?.Name;
                    fieldStore.NetCategory = !string.IsNullOrEmpty(receiverTypeName)
                        ? BoundaryTypeRegistry.Categorize(receiverTypeName)
                        : BoundaryTypeCategory.Unknown;
                }

                EmitInstruction(fieldStore);
            }
            else if (node.Target is ArrayAccessExpressionNode arrayExpr)
            {
                // Bracket-indexed write: `x[i] = v`. If the receiver is an indexable generic
                // collection (List/Dictionary), lower to IRIndexerStore so backends can honor
                // per-collection write semantics (e.g. Dictionary insert-or-update). Otherwise
                // it's a raw array store via element pointer.
                var arrayType = _semanticAnalyzer.GetNodeType(arrayExpr.Array);
                // §8.5 FIRST: a handle-represented .NET array/collection is neither a native
                // shared_ptr collection nor a std::vector — it has no storage to index. Tested
                // ahead of both branches for the same reason MapType tests the marker first.
                if (TryEmitNetIndexerStore(arrayType, arrayExpr.Array, arrayExpr.Indices, value))
                {
                    // stamped IRIndexerStore emitted
                }
                else if (arrayType != null && IsIndexableGenericType(arrayType))
                {
                    arrayExpr.Array.Accept(this);
                    var collection = _expressionResult;
                    var indexerStore = new IRIndexerStore(collection, value);
                    foreach (var index in arrayExpr.Indices)
                    {
                        index.Accept(this);
                        indexerStore.Indices.Add(_expressionResult);
                    }
                    EmitInstruction(indexerStore);
                }
                else
                {
                    // Handle array element assignment
                    arrayExpr.Array.Accept(this);
                    var array = _expressionResult;

                    // Get element pointer
                    var gepTemp = _currentFunction.GetNextTempName();
                    var gep = new IRGetElementPtr(gepTemp, array, value.Type);
                    foreach (var index in arrayExpr.Indices)
                    {
                        index.Accept(this);
                        gep.Indices.Add(_expressionResult);
                    }
                    EmitInstruction(gep);

                    // Store to pointer
                    EmitInstruction(new IRStore(value, gep));
                }
            }
            else if (node.Target is CallExpressionNode itemCallTarget
                     && itemCallTarget.Callee is MemberAccessExpressionNode itemTarget
                     && itemCallTarget.Arguments.Count > 0
                     && string.Equals(itemTarget.MemberName, "Item", StringComparison.OrdinalIgnoreCase)
                     && IsIndexableGenericType(_semanticAnalyzer.GetNodeType(itemTarget.Object)))
            {
                // Explicit VB indexer WRITE `l.Item(i) = v` on a collection: lower to the same
                // IRIndexerStore as `l(i) = v` (List -> (*l)[i] = v; Dictionary -> l->Set(k, v)).
                // Otherwise it degraded to a field access + array store on a nonexistent member.
                itemTarget.Object.Accept(this);
                var collection = _expressionResult;
                var indexerStore = new IRIndexerStore(collection, value);
                foreach (var index in itemCallTarget.Arguments)
                {
                    index.Accept(this);
                    indexerStore.Indices.Add(_expressionResult);
                }
                EmitInstruction(indexerStore);
            }
            else if (node.Target is CallExpressionNode callTarget && callTarget.Arguments.Count > 0)
            {
                // VB-style paren-indexed write: `coll(i) = v` / `dict(k) = v`. Because VB uses
                // PARENS for indexing, the parser produces a CallExpressionNode on the LHS.
                // When the callee is an indexable generic collection, lower to IRIndexerStore.
                // (Without this, such assignments were silently DROPPED — see Spike 1b.)
                var calleeType = _semanticAnalyzer.GetNodeType(callTarget.Callee);
                if (TryEmitNetIndexerStore(calleeType, callTarget.Callee, callTarget.Arguments, value))
                {
                    // §8.5 stamped IRIndexerStore emitted — see the ArrayAccess branch above.
                }
                else if (calleeType != null && IsIndexableGenericType(calleeType))
                {
                    callTarget.Callee.Accept(this);
                    var collection = _expressionResult;
                    var indexerStore = new IRIndexerStore(collection, value);
                    foreach (var index in callTarget.Arguments)
                    {
                        index.Accept(this);
                        indexerStore.Indices.Add(_expressionResult);
                    }
                    EmitInstruction(indexerStore);
                }
                else
                {
                    // Not a known indexable target: fall back to array-style store so behavior
                    // is at least defined (raw array element pointer + store).
                    callTarget.Callee.Accept(this);
                    var array = _expressionResult;
                    var gepTemp = _currentFunction.GetNextTempName();
                    var gep = new IRGetElementPtr(gepTemp, array, value.Type);
                    foreach (var index in callTarget.Arguments)
                    {
                        index.Accept(this);
                        gep.Indices.Add(_expressionResult);
                    }
                    EmitInstruction(gep);
                    EmitInstruction(new IRStore(value, gep));
                }
            }
        }

        public void Visit(ExpressionStatementNode node)
        {
            TrackSourceLine(node);
            node.Expression.Accept(this);
        }

        // ====================================================================
        // Expressions
        // ====================================================================

        private IRValue _expressionResult;

        /// <summary>
        /// Build an expression and return the result. Used for default parameter values.
        /// </summary>
        private IRValue BuildExpressionValue(ExpressionNode expr)
        {
            if (expr == null) return null;

            // Handle literal expressions directly
            if (expr is LiteralExpressionNode literal)
            {
                var type = _semanticAnalyzer.GetNodeType(literal);
                return new IRConstant(literal.Value, type);
            }

            // For other expressions, visit and capture the result
            var savedResult = _expressionResult;
            expr.Accept(this);
            var result = _expressionResult;
            _expressionResult = savedResult;
            return result;
        }

        /// <summary>
        /// Lowers <c>AndAlso</c>/<c>OrElse</c> to real CONTROL FLOW, so the right operand runs
        /// only when the left did not already decide the answer.
        ///
        /// <para>⛔ <b>Why it cannot be a binary operation.</b> The generic path evaluates BOTH
        /// operands into temps and then combines them, destroying short-circuiting before any
        /// backend sees it: <c>t1 = L(); t2 = R(); t3 = t1 &amp;&amp; t2;</c> — the <c>&amp;&amp;</c>
        /// is decorative, both already ran. MEASURED on C++ and JavaScript (chip task_c8db4a58).
        /// The C# backend LOOKED correct only because it renders operand trees inline, so C#'s
        /// own <c>&amp;&amp;</c> short-circuited an already-broken IR.</para>
        ///
        /// <para>⛔⛔ <b>THE BLOCK NAMES ARE LOAD-BEARING — this is not cosmetic.</b> The C#
        /// backend reconstructs structure by MATCHING NAMES, not graph shape:
        /// <c>IsIfThenElse</c> requires the true target to contain <c>.then</c> and the false
        /// target <c>.else</c>, then looks up <c>{prefix}.end</c>; <c>IsIfThen</c> requires
        /// <c>.then</c>/<c>.end</c> with matching prefixes. Anything else falls to a lossy
        /// fallback that emits both arms and NEVER emits the merge — and
        /// <c>HandleUnconditionalBranch</c> separately skips any branch whose target ends in
        /// <c>.end</c>, on the assumption the If construct emitted it. An earlier attempt named
        /// these blocks <c>.rhs</c>/<c>.skip</c> and the whole continuation vanished: the
        /// program printed its first operand and stopped.</para>
        ///
        /// <para>So both forms are emitted as the EXACT shape <see cref="Visit(IfStatementNode)"/>
        /// produces — <c>AndAlso</c> as a bare If (<c>then</c> = evaluate right), <c>OrElse</c>
        /// as an If/Else with an EMPTY then arm (<c>else</c> = evaluate right). Two constructs
        /// both backends already handle thousands of times over, rather than a novel CFG each
        /// would have to learn.</para>
        ///
        /// <para>⛔ <c>And</c>/<c>Or</c> must NOT come through here — they are the
        /// non-short-circuit operators and both operands must run. Lowering all four would be
        /// the mirror miscompile, silently dropping a side effect.</para>
        /// </summary>
        private void BuildShortCircuit(BinaryExpressionNode node, BinaryOpKind kind)
        {
            var resultType = _semanticAnalyzer.GetNodeType(node)
                             ?? new TypeInfo("Boolean", TypeKind.Primitive);

            // Same counter as a real If, so a prefix can never collide with one.
            var id = _ifCounter++;
            var thenBlock = _currentFunction.CreateBlock($"if{id}.then");
            var elseBlock = kind == BinaryOpKind.OrElse
                ? _currentFunction.CreateBlock($"if{id}.else")
                : null;
            var mergeBlock = _currentFunction.CreateBlock($"if{id}.end");

            // The carrier. ⛔ REGISTERED with the function exactly as a Dim is —
            // GetOrCreateVariable only tracks versions, while LocalVariables is what the
            // backends read to DECLARE a local. Without it the C++ backend emitted
            // `__sc0 = …` for a name it had never declared.
            var result = CreateVariable($"__sc{id}", resultType, _nextVersion++);
            PushVariableVersion(result.Name, result);
            _currentFunction.LocalVariables.Add(result);

            node.Left.Accept(this);
            EmitInstruction(new IRAssignment(result, _expressionResult));
            EmitInstruction(new IRConditionalBranch(result, thenBlock, elseBlock ?? mergeBlock));

            // AndAlso keeps going while the left is TRUE, so the right operand IS the then arm.
            // OrElse keeps going while it is FALSE, so the then arm is empty and the right
            // operand is the else arm — the left value already stands as the result.
            _currentBlock = thenBlock;
            if (kind == BinaryOpKind.AndAlso)
            {
                node.Right.Accept(this);
                EmitInstruction(new IRAssignment(result, _expressionResult));
            }
            if (!_currentBlock.IsTerminated())
                EmitInstruction(new IRBranch(mergeBlock));

            if (elseBlock != null)
            {
                _currentBlock = elseBlock;
                node.Right.Accept(this);
                EmitInstruction(new IRAssignment(result, _expressionResult));
                if (!_currentBlock.IsTerminated())
                    EmitInstruction(new IRBranch(mergeBlock));
            }

            _currentBlock = mergeBlock;
            _expressionResult = result;
        }

        public void Visit(BinaryExpressionNode node)
        {
            // ⛔ SHORT-CIRCUIT FIRST, before the right operand is touched. AndAlso/OrElse are
            // CONTROL FLOW, not operators with two ready values — see BuildShortCircuit.
            if (!IsComparisonOperator(node.Operator))
            {
                var scKind = MapBinaryOperator(node.Operator);
                if (scKind == BinaryOpKind.AndAlso || scKind == BinaryOpKind.OrElse)
                {
                    BuildShortCircuit(node, scKind);
                    return;
                }
            }

            node.Left.Accept(this);
            var left = _expressionResult;

            node.Right.Accept(this);
            var right = _expressionResult;

            var resultType = _semanticAnalyzer.GetNodeType(node);
            var tempName = _currentFunction.GetNextTempName();

            // Map operator
            IRValue result;

            if (IsComparisonOperator(node.Operator))
            {
                var cmpKind = MapComparisonOperator(node.Operator);
                result = new IRCompare(tempName, cmpKind, left, right, resultType);
            }
            else
            {
                var opKind = MapBinaryOperator(node.Operator);

                // ⛔ NEITHER BACKEND READS IRBinaryOp.Type. Both render `{left} {op} {right}`
                // and let the TARGET language pick the operator semantics, so a Double-typed
                // division of two int32_t operands still performs C-family INTEGER division
                // and merely widens the already-truncated result. Measured: the temp was
                // correctly `double t0` and the program still printed 3 for 7 / 2 — the
                // emission assertion passed while the executable was wrong.
                //
                // So widen the OPERANDS, not just the result. IRCast is the right seam: it is
                // already handled by both interpreters, every backend, IROperandWalker,
                // IRPrettyPrinter and CppCapabilityChecker, so one insertion here moves every
                // consumer at once instead of repeating the coercion per backend.
                //
                // IntDiv is deliberately excluded — `\` must keep truncating.
                if (opKind == BinaryOpKind.Div && resultType != null && resultType.IsFloatingPoint())
                {
                    left = WidenDivisionOperand(left, resultType);
                    right = WidenDivisionOperand(right, resultType);
                }

                result = new IRBinaryOp(tempName, opKind, left, right, resultType);
            }

            EmitInstruction(result);
            _expressionResult = result;
        }

        /// <summary>
        /// Widen an integral operand of a floating-point division to the result type, so the
        /// target language divides in floating point rather than truncating first. Non-integral
        /// operands are returned unchanged, so this is a no-op once both sides already match.
        /// </summary>
        private IRValue WidenDivisionOperand(IRValue operand, TypeInfo targetType)
        {
            var sourceType = operand?.Type;
            if (sourceType == null || !sourceType.IsIntegral())
                return operand;

            var castName = _currentFunction.GetNextTempName();
            var cast = new IRCast(castName, operand, sourceType, targetType,
                                  DetermineCastKind(sourceType, targetType));
            EmitInstruction(cast);
            return cast;
        }

        public void Visit(UnaryExpressionNode node)
        {
            node.Operand.Accept(this);
            var operand = _expressionResult;

            // Unary '+' is the identity on a numeric operand (the analyzer has already checked
            // it is numeric and typed the node as the operand's type) — there is no IR op for it,
            // so the operand IS the result. `Case +7`, `x = +y`.
            if (node.Operator == "+")
            {
                _expressionResult = operand;
                return;
            }

            var resultType = _semanticAnalyzer.GetNodeType(node);
            var opKind = MapUnaryOperator(node.Operator);

            // For constant folding (especially useful for global variable initializers like -1)
            if (operand is IRConstant constOp && opKind == UnaryOpKind.Neg)
            {
                // Fold the negation at compile time
                object foldedValue = constOp.Value switch
                {
                    int i => -i,
                    long l => -l,
                    float f => -f,
                    double d => -d,
                    decimal m => -m,
                    short s => (short)-s,
                    byte b => -b,
                    _ => null
                };

                if (foldedValue != null)
                {
                    _expressionResult = new IRConstant(foldedValue, resultType);
                    return;
                }
            }

            // If we're outside a function context (global initializer), create an IRUnaryOp without emitting
            if (_currentFunction == null)
            {
                var result = new IRUnaryOp("global_init", opKind, operand, resultType);
                _expressionResult = result;
                return;
            }

            var tempName = _currentFunction.GetNextTempName();
            var unaryResult = new IRUnaryOp(tempName, opKind, operand, resultType);

            EmitInstruction(unaryResult);
            _expressionResult = unaryResult;
        }

        public void Visit(LiteralExpressionNode node)
        {
            var type = _semanticAnalyzer.GetNodeType(node);

            // Spec 6.1: the analyzer retyped this literal to Decimal (Decimal
            // context), so the IR constant carries a System.Decimal built from
            // the SOURCE TEXT — '1.50' stays 1.50m (scale preserved), never the
            // double 1.5. The is-double optimizer fold patterns then skip it.
            if (type?.Name == "Decimal")
            {
                if (SemanticAnalyzer.TryConvertDecimalLiteral(node, out var decimalValue))
                {
                    _expressionResult = new IRConstant(decimalValue, type);
                    return;
                }

                // Should be unreachable: the analyzer only retypes a literal to
                // Decimal after TryConvertDecimalLiteral succeeded on it.
                // Last-resort fallback for a synthesized Decimal-typed literal:
                // convert the parsed value (loses text fidelity for floats).
                Debug.Assert(false, "Decimal-typed literal with no convertible text/value");
                _expressionResult = new IRConstant(
                    Convert.ToDecimal(node.Value, CultureInfo.InvariantCulture), type);
                return;
            }

            _expressionResult = new IRConstant(node.Value, type);
        }

        public void Visit(InterpolatedStringNode node)
        {
            var stringType = new TypeInfo("String", TypeKind.Primitive);

            // Build the string by concatenating all parts
            IRValue result = null;

            foreach (var part in node.Parts)
            {
                IRValue partValue;

                if (part is string text)
                {
                    // Literal text part
                    partValue = new IRConstant(text, stringType);
                }
                else if (part is ExpressionNode expr)
                {
                    // A hole lowers exactly as `&` would: the value goes straight into a Concat
                    // and each backend turns it into text there, the one place it already
                    // knows how (CppCodeGenerator.StringifyForText, C#'s and JS's `+`).
                    // ⛔ Not a call to "ToString": no backend defines a free function of that
                    // name, so every non-String hole used to fail to build, on all of them.
                    expr.Accept(this);
                    partValue = _expressionResult;

                    // A Concat needs a String on its LEFT: `&` guarantees one (the analyzer
                    // requires a string operand), and for `{a}{b}` with two Integers C# and
                    // JS would otherwise ADD them. So a leading hole starts from "".
                    if (result == null && _semanticAnalyzer.GetNodeType(expr)?.Name != "String")
                        result = new IRConstant("", stringType);
                }
                else
                {
                    continue;
                }

                // Concatenate with previous result
                if (result == null)
                {
                    result = partValue;
                }
                else
                {
                    var tempName = _currentFunction.GetNextTempName();
                    var concat = new IRBinaryOp(tempName, BinaryOpKind.Concat, result, partValue, stringType);
                    EmitInstruction(concat);
                    result = concat;
                }
            }

            _expressionResult = result ?? new IRConstant("", stringType);
        }

        public void Visit(IdentifierExpressionNode node)
        {
            // A ::-qualified foreign C++ global/constant read (mathlib::kAnswer, ::kMax).
            // Emit a bare reference by verbatim name — NOT a declared local (its '::' name is
            // not a real identifier). The C++ backend renders the IRVariable name verbatim
            // because its type is Foreign (SanitizeName would strip the '::').
            if (node.IsForeignQualified)
            {
                _expressionResult = new IRVariable(node.Name, _semanticAnalyzer.GetNodeType(node));
                return;
            }

            // vbCrLf, vbTab, ... (see SemanticAnalyzer.VbStringConstants).
            if (node.BuiltinConstantValue != null)
            {
                _expressionResult = new IRConstant(node.BuiltinConstantValue, _semanticAnalyzer.GetNodeType(node));
                return;
            }

            // A variable or constant of a Module in this unit, resolved by the analyzer (its own
            // module's by lexical scope, another module's by the cross-module fallback). Bound
            // to the real global, whatever the declaration order.
            if (ModuleMemberSymbolOf(node) is Symbol moduleMember)
            {
                _expressionResult = GlobalReference(moduleMember.Name, moduleMember.OwningModule,
                    _semanticAnalyzer.GetNodeType(node));
                return;
            }

            // Check if this identifier was resolved as an imported symbol
            var symbol = _semanticAnalyzer.GetNodeSymbol(node);
            if (symbol != null && symbol.IsImported && !string.IsNullOrEmpty(symbol.SourceModule))
            {
                // This is an imported variable from another module
                var variable = new IRVariable(node.Name, _semanticAnalyzer.GetNodeType(node));
                variable.IsGlobal = true;
                variable.ModuleName = symbol.SourceModule;
                _expressionResult = variable;
                return;
            }

            // Look up variable normally
            var localVar = GetOrCreateVariable(node.Name, _semanticAnalyzer.GetNodeType(node));
            _expressionResult = localVar;
        }

        public void Visit(MemberAccessExpressionNode node)
        {
            // §8.3's enum row (P2a-2 T8c-3). The analyzer folded this member access to its
            // underlying primitive because the winner's parameter at that index is enum-typed,
            // so the value is known at compile time.
            //
            // ⛔ RETURNING HERE IS DOING TWO JOBS, and the position of this arm is both of
            // them. It skips the receiver visit, and — because it returns before any
            // IRFieldAccess is constructed or emitted, and NetSurfaceCollector is IR-driven —
            // it is ALSO what stops a compile-time constant minting a shim export, a proxy slot
            // and a ~27 s Native AOT publish round trip. Measured before this change:
            // FileMode.Open emitted a real bl_net_System_IO_FileMode_Open__… export. Move this
            // arm below the emission and the value stays correct while the export comes back.
            if (_semanticAnalyzer?.NetEnumConstants != null
                && _semanticAnalyzer.NetEnumConstants.TryGetValue(node, out var enumConstant))
            {
                _expressionResult = new IRConstant(enumConstant.Value, enumConstant.Type);
                return;
            }

            // `Helpers.Value`: the analyzer resolved this to a Module's variable or constant, so
            // it IS that global — not a field read on a receiver. ⛔ Before the receiver visit,
            // which would materialize a phantom variable named after the module. Measured before:
            // `t0 = Helpers.Value;` on C++ ("'Helpers' was not declared"), ReferenceError on
            // JavaScript, MissingFieldException 'System.Object.Value' on MSIL.
            if (ModuleMemberSymbolOf(node) is Symbol moduleMember)
            {
                _expressionResult = GlobalReference(moduleMember.Name, moduleMember.OwningModule,
                    _semanticAnalyzer.GetNodeType(node));
                return;
            }

            node.Object.Accept(this);
            var obj = _expressionResult;

            // Generate field access
            var memberType = _semanticAnalyzer.GetNodeType(node);
            var tempName = _currentFunction.GetNextTempName();

            var fieldAccess = new IRFieldAccess(tempName, obj, node.MemberName, memberType);

            // P2a-2 Task 7a: a .NET PROPERTY/FIELD read carries its descriptor (the
            // getter-shaped slot) into IR. Gated to those two kinds on purpose: a METHOD
            // annotation on a bare member access (a method-group reference) is not a read
            // and must not make this node lower through a proxy. An INDEXER (a Property
            // WITH parameters — DescribeMember records its indices) is excluded too: its
            // get_Item/set_Item pair is §8.5 / Task 9 work, and stamping it here would hand
            // the lowering a descriptor whose declared parameters no call site fills.
            if (_semanticAnalyzer.NetMemberAnnotations.TryGetValue(node, out var netRead)
                && (netRead.Member.Kind == BasicLang.Net.NetMemberCategory.Property
                    || netRead.Member.Kind == BasicLang.Net.NetMemberCategory.Field)
                && netRead.Member.Parameters.Count == 0)
            {
                fieldAccess.ResolvedNetTarget = netRead.Member;
                fieldAccess.ResolvedNetTargetIsExact = netRead.Exact;
                var receiverTypeName = _semanticAnalyzer.GetNodeType(node.Object)?.Name;
                fieldAccess.NetCategory = !string.IsNullOrEmpty(receiverTypeName)
                    ? BoundaryTypeRegistry.Categorize(receiverTypeName)
                    : BoundaryTypeCategory.Unknown;
            }
            else if (_semanticAnalyzer.NetArrayLengthFor(
                         _semanticAnalyzer.GetNodeType(node.Object), node.MemberName) is { } arrayLength)
            {
                // P2a-2 Task-9 review item 1: `arr.Length` on a HANDLE-represented .NET array.
                // There is no annotation to find — a .NET array declares no members in metadata
                // at all (which is why §8.5 synthesizes the accessor), so the probe records
                // nothing and this used to fall through to CppCodeGenerator's name/Kind-keyed
                // `.Length` arm and emit `parts.size()` on a BasicLang::NetRef. It is the very
                // next thing a user writes after `parts(0)`.
                fieldAccess.ResolvedNetTarget = arrayLength;
                fieldAccess.ResolvedNetTargetIsExact = true;
                fieldAccess.NetCategory = BoundaryTypeCategory.Unknown;
            }

            EmitInstruction(fieldAccess);

            _expressionResult = fieldAccess;
        }

        public void Visit(CallExpressionNode node)
        {
            var returnType = _semanticAnalyzer.GetNodeType(node);
            var tempName = returnType != null && returnType.Name != "Void"
                ? _currentFunction.GetNextTempName()
                : null;

            // Nested/chained indexer read: `m(0)(1)` (or `d("a")("b")`). The OUTER callee is an
            // arbitrary expression (here the inner `m(0)` indexer), not an identifier/member — so
            // it never reached the identifier-branch indexer check, and fell through to the
            // delegate-invocation `else` branch, emitting a call on an UNDECLARED temp
            // (`t5(1)`). Detect it here: when the callee's own type is an indexable collection,
            // lower to IRIndexerAccess over the (already-evaluated) inner collection value.
            if (node.Arguments.Count > 0
                && !(node.Callee is IdentifierExpressionNode)
                && !(node.Callee is MemberAccessExpressionNode))
            {
                var chainType = _semanticAnalyzer.GetNodeType(node.Callee);
                if (chainType != null && IsIndexableGenericType(chainType))
                {
                    node.Callee.Accept(this);
                    var innerCollection = _expressionResult;
                    var elementType = returnType ?? new TypeInfo("Object", TypeKind.Class);
                    var chainTemp = _currentFunction.GetNextTempName();
                    var chainAccess = new IRIndexerAccess(chainTemp, innerCollection, elementType);
                    foreach (var index in node.Arguments)
                    {
                        index.Accept(this);
                        chainAccess.Indices.Add(_expressionResult);
                    }
                    EmitInstruction(chainAccess);
                    _expressionResult = chainAccess;
                    return;
                }
            }

            // Check for different call types
            if (node.Callee is MemberAccessExpressionNode memberExpr)
            {
                // Check if this is a MyBase call
                if (memberExpr.Object is MyBaseExpressionNode)
                {
                    // Base class method call: MyBase.Method(args)
                    var baseCall = new IRBaseMethodCall(tempName, memberExpr.MemberName, returnType);

                    foreach (var arg in node.Arguments)
                    {
                        arg.Accept(this);
                        baseCall.Arguments.Add(_expressionResult);
                    }

                    EmitInstruction(baseCall);
                    _expressionResult = baseCall;
                    return;
                }

                // `Helpers.Twice(4)`: the analyzer resolved the callee to a Module's PROCEDURE,
                // so this is a plain call to it — not a method on a receiver. ⛔ Before the
                // receiver visit, which materialized a phantom variable named after the module
                // and lowered an INSTANCE call on it: `t0 = Helpers.Twice(4);` on C++ ("'Helpers'
                // was not declared"), ReferenceError on JavaScript, `callvirt ... System.Object::
                // 'Twice'` (MissingMethodException) on MSIL — and C# ran it only by re-emitting
                // the text. Measured in the single-file AND the multi-file path alike.
                if (_semanticAnalyzer.GetNodeSymbol(memberExpr) is Symbol moduleProcedure
                    && (moduleProcedure.Kind == SymbolKind.Function || moduleProcedure.Kind == SymbolKind.Subroutine)
                    && !string.IsNullOrEmpty(moduleProcedure.OwningModule))
                {
                    EmitProcedureCall(node, moduleProcedure, memberExpr.MemberName, tempName, returnType);
                    return;
                }

                // Check if this is an instance method call vs static method call
                // Instance: obj.Method() where obj is a variable
                // Static: ClassName.Method() where ClassName is a type
                memberExpr.Object.Accept(this);
                var obj = _expressionResult;

                // `.Item(i)` READ on a collection is the explicit VB indexer — lower it to the
                // same IRIndexerAccess as `l(i)` (List -> (*l)[i]; Dictionary -> l->Get(k)).
                // Otherwise it degraded to a bogus `l->Item(i)` member call (no such member).
                var itemReceiverType = _semanticAnalyzer.GetNodeType(memberExpr.Object);
                if (string.Equals(memberExpr.MemberName, "Item", StringComparison.OrdinalIgnoreCase)
                    && node.Arguments.Count > 0
                    && itemReceiverType != null && IsIndexableGenericType(itemReceiverType))
                {
                    var elementType = returnType ?? new TypeInfo("Object", TypeKind.Class);
                    var itemTemp = _currentFunction.GetNextTempName();
                    var itemAccess = new IRIndexerAccess(itemTemp, obj, elementType);
                    foreach (var index in node.Arguments)
                    {
                        index.Accept(this);
                        itemAccess.Indices.Add(_expressionResult);
                    }
                    EmitInstruction(itemAccess);
                    _expressionResult = itemAccess;
                    return;
                }

                // `obj.Field(i)` where Field is an ARRAY is an index, not a method call. The
                // array-index branch below covers only an IdentifierExpressionNode callee, so a
                // FIELD receiver fell through to the instance-call arm and emitted `b->Cells(0)`
                // — calling a std::vector. Same test as that branch, same lowering (GEP + load);
                // the receiver is the field access rather than a bare variable.
                var memberArrayType = _semanticAnalyzer.GetNodeType(memberExpr);
                if (node.Arguments.Count > 0
                    && memberArrayType != null
                    && memberArrayType.Kind == TypeKind.Array
                    // §8.5: a handle-represented .NET array owns no native storage to index —
                    // the marker is tested before the array branch everywhere else too.
                    && memberArrayType.NetHandleTypeFullName == null)
                {
                    memberExpr.Accept(this);
                    var arrayField = _expressionResult;

                    var elementType = memberArrayType.ElementType
                                      ?? new TypeInfo("Object", TypeKind.Class);
                    var fieldGepTemp = _currentFunction.GetNextTempName();
                    var fieldGep = new IRGetElementPtr(fieldGepTemp, arrayField, elementType);
                    foreach (var index in node.Arguments)
                    {
                        index.Accept(this);
                        fieldGep.Indices.Add(_expressionResult);
                    }
                    EmitInstruction(fieldGep);

                    var fieldLoadTemp = _currentFunction.GetNextTempName();
                    var fieldLoad = new IRLoad(fieldLoadTemp, fieldGep, elementType);
                    EmitInstruction(fieldLoad);

                    _expressionResult = fieldLoad;
                    return;
                }

                // Determine if this is a static call (type reference) or instance call (variable)
                // Check if the object is a reference to a class type (static call) by:
                // 1. It's an IRVariable with the EXACT name of a class (case-sensitive)
                // 2. The identifier doesn't match a local variable in the current function
                // 3. It's a known .NET static type (Console, Math, File, etc.)
                bool isStaticCall = false;
                if (obj is IRVariable objVar)
                {
                    // Case-sensitive check: does the name exactly match a class name?
                    bool exactClassMatch = _module.Classes.Keys.Any(k => k == objVar.Name);
                    // Is this a local variable or parameter?
                    bool isLocalOrParam = _currentFunction?.Parameters.Any(p => p.Name == objVar.Name) == true ||
                                          _locals.ContainsKey(objVar.Name);

                    // Check if it's a .NET type (contains dot or is known .NET type name)
                    bool isNetType = objVar.Name.Contains('.') || IsKnownNetStaticType(objVar.Name);

                    // A declared name is never a type name, so the guard applies to BOTH
                    // sources of type-ness, not just the class-name one. It used to read
                    // `(exactClassMatch && !isLocalOrParam) || isNetType`, which let isNetType
                    // bypass the guard entirely — and IsKnownNetStaticType answers TRUE for ANY
                    // PascalCase identifier once the unit has a .NET `Using` (its
                    // imported-namespace heuristic), so on the real CLI/IDE path — where
                    // ConfigureModuleSystem populates CurrentUnit — `Dim Rx As New Regex("a+")`
                    // followed by `Rx.IsMatch("aaa")` routed the INSTANCE call into the fused
                    // static arm, discarding the receiver expression. Pre-P2a-2 that produced a
                    // bogus `Rx.IsMatch(...)` free-function call; with .NET lowering live it
                    // reached BuildNetProxyCall with an instance descriptor and no receiver.
                    //
                    // ⚠ HOW MUCH THIS LINE ACTUALLY COVERS, measured — do not overestimate it:
                    // `_locals` IS VESTIGIAL. Nothing ever adds an entry for a declared
                    // variable; its only writes are the two lambda-context restore loops, each
                    // fed by a `savedLocals` copy of the (empty) dictionary. So
                    // `_locals.ContainsKey(...)` is permanently false and isLocalOrParam
                    // reduces to "is a PARAMETER of the current function". A DIM'D LOCAL — the
                    // C1 shape above — is NOT caught here; it is caught by the descriptor
                    // cross-check immediately below, which is the AUTHORITATIVE layer for
                    // locals. Do not "simplify away" that check on the strength of this line.
                    // (Fixing `_locals`, or deleting it, is separate work: it would also revive
                    // the dead lambda capture-detection loop it feeds.)
                    isStaticCall = (exactClassMatch || isNetType) && !isLocalOrParam;
                }

                // P2a-2 Task 7a: the ANALYZER'S DESCRIPTOR IS AUTHORITATIVE about static-ness —
                // it comes from real overload resolution, while the routing above is a
                // name-shape heuristic. An instance member can only be reached through a
                // receiver, so a non-static annotation forces the instance arm regardless of
                // what the identifier looked like. (The converse needs no handling: VB's
                // shared-through-instance leniency lands a STATIC descriptor on the instance
                // arm, and the lowering drops the phantom receiver by reading IsStatic.)
                //
                // ⚠ THIS IS THE LOAD-BEARING LAYER FOR THE C1 SHAPE, not a belt-and-braces
                // second opinion: `_locals` is vestigial (see above), so the name-shape guard
                // catches PARAMETERS only and a `Dim`'d local named like a type reaches here
                // still marked static. Removing this check re-breaks
                // NetBuildPipelineTests.InstanceCallOnAPascalCaseLocal_UnderANetUsing_… —
                // verified by mutation, which reproduced the original C1 failure exactly.
                if (isStaticCall
                    && _semanticAnalyzer.NetMemberAnnotations.TryGetValue(memberExpr, out var netShape)
                    && !netShape.Member.IsStatic
                    && netShape.Member.Kind != BasicLang.Net.NetMemberCategory.Constructor)
                {
                    isStaticCall = false;
                }

                if (isStaticCall && obj is IRVariable staticVar)
                {
                    // Static method call: ClassName.Method()
                    var call = new IRCall(tempName, $"{staticVar.Name}.{memberExpr.MemberName}", returnType);

                    // P2a-1 CARRIAGE (read by nobody until P2a-2). The fused name above is the
                    // ONLY place the receiver type survives as a distinct token — everything
                    // downstream sees the single string "Receiver.Member". Record the receiver's
                    // spec-C1 category here, while we still have it.
                    //
                    // INERT BY CONSTRUCTION: Categorize is a pure static table lookup over a name
                    // — no Roslyn, no assembly references, no IO — and no backend reads
                    // NetCategory.
                    call.NetCategory = BoundaryTypeRegistry.Categorize(staticVar.Name);

                    // P2a-2 Task 2: attach the analyzer's resolution. The analyzer probed this
                    // SAME MemberAccessExpressionNode (both walks share one AST, so reference
                    // identity is the key) and recorded the winning descriptor in its annotation
                    // side table. Absent for every claimed name and for every compilation
                    // without a NetResolverFactory — then ResolvedNetTarget stays null and
                    // nothing downstream changes. Task 7a: the exactness bit rides along —
                    // the lowering trusts ONLY probe-verified signatures (the name-only gate).
                    if (_semanticAnalyzer.NetMemberAnnotations.TryGetValue(memberExpr, out var staticNetTarget))
                    {
                        call.ResolvedNetTarget = staticNetTarget.Member;
                        call.ResolvedNetTargetIsExact = staticNetTarget.Exact;
                    }

                    call.GenericArguments.AddRange(BuildGenericArgTypes(node.GenericArguments));

                    // ⛔ The STATIC member arm needs the coercion too, and it is a third site, not
                    // a duplicate: `Box.Shr(7 / 2)` on a user `Shared` method reaches here, not
                    // the instance arm below nor the plain-identifier arm further down. Measured
                    // without it, MSIL pushed a float64 and called `Box::Shr(int32)` — the
                    // signature correct (it is spelled from the declaration) and the VALUE wrong,
                    // which the CLR rejects as an invalid program. The symbol is the one the
                    // analyzer resolved for this member access, the same one the instance arm
                    // reads ByRef from.
                    var staticCalleeSymbol = _semanticAnalyzer.GetNodeSymbol(memberExpr);
                    foreach (var arg in node.Arguments)
                    {
                        arg.Accept(this);
                        call.Arguments.Add(CoerceToParameterType(
                            _expressionResult, staticCalleeSymbol, call.Arguments.Count));

                        // P2a-2 Task 8: ByRefArguments was populated only for resolved USER
                        // functions (funcSymbol.Parameters[i].IsByRef, below). A resolved .NET
                        // target carries the same fact in its DESCRIPTOR, and §8.3's ref/out
                        // row makes it observable — an IRCall that says every argument is
                        // by-value while the callee writes one back is IR that lies about its
                        // own effects, which is exactly what an optimizer is entitled to trust.
                        // Read from the descriptor rather than re-derived: it is the same
                        // signature the lowering marshals against.
                        // P2a-2 Task 9 (7a/8 review I5): the KIND travels alongside, because
                        // `List<bool>` cannot tell ref from out and CSharpBackend emitted
                        // `ref x` for a .NET `out` parameter (CS1620).
                        var parameters = call.ResolvedNetTarget?.Parameters;
                        var refKind = parameters != null && call.Arguments.Count - 1 < parameters.Count
                            ? parameters[call.Arguments.Count - 1].RefKind
                            : BasicLang.Net.NetRefKind.None;
                        call.ByRefArguments.Add(refKind != BasicLang.Net.NetRefKind.None);
                        call.NetArgumentRefKinds.Add(refKind);
                    }

                    // ⚠ USER callees only, deliberately. This arm also serves .NET targets the
                    // analyzer resolved, whose arguments are marshalled against the descriptor
                    // recorded in NetArgumentRefKinds — appending behind that list's back would
                    // leave the two out of step. Filling a .NET optional is a separate job from
                    // this one, so the guard makes this do NOTHING there rather than guess. Like
                    // the constructor-ambiguity branch below it is fail-safe by construction: the
                    // arguments keep exactly what they had before this existed.
                    if (call.ResolvedNetTarget == null)
                    {
                        AppendOmittedOptionalArguments(
                            call.Arguments, call.ByRefArguments, staticCalleeSymbol);
                    }

                    EmitInstruction(call);
                    _expressionResult = call;
                }
                else
                {
                    // Instance method call: obj.Method()
                    var methodCall = new IRInstanceMethodCall(tempName, obj, memberExpr.MemberName, returnType);
                    methodCall.GenericArguments.AddRange(BuildGenericArgTypes(node.GenericArguments));

                    // P2a-2 Task 2 (mirror of the fused static arm above): the annotation is
                    // keyed on the callee MemberAccessExpressionNode; the receiver's spec-C1
                    // category rides along so the surface collector can tell a natively-handled
                    // receiver from a shim-routed one. itemReceiverType IS the receiver's static
                    // type (computed above for the .Item check). All fields keep their inert
                    // defaults (null / Unknown / false) for every non-.NET call.
                    if (_semanticAnalyzer.NetMemberAnnotations.TryGetValue(memberExpr, out var instanceNetTarget))
                    {
                        methodCall.ResolvedNetTarget = instanceNetTarget.Member;
                        methodCall.ResolvedNetTargetIsExact = instanceNetTarget.Exact;
                        methodCall.NetCategory = !string.IsNullOrEmpty(itemReceiverType?.Name)
                            ? BoundaryTypeRegistry.Categorize(itemReceiverType.Name)
                            : BoundaryTypeCategory.Unknown;
                    }

                    // Which parameters the method takes ByRef. The analyzer recorded the member
                    // symbol on this SAME MemberAccessExpressionNode, and its Parameters carry
                    // IsByRef straight from the ParameterNode. Without this an instance call is
                    // IR that lies about its own effects exactly the way the static arm used to:
                    // the C# backend dropped the `ref` at the call site (CS1620) and the
                    // optimizer kept copy facts across a call that writes them.
                    var methodSymbol = _semanticAnalyzer.GetNodeSymbol(memberExpr);
                    foreach (var arg in node.Arguments)
                    {
                        arg.Accept(this);
                        methodCall.Arguments.Add(CoerceToParameterType(
                            _expressionResult, methodSymbol, methodCall.Arguments.Count));

                        var methodParams = methodSymbol?.Parameters;
                        methodCall.ByRefArguments.Add(
                            methodParams != null && methodCall.Arguments.Count - 1 < methodParams.Count
                            && methodParams[methodCall.Arguments.Count - 1].IsByRef);
                    }

                    AppendOmittedOptionalArguments(
                        methodCall.Arguments, methodCall.ByRefArguments, methodSymbol);

                    EmitInstruction(methodCall);
                    _expressionResult = methodCall;
                }
            }
            else if (node.Callee is IdentifierExpressionNode idExpr)
            {
                // Get symbol to check if this is an array access (VB-style arr(i) syntax)
                var symbol = _semanticAnalyzer.GetNodeSymbol(node.Callee);
                var calleeType = _semanticAnalyzer.GetNodeType(node.Callee);

                // `f(args)` is a CALL, not an index, whenever f is itself callable (a Function
                // or Subroutine). Its calleeType is the RETURN type, so a collection/array-
                // returning function (e.g. `Function MakeList() As List(Of Integer)`) must NOT
                // take the array/indexer branches below — that lowered `MakeList(3)` to the
                // invalid `MakeList[3]`. Only value receivers (variables/parameters) index.
                bool calleeIsCallable = symbol != null &&
                    (symbol.Kind == SymbolKind.Function || symbol.Kind == SymbolKind.Subroutine);

                // §8.5 FIRST, ahead of BOTH the raw-array branch and the generic-collection
                // branch below: a handle-represented .NET array/collection has no native storage
                // to index, so neither IRGetElementPtr nor a native IRIndexerAccess is sound.
                if (!calleeIsCallable && node.Arguments.Count > 0
                    && TryEmitNetIndexerAccess(calleeType, node.Callee, node.Arguments))
                {
                    return;
                }

                // Check if this is actually an array access, not a function call
                // In VB, arr(i) can be either function call or array indexing
                if (!calleeIsCallable && calleeType != null && calleeType.Kind == TypeKind.Array && node.Arguments.Count > 0)
                {
                    // This is array access, generate array element access
                    node.Callee.Accept(this);
                    var array = _expressionResult;
                    
                    var elementType = calleeType.ElementType ?? new TypeInfo("Object", TypeKind.Class);
                    var gepTemp = _currentFunction.GetNextTempName();
                    var gep = new IRGetElementPtr(gepTemp, array, elementType);
                    
                    foreach (var index in node.Arguments)
                    {
                        index.Accept(this);
                        gep.Indices.Add(_expressionResult);
                    }
                    
                    EmitInstruction(gep);
                    
                    // Load from array element
                    var loadTemp = _currentFunction.GetNextTempName();
                    var load = new IRLoad(loadTemp, gep, elementType);
                    EmitInstruction(load);

                    _expressionResult = load;
                    return;
                }

                // Check if this is a generic collection indexer access (List<T>, Dictionary<K,V>, etc.)
                if (!calleeIsCallable && calleeType != null && node.Arguments.Count > 0 && IsIndexableGenericType(calleeType))
                {
                    // This is collection indexer access, generate IRIndexerAccess
                    node.Callee.Accept(this);
                    var collection = _expressionResult;

                    var elementType = returnType ?? new TypeInfo("Object", TypeKind.Class);
                    var indexerTemp = _currentFunction.GetNextTempName();
                    var indexerAccess = new IRIndexerAccess(indexerTemp, collection, elementType);

                    foreach (var index in node.Arguments)
                    {
                        index.Accept(this);
                        indexerAccess.Indices.Add(_expressionResult);
                    }

                    EmitInstruction(indexerAccess);
                    _expressionResult = indexerAccess;
                    return;
                }

                // A user procedure: same unit, another Module, or another file — one path.
                EmitProcedureCall(node, symbol, idExpr.Name, tempName, returnType);
                return;
            }
            else
            {
                // The callee is an arbitrary expression that yields a delegate,
                // e.g. invoking the delegate returned by another call: f(a)(b).
                // Invoke the callee VALUE rather than treating its temp name as
                // a function name (which dropped the invocation).
                node.Callee.Accept(this);
                var callee = _expressionResult;
                var call = new IRCall(tempName, callee?.Name ?? "unknown", returnType)
                {
                    CalleeValue = callee
                };

                foreach (var arg in node.Arguments)
                {
                    arg.Accept(this);
                    call.Arguments.Add(_expressionResult);
                }

                EmitInstruction(call);
                _expressionResult = call;
            }
        }

        /// <summary>
        /// Emits a call to a user procedure — the arguments coerced to the declared parameter
        /// types, ByRef marked from the declaration, omitted Optionals filled — under the IR name
        /// and owner <see cref="ProcedureCallTarget"/> decides. Shared by the bare-identifier
        /// callee and the <c>Module.Procedure</c> callee, so a qualified call cannot lose what a
        /// bare one has: before this, <c>Helpers.Inc(x)</c> went through the INSTANCE arm and
        /// dropped its ByRef marker on every backend (CS1620 on C#).
        /// </summary>
        private void EmitProcedureCall(CallExpressionNode node, Symbol funcSymbol, string writtenName,
            string tempName, TypeInfo returnType)
        {
            var (functionName, calleeModule) = ProcedureCallTarget(funcSymbol, funcSymbol?.Name ?? writtenName);
            var call = new IRCall(tempName, functionName, returnType) { CalleeModule = calleeModule };
            call.GenericArguments.AddRange(BuildGenericArgTypes(node.GenericArguments));

            for (int i = 0; i < node.Arguments.Count; i++)
            {
                node.Arguments[i].Accept(this);
                call.Arguments.Add(CoerceToParameterType(_expressionResult, funcSymbol, i));

                bool isByRef = false;
                if (funcSymbol?.Parameters != null && i < funcSymbol.Parameters.Count)
                {
                    isByRef = funcSymbol.Parameters[i].IsByRef;
                }
                call.ByRefArguments.Add(isByRef);
            }

            AppendOmittedOptionalArguments(call.Arguments, call.ByRefArguments, funcSymbol);

            EmitInstruction(call);
            _expressionResult = call;
        }

        public void Visit(ArrayAccessExpressionNode node)
        {
            node.Array.Accept(this);
            var array = _expressionResult;

            var elementType = _semanticAnalyzer.GetNodeType(node);
            var gepTemp = _currentFunction.GetNextTempName();
            var gep = new IRGetElementPtr(gepTemp, array, elementType);

            foreach (var index in node.Indices)
            {
                index.Accept(this);
                gep.Indices.Add(_expressionResult);
            }

            EmitInstruction(gep);

            // Load from array element
            var loadTemp = _currentFunction.GetNextTempName();
            var load = new IRLoad(loadTemp, gep, elementType);
            EmitInstruction(load);

            _expressionResult = load;
        }

        public void Visit(NewExpressionNode node)
        {
            var type = _semanticAnalyzer.GetNodeType(node);
            var tempName = _currentFunction.GetNextTempName();
            var className = node.Type?.Name ?? "Object";

            // Create new object instruction
            var newObj = new IRNewObject(tempName, className, type);

            // P2a-2 Task 7a: a probed .NET construction carries its resolved CONSTRUCTOR
            // (recorded EXACT by SemanticAnalyzer.ProbeNetConstruction) so the C++ lowering
            // can emit the ctor proxy returning the created object's handle into a NetRef.
            // Inert defaults (null / Unknown / false) for every non-.NET construction.
            if (_semanticAnalyzer.NetMemberAnnotations.TryGetValue(node, out var netCtor))
            {
                newObj.ResolvedNetTarget = netCtor.Member;
                newObj.ResolvedNetTargetIsExact = netCtor.Exact;
                newObj.NetCategory = !string.IsNullOrEmpty(className)
                    ? BoundaryTypeRegistry.Categorize(className)
                    : BoundaryTypeCategory.Unknown;
            }

            // ⛔ A constructor argument needs the same coercion a method argument does —
            // `New Box(7 / 2)` was CS1503 on C#, `MissingMethodException: Box..ctor(Double)` on
            // MSIL and 3.5 on JavaScript — and the parameter types come from the constructor the
            // ANALYZER bound this site to, the same Symbol every call arm reads.
            //
            // ⚠ It used to re-derive them from the IR class (`UnambiguousConstructorParameters`).
            // Reading the analyzer's binding instead means the IR can no longer coerce against a
            // DIFFERENT constructor than the one the analyzer type-checked, and it carries
            // IsOptional/DefaultValueExpression, which an IRVariable list does not.
            //
            // ⚠ Declaration ORDER no longer matters. Pass 1 (`RegisterClassTypes` +
            // `RegisterConstructorSignature`) gives every class its real TypeInfo and its `.ctorN`
            // members before any body is analyzed, so a `New` written above the class binds here
            // too. Before that, the same program emitted `new Box((double)(7) / (double)(2))` —
            // CS1503 — in one declaration order and the cast in the other.
            var ctorSymbol = _semanticAnalyzer.ConstructorBindings.TryGetValue(node, out var bound)
                ? bound
                : null;

            // Evaluate arguments
            foreach (var arg in node.Arguments)
            {
                arg.Accept(this);
                newObj.Arguments.Add(CoerceToParameterType(
                    _expressionResult, ctorSymbol, newObj.Arguments.Count));
            }

            AppendOmittedOptionalArguments(newObj.Arguments, null, ctorSymbol);

            EmitInstruction(newObj);
            _expressionResult = newObj;
        }

        public void Visit(CastExpressionNode node)
        {
            node.Expression.Accept(this);
            var value = _expressionResult;

            var sourceType = _semanticAnalyzer.GetNodeType(node.Expression);
            var targetType = _semanticAnalyzer.GetNodeType(node);

            var tempName = _currentFunction.GetNextTempName();
            var castKind = DetermineCastKind(sourceType, targetType);

            var cast = new IRCast(tempName, value, sourceType, targetType, castKind);
            cast.IsTryCast = node.IsTryCast;
            EmitInstruction(cast);

            _expressionResult = cast;
        }

        /// <summary>
        /// The IR intrinsic a ReDim's value lowers to: <c>ArrayResizeIntrinsic(array, count,
        /// preserve)</c>, returning the resized array, which the enclosing assignment stores back.
        /// Each backend renders it natively (C# <c>new T[n]</c> / <c>Array.Resize</c>, C++
        /// <c>BasicLang::ReDimArray</c>, JavaScript <c>new Array(n).fill</c> / <c>Array.from</c>).
        /// A call, not a new IR node: optimizer passes already treat a call as opaque and
        /// side-effecting, so nothing folds, hoists or merges it.
        /// </summary>
        public const string ArrayResizeIntrinsic = "__BLReDim";

        public void Visit(ArrayResizeExpressionNode node)
        {
            node.Array.Accept(this);
            var array = _expressionResult;
            node.Size.Accept(this);
            var size = _expressionResult;

            var arrayType = _semanticAnalyzer.GetNodeType(node);
            var call = new IRCall(_currentFunction.GetNextTempName(), ArrayResizeIntrinsic, arrayType);
            call.Arguments.Add(array);
            call.Arguments.Add(size);
            call.Arguments.Add(new IRConstant(node.Preserve, new TypeInfo("Boolean", TypeKind.Primitive)));
            EmitInstruction(call);

            _expressionResult = call;
        }

        // ====================================================================
        // Helper Methods
        // ====================================================================

        private IRConstant CreateDefaultValue(TypeInfo type)
        {
            if (type.Name == "Integer" || type.Name == "Long")
                return new IRConstant(0, type);
            if (type.Name == "Single" || type.Name == "Double")
                return new IRConstant(0.0, type);
            if (type.Name == "Boolean")
                return new IRConstant(false, type);
            if (type.Name == "String")
                return new IRConstant("", type);

            return new IRConstant(null, type);
        }

        private bool IsComparisonOperator(string op)
        {
            return op == "<" || op == "<=" || op == ">" || op == ">=" ||
                   op == "=" || op == "<>" || op == "==" || op == "!=" || op == "IsEqual";
        }

        private CompareKind MapComparisonOperator(string op)
        {
            return op switch
            {
                "=" or "==" or "IsEqual" => CompareKind.Eq,
                "<>" or "!=" => CompareKind.Ne,
                "<" => CompareKind.Lt,
                "<=" => CompareKind.Le,
                ">" => CompareKind.Gt,
                ">=" => CompareKind.Ge,
                _ => throw new Exception($"Unknown comparison operator: {op}")
            };
        }

        /// <summary>
        /// Maps an AST operator to its IR kind.
        ///
        /// <para><b>The word operators are matched case-INSENSITIVELY, and that is a fix, not
        /// a nicety.</b> BasicLang keywords are case-insensitive — the lexer's table is
        /// OrdinalIgnoreCase — but the AST stores the RAW SOURCE LEXEME, so a lowercase
        /// <c>and</c> arrived here as "and" and fell through to the throw. `If a and b Then`
        /// failed on EVERY backend with "Unknown binary operator 'and'" while `And` worked.
        /// The same applied to <c>or</c>, <c>mod</c>, <c>xor</c>, <c>shl</c> and <c>shr</c>.</para>
        ///
        /// <para><c>AndAlso</c>/<c>OrElse</c> map onto the same kinds as <c>And</c>/<c>Or</c>.
        /// BasicLang's And/Or are Boolean-only (SemanticAnalyzer rejects integral operands),
        /// and the optimizer already rewrites <c>x And False</c> to <c>False</c> — discarding
        /// the left operand, which is sound only under short-circuit evaluation. So the
        /// language's And is ALREADY the short-circuit one in practice, and the VB spellings
        /// are accepted as explicit synonyms rather than as a second, divergent semantics.</para>
        /// </summary>
        private BinaryOpKind MapBinaryOperator(string op)
        {
            // Symbol operators first: they are case-irrelevant and must not pay for ToLower.
            switch (op)
            {
                case "+": return BinaryOpKind.Add;
                case "-": return BinaryOpKind.Sub;
                case "*": return BinaryOpKind.Mul;
                case "/": return BinaryOpKind.Div;
                case "\\": return BinaryOpKind.IntDiv;
                case "%": return BinaryOpKind.Mod;
                case "&": return BinaryOpKind.Concat;
                // C-style spellings mean what they mean in C: these SHORT-CIRCUIT.
                case "&&": return BinaryOpKind.AndAlso;
                case "||": return BinaryOpKind.OrElse;
                case "^": return BinaryOpKind.Xor;
                case "<<": return BinaryOpKind.Shl;
                case ">>": return BinaryOpKind.Shr;
            }

            return (op ?? string.Empty).ToLowerInvariant() switch
            {
                "mod" => BinaryOpKind.Mod,
                // ⛔ These four are DISTINCT and must stay distinct. Collapsing andalso->And
                // and orelse->Or killed the short-circuit/non-short-circuit distinction at the
                // IR boundary, after the lexer, parser and analyzer had all carried it
                // faithfully — so every backend downstream got one of the two spellings wrong.
                "and" => BinaryOpKind.And,
                "andalso" => BinaryOpKind.AndAlso,
                "or" => BinaryOpKind.Or,
                "orelse" => BinaryOpKind.OrElse,
                "xor" => BinaryOpKind.Xor,
                "shl" => BinaryOpKind.Shl,
                "shr" => BinaryOpKind.Shr,
                _ => throw new Exception($"Unknown binary operator: {op}")
            };
        }

        /// <summary>Word operators are case-insensitive here for the same reason as
        /// <see cref="MapBinaryOperator"/>: lowercase <c>not</c> is legal BasicLang.</summary>
        private UnaryOpKind MapUnaryOperator(string op)
        {
            switch (op)
            {
                case "-": return UnaryOpKind.Neg;
                case "!": return UnaryOpKind.Not;
                case "~": return UnaryOpKind.BitwiseNot;
                case "++": return UnaryOpKind.Inc;
                case "--": return UnaryOpKind.Dec;
            }

            return (op ?? string.Empty).ToLowerInvariant() switch
            {
                "not" => UnaryOpKind.Not,
                "addressof" => UnaryOpKind.AddressOf,
                _ => throw new Exception($"Unknown unary operator: {op}")
            };
        }

        private CastKind DetermineCastKind(TypeInfo source, TypeInfo target)
        {
            if (source == null || target == null)
                return CastKind.Bitcast;
            if (source.IsFloatingPoint() && target.IsIntegral())
                return CastKind.FPToSI;
            if (source.IsIntegral() && target.IsFloatingPoint())
                return CastKind.SIToFP;
            if (source.IsIntegral() && target.IsIntegral())
                return source.Name == "Long" ? CastKind.Trunc : CastKind.SExt;
            if (source.IsFloatingPoint() && target.IsFloatingPoint())
                return source.Name == "Double" ? CastKind.FPTrunc : CastKind.FPExt;

            return CastKind.Bitcast;
        }

        /// <summary>
        /// Known .NET static types/classes that should be treated as static method targets
        /// </summary>
        private static readonly HashSet<string> KnownNetStaticTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // System types
            "Console", "Math", "Environment", "Convert", "BitConverter",
            "String", "Char", "Int32", "Int64", "Double", "Single", "Boolean", "Byte",
            "Int16", "UInt16", "UInt32", "UInt64", "Decimal", "SByte",
            "Object", "DateTime", "DateTimeOffset", "TimeSpan", "Guid", "Random",
            "Activator", "Type", "Enum", "Array", "Buffer",
            "GC", "AppDomain", "Assembly",
            // System enums (for static member access like ConsoleColor.Green)
            "ConsoleColor", "ConsoleKey", "DayOfWeek", "DateTimeKind", "StringComparison",
            "StringSplitOptions", "TypeCode", "MidpointRounding",
            // System.IO
            "File", "Directory", "Path", "FileMode", "FileAccess", "FileShare", "SearchOption",
            // System.Text
            "Encoding", "StringBuilder",
            // System.Threading
            "Task", "Thread", "Monitor", "Interlocked",
            // System.Diagnostics
            "Process", "Stopwatch", "Debug", "Trace"
        };

        /// <summary>
        /// Spec §6.5 row (c)'s FIRST half, exposed so
        /// <see cref="BasicLang.Net.NetClaimPredicate"/> reads the one table rather than copying it.
        ///
        /// <para><b>This is a call-SHAPE classifier, not an inventory of native implementations.</b>
        /// Membership alone claims nothing: 51 of these 58 names have no
        /// <c>EmitStdLibCall</c> arm for any member (<c>File</c>, <c>Activator</c>, <c>Encoding</c>,
        /// <c>Convert</c>, …), and claiming them by membership would strand exactly the .NET surface
        /// P2a exists to deliver. Row (c) is membership AND
        /// <see cref="Compiler.CodeGen.CPlusPlus.CppCodeGenerator.HasStdLibEmission"/>.</para>
        ///
        /// <para>Exposed read-only over the live set — a copy would be a second table to keep in
        /// sync, which is the failure mode this whole seam exists to prevent.</para>
        /// </summary>
        internal static IReadOnlyCollection<string> KnownNetStaticTypeNames => KnownNetStaticTypes;

        /// <summary>
        /// Case-insensitive membership in <see cref="KnownNetStaticTypeNames"/>. NOTE this is the
        /// static table only — the instance method <see cref="IsKnownNetStaticType"/> additionally
        /// applies a PascalCase heuristic keyed on the current unit's imports, which is a
        /// call-routing decision and deliberately not part of the claim predicate.
        /// </summary>
        internal static bool IsKnownNetStaticTypeName(string name) =>
            !string.IsNullOrEmpty(name) && KnownNetStaticTypes.Contains(name);

        private bool IsKnownNetStaticType(string name)
        {
            // Check the hardcoded list first
            if (KnownNetStaticTypes.Contains(name))
                return true;

            // If any .NET namespace is imported and the name looks like a type (PascalCase),
            // treat it as a potential .NET static type
            // This allows System.Windows.Forms, System.Drawing, etc.
            if (!string.IsNullOrEmpty(name) && char.IsUpper(name[0]))
            {
                // Check if the semantic analyzer knows about imported .NET namespaces
                var currentUnit = _semanticAnalyzer?.CurrentUnit;
                if (currentUnit != null && currentUnit.Usings.Any(u => u.IsNetNamespace))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsIndexableGenericType(TypeInfo type)
        {
            if (type == null) return false;

            var name = type.Name;
            // Check for common .NET generic collection types that support indexing
            if (name.StartsWith("List`") || name.StartsWith("List<") ||
                name.StartsWith("Dictionary`") || name.StartsWith("Dictionary<") ||
                name == "List" || name == "Dictionary" ||
                name.Contains("List(Of") || name.Contains("Dictionary(Of"))
            {
                return true;
            }

            // Also check for IList<T>, ICollection<T>, etc.
            if (name.StartsWith("IList`") || name.StartsWith("IList<") ||
                name.StartsWith("IReadOnlyList`") || name.StartsWith("IReadOnlyList<"))
            {
                return true;
            }

            return false;
        }

        private class LoopContext
        {
            public BasicBlock ContinueTarget { get; }
            public BasicBlock BreakTarget { get; }

            public LoopContext(BasicBlock continueTarget, BasicBlock breakTarget)
            {
                ContinueTarget = continueTarget;
                BreakTarget = breakTarget;
            }
        }
    }
}