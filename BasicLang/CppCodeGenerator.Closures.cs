using System;
using System.Collections.Generic;
using System.Linq;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.SemanticAnalysis;

namespace BasicLang.Compiler.CodeGen.CPlusPlus
{
    /// <summary>Which representation a root's lambdas took on C++ (#140).</summary>
    public enum CppClosurePath
    {
        /// <summary>Lowered by <see cref="ClosureLowering"/>: environments, by-reference capture.</summary>
        Lowered,
        /// <summary>Left un-lowered by the lowering and admitted by
        /// <c>CppCapabilityChecker.CheckLambdaCaptureWrites</c>: today's <c>[=]</c> lambdas, byte-identical
        /// to the emission before #140.</summary>
        ByCopy,
    }

    /// <summary>One root (an outermost non-lambda function, or a lambda outside every function body)
    /// that creates a lambda, and the path its lambdas took. <see cref="Root"/> is the display name:
    /// <c>Main</c>, <c>Box.Run</c>, <c>D.New</c>.</summary>
    public sealed record CppClosureRootPath(string Root, CppClosurePath Path);

    /// <summary>
    /// ⭐ C++ closures (#140, ADR-0010 D1 as ruled for #140). Every ROOT goes through
    /// <see cref="ClosureLowering"/> with C++'s options; a root the lowering cannot lower is emitted
    /// by the <c>[=]</c> path ONLY when <c>CheckLambdaCaptureWrites</c> proves a by-copy capture
    /// sound for it; otherwise the program is refused with that rule's text first and the lowering's
    /// reason after it. Both paths produce the same delegate type, <c>std::function</c>.
    ///
    /// <list type="bullet">
    /// <item><b>Environments are NESTED classes</b>: inside the class whose member created them
    /// (<see cref="IRClass.EnclosingClass"/>, chains included), and for a module procedure inside one
    /// holder struct emitted after every class. An inline member body of a nested class is a
    /// complete-class context of every enclosing class, so environments, their creator's class and
    /// each other may use one another in any order — there is no declaration-order problem to
    /// solve (#233 does not arise between them).</item>
    /// <item><b>A delegate value</b> (<see cref="IRDelegateCreate"/>) is a <c>std::function</c> built
    /// from a C++ lambda that holds the target's <c>shared_ptr</c> by copy and forwards to the bound
    /// method. Copying the handle shares the environment, which is by-reference capture.</item>
    /// <item><b>A captured Catch variable</b> lives in an environment field as a
    /// <c>std::exception_ptr</c> taken in the handler, so the exception keeps its dynamic type:
    /// <c>.Message</c> is its <c>what()</c>, and <c>Throw</c> rethrows the same object.</item>
    /// <item><b>Names</b>: the lowering's are MSIL-shaped (<c>&lt;&gt;c__Env0</c>, <c>__lambda_0</c>);
    /// the C++ spellings are minted once per module so that they avoid every name the program owns
    /// (ADR-0018's sets: <see cref="IRTempNames.ModuleLevelNames"/>, every function's reserved names,
    /// parameters and locals, and every type name) — never chosen by spelling alone.</item>
    /// </list>
    /// </summary>
    public partial class CppCodeGenerator
    {
        /// <summary>The C++ spelling of every name the closure lowering introduced (environment
        /// classes, lowered lambdas' methods, the holder struct, the delegate thunk's own names),
        /// keyed by the IR's spelling. Read by <see cref="SanitizeName"/>; empty for a module the
        /// lowering did not touch.</summary>
        private readonly Dictionary<string, string> _closureNames = new(StringComparer.Ordinal);

        private const string HolderKey = "<>closures";
        private const string ThunkTargetKey = "<>target";
        private const string ThunkArgumentKey = "<>arg";

        private readonly List<CppClosureRootPath> _closurePaths = new();

        /// <summary>
        /// TEST SEAM (#140 ruling D5.4): the path each root that creates a lambda took in the last
        /// <see cref="Generate"/> / <see cref="GenerateSplit"/>, in the module's function order. A
        /// lowering regression that pushes a root onto <c>[=]</c> is visible here even when the
        /// by-copy rule happens to accept it.
        /// </summary>
        public IReadOnlyList<CppClosureRootPath> ClosurePaths => _closurePaths;

        /// <summary>
        /// Lowers <paramref name="module"/>'s closures for C++ and returns the module to emit — the
        /// input itself when there is nothing to lower. Throws <see cref="CppCapabilityException"/> for
        /// a root neither path can take.
        /// </summary>
        private IRModule LowerClosures(IRModule module)
        {
            _closurePaths.Clear();
            _closureNames.Clear();
            _fallbackAddressOf.Clear();
            _fallbackMethodReads.Clear();
            _memberImplementations = null;

            var result = ClosureLowering.Run(module,
                new ClosureLoweringOptions("C++", MaxActionArity: null, MaxFuncArity: null, UnloweredRootPolicy.Skip));
            var lowered = result.Module;

            var skipped = new HashSet<IRFunction>(result.SkippedRoots.Select(s => s.Root), ReferenceEqualityComparer.Instance);
            if (skipped.Count > 0)
            {
                var unsound = new HashSet<IRFunction>(ReferenceEqualityComparer.Instance);
                var byCopy = CppCapabilityChecker.CheckLambdaCaptureWrites(lowered, skipped.Contains, unsound);
                if (byCopy.Count > 0)
                {
                    var reasons = result.SkippedRoots.Where(s => unsound.Contains(s.Root))
                        .Select(s => $"closure lowering cannot lower '{RootDisplayName(lowered, s.Root)}' either (#140): {s.Reason.Message}");
                    throw new CppCapabilityException(byCopy.Concat(reasons).Distinct().ToList());
                }
            }

            RecordClosurePaths(module, lowered, skipped);

            if (!ReferenceEquals(lowered, module))
            {
                // The lowered clone is new IR: verified under the optimizer's invariants, as MSIL does.
                BasicLang.Compiler.IR.Optimization.IRVerifier.VerifyAfterOptimization(lowered, lowered: true);
                MintClosureNames(module, lowered);
            }
            return lowered;
        }

        private void RecordClosurePaths(IRModule original, IRModule lowered, HashSet<IRFunction> skipped)
        {
            if (original?.Functions == null || !original.Functions.Any(f => f != null && f.IsLambda)) return;
            // The roots that create a lambda, by the shared definition, on the INPUT (the lowered
            // module no longer names its lowered lambdas).
            var creators = ClosureLowering.CreatorsOf(original);
            var roots = new HashSet<IRFunction>(creators.Keys.Select(l => ClosureLowering.RootOf(l, creators)), ReferenceEqualityComparer.Instance);
            var skippedNames = new HashSet<string>(skipped.Select(r => RootDisplayName(lowered, r)), StringComparer.Ordinal);
            foreach (var root in ClosureLowering.AllFunctions(original).Where(roots.Contains))
            {
                var name = RootDisplayName(original, root);
                _closurePaths.Add(new CppClosureRootPath(name, skippedNames.Contains(name) ? CppClosurePath.ByCopy : CppClosurePath.Lowered));
            }
            // A lambda outside every function body (a field or module initializer) is its own root,
            // and the lowering never lowers one.
            foreach (var root in skipped.Where(r => r.IsLambda))
                _closurePaths.Add(new CppClosureRootPath(root.Name, CppClosurePath.ByCopy));
        }

        private static string RootDisplayName(IRModule module, IRFunction root)
        {
            var owner = ClosureLowering.OwnerClassOf(module, root, out _);
            if (owner == null) return root.Name;
            var ctor = owner.Constructors.Any(c => ReferenceEquals(c?.Implementation, root));
            var member = ctor ? "New"
                : owner.Methods.FirstOrDefault(m => ReferenceEquals(m?.Implementation, root))?.Name
                  ?? owner.Properties.FirstOrDefault(p => ReferenceEquals(p?.Getter, root) || ReferenceEquals(p?.Setter, root))?.Name
                  ?? root.Name;
            return owner.Name + "." + member;
        }

        // =========================================================================================
        // Names
        // =========================================================================================

        /// <summary>
        /// Mints the C++ spelling of every name the lowering introduced, avoiding every name the
        /// program owns (ADR-0018: reserve, never mangle by spelling). The candidates are the
        /// sanitized IR spellings; a taken one gets the first free <c>_N</c> suffix, compared ignoring
        /// case (BasicLang is case-insensitive and so is the set).
        /// </summary>
        private void MintClosureNames(IRModule original, IRModule lowered)
        {
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in IRTempNames.ModuleLevelNames(original)) Add(name);
            foreach (var fn in IRTempNames.AllFunctions(original))
            {
                if (fn.IsLambda) continue;   // a lambda's own IR name is what is being renamed
                foreach (var p in fn.Parameters) Add(p?.Name);
                foreach (var l in fn.LocalVariables) Add(l?.Name);
                foreach (var r in fn.ReservedNames) Add(r);
            }
            foreach (var fn in IRTempNames.AllFunctions(original).Where(f => f.IsLambda))
            {
                foreach (var p in fn.Parameters) Add(p?.Name);
                foreach (var l in fn.LocalVariables) Add(l?.Name);
                foreach (var r in fn.ReservedNames) Add(r);
            }
            foreach (var name in original.Classes.Keys) Add(name);
            foreach (var name in original.Interfaces.Keys) Add(name);
            foreach (var name in original.Enums.Keys) Add(name);
            foreach (var name in original.Delegates.Keys) Add(name);
            // Lambda function names are the IR's own and are being renamed — unless the program also
            // spells one, which the per-function sets above already hold.
            foreach (var lambda in IRTempNames.AllFunctions(original).Where(f => f.IsLambda)) taken.Remove(lambda.Name ?? "");

            void Add(string name)
            {
                if (!string.IsNullOrEmpty(name)) taken.Add(base.SanitizeName(name));
            }
            string Mint(string wanted)
            {
                var name = wanted;
                for (var k = 1; !taken.Add(name); k++) name = $"{wanted}_{k}";
                return name;
            }

            _closureNames[HolderKey] = Mint("BasicLangClosures");
            _closureNames[ThunkTargetKey] = Mint("blTarget");
            _closureNames[ThunkArgumentKey] = Mint("blArg");
            foreach (var env in lowered.Classes.Values.Where(ClosureLowering.IsEnvironmentClass))
            {
                _closureNames[env.Name] = Mint(base.SanitizeName(env.Name));
                foreach (var method in env.Methods)
                    if (method?.Name != null && !_closureNames.ContainsKey(method.Name))
                        _closureNames[method.Name] = Mint(base.SanitizeName(method.Name));
            }
        }

        /// <summary>The C++ spelling of an IR name: a closure name's minted spelling, else the usual.</summary>
        protected override string SanitizeName(string name) =>
            name != null && _closureNames.Count > 0 && _closureNames.TryGetValue(name, out var minted)
                ? minted
                : base.SanitizeName(name);

        // =========================================================================================
        // Environments
        // =========================================================================================

        private bool IsEnvironmentClassName(string name) =>
            name != null && name.StartsWith(ClosureLowering.EnvironmentPrefix, StringComparison.Ordinal)
            && _module?.Classes != null && _module.Classes.ContainsKey(name);

        /// <summary>
        /// An environment class as C++ spells it from anywhere: every enclosing class named, outermost
        /// first (an environment created inside a lowered lambda of a class member is nested in the
        /// member's class too), and a module procedure's environments inside the holder struct.
        /// </summary>
        private string EnvironmentTypeName(string envName)
        {
            var parts = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var name = envName; name != null && seen.Add(name);)
            {
                parts.Insert(0, SanitizeName(name));
                if (!_module.Classes.TryGetValue(name, out var cls) || cls == null) break;
                if (string.IsNullOrEmpty(cls.EnclosingClass))
                {
                    if (IsEnvironmentClassName(name)) parts.Insert(0, _closureNames[HolderKey]);
                    break;
                }
                name = cls.EnclosingClass;
            }
            return string.Join("::", parts);
        }

        /// <summary>The environments declared directly inside <paramref name="enclosingName"/> (null:
        /// a module procedure's), in creation order.</summary>
        private List<IRClass> EnvironmentsIn(string enclosingName) =>
            (_module?.Classes?.Values ?? Enumerable.Empty<IRClass>())
            .Where(c => c != null && ClosureLowering.IsEnvironmentClass(c)
                        && string.Equals(c.EnclosingClass ?? "", enclosingName ?? "", StringComparison.Ordinal))
            .ToList();

        /// <summary>Written at the top of a class body's public section: the environments its
        /// members create, each forward-declared first because a field may name a later sibling.</summary>
        private void EmitNestedEnvironments(IRClass enclosing)
        {
            var envs = EnvironmentsIn(enclosing.Name);
            if (envs.Count == 0) return;
            foreach (var env in envs) WriteLine($"class {SanitizeName(env.Name)};");
            foreach (var env in envs)
            {
                GenerateClass(env);
                WriteLine();
            }
        }

        /// <summary>Written after every class body: the holder of the module procedures' environments.</summary>
        private void EmitClosureHolder()
        {
            var envs = EnvironmentsIn(null);
            if (envs.Count == 0) return;
            WriteLine("// Closure environments of module procedures");
            WriteLine($"struct {_closureNames[HolderKey]}");
            WriteLine("{");
            Indent();
            foreach (var env in envs) WriteLine($"class {SanitizeName(env.Name)};");
            foreach (var env in envs)
            {
                GenerateClass(env);
                WriteLine();
            }
            Unindent();
            WriteLine("};");
            WriteLine();
        }

        // =========================================================================================
        // Delegate values
        // =========================================================================================

        /// <summary>
        /// A delegate value: <c>t = [target = handle](P0 a0, …) -&gt; R { return target-&gt;M(a0, …); };</c>
        /// assigned to the slot's <c>std::function</c>. The target is the environment (a lowered
        /// lambda) or the object (<c>AddressOf obj.M</c>); with none, the method is a module procedure
        /// or a Shared method and is called directly. A virtual method dispatches through the target,
        /// as <c>-&gt;</c> does.
        /// </summary>
        public override void Visit(IRDelegateCreate create)
        {
            var method = create.Method
                ?? throw new CppCapabilityException(new List<string> { "a delegate value with no method to bind (#140)" });
            var target = create.Target != null ? GetValueName(create.Target) : null;
            WriteLine($"{GetValueName(create)} = {DelegateValueText(method, target)};");
        }

        /// <summary>The forwarding closure a delegate value over <paramref name="method"/> is: it holds
        /// <paramref name="targetValue"/> (null: none) by copy and calls the method through it. The ONE
        /// spelling of a bound delegate, for a lowered root's <see cref="IRDelegateCreate"/> and a by-copy
        /// root's <c>AddressOf</c> alike (#201).</summary>
        private string DelegateValueText(IRFunction method, string targetValue)
        {
            var (declaring, declared) = DeclaringMethodOf(method);

            var argument = _closureNames.TryGetValue(ThunkArgumentKey, out var a) ? a : "blArg";
            var target = _closureNames.TryGetValue(ThunkTargetKey, out var t) ? t : "blTarget";
            var parameters = method.Parameters.Select((p, i) => (Type: MapType(p.Type), Name: $"{argument}{i}")).ToList();
            var parameterList = string.Join(", ", parameters.Select(p => $"{p.Type} {p.Name}"));
            var argumentList = string.Join(", ", parameters.Select(p => p.Name));
            var returnType = declared?.ReturnType ?? method.ReturnType;
            var ret = MapType(returnType);
            var isVoid = ClosureLowering.IsVoid(returnType) || ret == "void";
            var methodName = SanitizeName(declared?.Name ?? method.Name);

            string capture = "", callee;
            if (targetValue != null)
            {
                capture = $"{target} = {targetValue}";
                callee = $"{target}->{methodName}";
            }
            else if (declaring != null)
            {
                var owner = IsEnvironmentClassName(declaring.Name) ? EnvironmentTypeName(declaring.Name) : SanitizeName(declaring.Name);
                callee = $"{owner}::{methodName}";
            }
            else
            {
                callee = methodName;
            }

            var body = isVoid ? $"{callee}({argumentList});" : $"return {callee}({argumentList});";
            return $"[{capture}]({parameterList}){(isVoid ? "" : " -> " + ret)} {{ {body} }}";
        }

        /// <summary>The <c>std::function</c> that holds <see cref="DelegateValueText"/>'s closure over
        /// <paramref name="method"/>: its parameter and return types exactly as the closure spells them.</summary>
        private string DelegateValueType(IRFunction method)
        {
            var (_, declared) = DeclaringMethodOf(method);
            var returnType = declared?.ReturnType ?? method.ReturnType;
            var ret = MapType(returnType);
            if (ClosureLowering.IsVoid(returnType)) ret = "void";
            return $"std::function<{ret}({string.Join(", ", method.Parameters.Select(p => MapType(p.Type)))})>";
        }

        private (IRClass Declaring, IRMethod Declared) DeclaringMethodOf(IRFunction method)
        {
            var declaring = _module.Classes.Values.FirstOrDefault(
                c => c?.Methods != null && c.Methods.Any(m => ReferenceEquals(m?.Implementation, method)));
            return (declaring, declaring?.Methods.First(m => ReferenceEquals(m?.Implementation, method)));
        }

        // =========================================================================================
        // AddressOf on the by-copy fallback (#201)
        // =========================================================================================
        //
        // A root the lowering refused (ADR-0019's fallback) reaches this backend with its AddressOf as
        // written — an IRUnaryOp, never an IRDelegateCreate. Two of its shapes did not compile: an
        // instance or Shared method (`AddressOf obj.M`, `AddressOf Me.M`, a bare method of the class)
        // rendered as the member READ `obj->M`, and every AddressOf temp was declared `auto` at its
        // assignment (§8.4 / D-P12), which a goto from before it to a label after it may not cross —
        // a Return or a Select arm makes exactly that goto. Where ClosureLowering.ResolveAddressOf
        // names the target, the IR's pointer-to-return type is no longer the only spelling available:
        //   * a module procedure keeps its name (`t = Inc;`, a function pointer as before) and its
        //     temp is declared with the rest, as `decltype(&Inc)` — exactly what `auto` deduced;
        //   * a class method — bound to its receiver, or Shared — is the lowered path's own forwarding
        //     closure (DelegateValueText), and its temp is declared as that closure's std::function.
        // A target the resolver cannot name keeps `auto`, byte-identical to before.

        /// <summary>Every by-copy AddressOf this module renders through its resolved target, by node.</summary>
        private readonly Dictionary<IRUnaryOp, ClosureLowering.AddressOfTarget> _fallbackAddressOf = new(ReferenceEqualityComparer.Instance);

        /// <summary>The member "read" IRBuilder emitted for a by-copy <c>AddressOf obj.M</c> rendered
        /// through its target: it is not a read, and emits nothing.</summary>
        private readonly HashSet<IRFieldAccess> _fallbackMethodReads = new(ReferenceEqualityComparer.Instance);

        private HashSet<IRFunction> _memberImplementations;

        /// <summary>Records <paramref name="function"/>'s AddressOf nodes the fallback renders through
        /// their target. Called before the temporaries are collected, so a method read is never one.</summary>
        private void CollectFallbackAddressOf(IRFunction function)
        {
            foreach (var block in function.Blocks)
                foreach (var instruction in block.Instructions)
                {
                    if (instruction is not IRUnaryOp { Operation: UnaryOpKind.AddressOf } u || _fallbackAddressOf.ContainsKey(u))
                        continue;
                    _memberImplementations ??= _module.CollectMemberImplementations();
                    var resolved = ClosureLowering.ResolveAddressOf(_module, _memberImplementations, _emittingClass, u.Operand);
                    if (resolved == null || !CanRenderFallbackAddressOf(function, u, resolved)) continue;
                    _fallbackAddressOf[u] = resolved;
                    if (resolved.MethodRead != null) _fallbackMethodReads.Add(resolved.MethodRead);
                }
        }

        private bool CanRenderFallbackAddressOf(IRFunction function, IRUnaryOp u, ClosureLowering.AddressOfTarget resolved)
        {
            if (IsModuleProcedure(resolved.Method)) return true;
            // A template's parameter types are not in scope where the closure is written.
            if (resolved.Method.GenericParameters is { Count: > 0 }) return false;
            if (DeclaringMethodOf(resolved.Method).Declaring is not { } declaring
                || declaring.GenericParameters is { Count: > 0 }) return false;
            // The read is also used as a value: no rendering of it compiles (the lowering refuses it).
            if (resolved.MethodRead != null && ClosureLowering.IsUsedElsewhere(function, resolved.MethodRead, u)) return false;
            if (!resolved.BindsMe) return true;
            // A bare instance method binds Me, which exists only in an instance member's root.
            if (_emittingClass is not { IsStruct: false }) return false;
            var root = function.IsLambda ? ClosureLowering.RootOf(function, ClosureLowering.CreatorsOf(_module)) : function;
            return ClosureLowering.OwnerClassOf(_module, root, out var isInstance) != null && isInstance;
        }

        private bool IsModuleProcedure(IRFunction method) => !_memberImplementations.Contains(method);

        /// <summary>The declared type of a by-copy AddressOf temp rendered through its target.</summary>
        private string FallbackAddressOfType(IRUnaryOp u, ClosureLowering.AddressOfTarget resolved) =>
            IsModuleProcedure(resolved.Method) ? $"decltype(&{GetValueName(u.Operand)})" : DelegateValueType(resolved.Method);

        private void VisitFallbackAddressOf(IRUnaryOp u, ClosureLowering.AddressOfTarget resolved)
        {
            string value;
            if (IsModuleProcedure(resolved.Method))
                value = GetValueName(u.Operand);
            else
            {
                var receiver = resolved.BindsMe ? new IRVariable("Me", new TypeInfo(_emittingClass.Name, TypeKind.Class)) : resolved.Receiver;
                value = DelegateValueText(resolved.Method, receiver != null ? GetValueName(receiver) : null);
            }
            WriteLine($"{GetValueName(u)} = {value};");
        }

        // =========================================================================================
        // A captured Catch variable
        // =========================================================================================

        private const string CapturedExceptionType = "std::exception_ptr";

        /// <summary>An environment field holding a captured Catch variable.</summary>
        private static bool IsCapturedExceptionField(IRClass owner, IRField field) =>
            ClosureLowering.IsEnvironmentClass(owner) && CppExceptionTypes.IsNetException(field?.Type?.Name);

        /// <summary>A read of such a field (the lowering marks its environment accesses as storage).</summary>
        private static bool IsCapturedExceptionValue(IRValue value) =>
            value is IRFieldAccess { IsStorageAccess: true } access && CppExceptionTypes.IsNetException(access.Type?.Name);

        /// <summary><c>x.Message</c> where x is a captured Catch variable read from its environment.</summary>
        private static bool IsCapturedExceptionMessage(IRValue value) =>
            value is IRFieldAccess { Object: var captured } fa
            && string.Equals(fa.FieldName, "Message", StringComparison.OrdinalIgnoreCase)
            && IsCapturedExceptionValue(captured);

        /// <summary>The C++ type of a class field: a captured Catch variable's is a
        /// <c>std::exception_ptr</c>, every other field's its mapped type.</summary>
        private string FieldCppType(IRClass owner, IRField field) =>
            IsCapturedExceptionField(owner, field) ? CapturedExceptionType : MapType(field.Type);

        /// <summary>The handler storing its caught exception into the environment.</summary>
        private static bool IsCapturedExceptionStore(IRFieldStore store) =>
            store.IsStorageAccess && store.Value is IRVariable caught
            && CppExceptionTypes.IsNetException(caught.Type?.Name)
            && ClosureLowering.IsEnvironmentType(store.Object?.Type);

        /// <summary>The <c>what()</c> of a <c>std::exception_ptr</c>, as one expression.</summary>
        private static string CapturedExceptionMessage(string pointer) =>
            $"BasicLang::String([&]() -> std::string {{ try {{ std::rethrow_exception({pointer}); }} "
            + "catch (const std::exception& e) { return e.what(); } catch (...) { return std::string(); } }())";
    }
}
