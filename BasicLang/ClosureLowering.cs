using System;
using System.Collections.Generic;
using System.Linq;
using BasicLang.Compiler.CodeGen;
using BasicLang.Compiler.IR.Optimization;
using BasicLang.Compiler.SemanticAnalysis;

namespace BasicLang.Compiler.IR
{
    /// <summary>
    /// ⭐ CLOSURE CONVERSION (ADR-0010): an IR→IR pass that turns every lambda into an instance
    /// method on a synthesised ENVIRONMENT class, every captured variable into a field of one,
    /// and every lambda value and <c>AddressOf</c> into an <see cref="IRDelegateCreate"/>.
    ///
    /// <para><b>Opt-in, after the optimizer and the verifier (D1).</b> Only a backend that asks
    /// for it runs it — MSIL today, from the top of <c>MSILCodeGenerator.Generate</c>, which is the
    /// one seam every MSIL entry point (the CLI, a <c>.blproj</c> build, the IDE, the test harness)
    /// goes through. It lowers a CLONE: the module it is handed is never written, so a pipeline
    /// that emits MSIL and then C# from one module gives C# exactly what it got before. C#,
    /// JavaScript and C++ lower lambdas their own way and never see the output.</para>
    ///
    /// <para><b>The environment model (D2 as amended, D5, D6).</b></para>
    /// <list type="bullet">
    /// <item>One FUNCTION environment per function that creates a lambda, allocated at entry. It
    /// holds every captured variable of that function — locals, by-value parameters (copied in at
    /// entry), Catch variables — and, for the outermost creator, <c>Me</c>.</item>
    /// <item>One ITERATION environment per iteration of a DECLARING <see cref="IRForEach"/>
    /// whose variable is captured, allocated at the top of its body and holding that variable.
    /// A non-declaring For Each (#168's hidden <c>__foreach_N</c>) gets none: its variable is a
    /// function-level local.</item>
    /// <item>Every environment but the outermost holds a <c>__parent</c> reference to the next
    /// one out, and a lambda is an instance method on the INNERMOST environment it captures
    /// from, so each delegate has exactly one target object and everything it can reach is on
    /// that object's parent chain.</item>
    /// </list>
    ///
    /// <para><b>Binding is the front end's (D3).</b> The pass never re-resolves which variable a
    /// name means: what is hoisted is (a creator's locals ∪ by-value parameters) ∩ the capture
    /// sets #122 recorded, and the two shapes where that name-based set is ambiguous (N9, #169)
    /// are refused, never guessed. What it does resolve is what the MSIL backend used to resolve
    /// for the lambda from its creator's context — a bare member of the creator's class — so the
    /// lowered lambda, emitted in the environment's context, still means the same member.</para>
    /// </summary>
    public static class ClosureLowering
    {
        /// <summary>Every environment class's name starts with this. <c>&lt;&gt;</c> is not an
        /// identifier character in BasicLang, so no user type can ever be mistaken for one.</summary>
        public const string EnvironmentPrefix = "<>c__Env";

        /// <summary>D7, MEASURED: the highest <c>Action`N</c> that assembles and runs through the
        /// generated <c>[mscorlib]</c> reference on .NET 8 (<c>Action`9</c> is a TypeLoadException —
        /// the facade does not forward it).</summary>
        public const int MaxActionTypeArguments = 8;

        /// <summary>D7, MEASURED: the highest <c>Func`N</c> reachable the same way (<c>Func`10</c>
        /// is a TypeLoadException).</summary>
        public const int MaxFuncTypeArguments = 9;

        /// <summary>True for a type this pass synthesised as an environment.</summary>
        public static bool IsEnvironmentType(TypeInfo type) =>
            type?.Name != null && type.Name.StartsWith(EnvironmentPrefix, StringComparison.Ordinal);

        /// <summary>True for a class this pass synthesised as an environment.</summary>
        public static bool IsEnvironmentClass(IRClass irClass) =>
            irClass?.Name != null && irClass.Name.StartsWith(EnvironmentPrefix, StringComparison.Ordinal);

        /// <summary>
        /// ⭐ THE ENTRY POINT. Returns <paramref name="module"/> itself when it holds nothing to
        /// lower — no lambda, no <c>AddressOf</c>, no call through a delegate value — so a program
        /// without delegates reaches the backend byte-for-byte as before. Otherwise returns a
        /// lowered CLONE and leaves <paramref name="module"/> untouched.
        ///
        /// <para>Post-condition (asserted, else <see cref="InvalidOperationException"/>): no
        /// <see cref="IRFunction.IsLambda"/> function remains, and no <see cref="IRVariable"/> in
        /// a former lambda body names a variable of an enclosing function. A construct the first
        /// cut does not lower (D9) throws <see cref="ForeignFeatureException"/> naming it.</para>
        /// </summary>
        public static IRModule Run(IRModule module)
        {
            if (module == null) return null;
            if (!NeedsLowering(module)) return module;

            var lowered = ModuleCloner.Clone(module);
            new Lowerer(lowered).Run();
            return lowered;
        }

        // =====================================================================================
        // Is there anything to do?
        // =====================================================================================

        private static bool NeedsLowering(IRModule module)
        {
            if (module.Functions.Any(f => f != null && f.IsLambda)) return true;

            foreach (var function in AllFunctions(module))
            {
                if (function.Blocks == null) continue;
                var ownerClass = OwnerClassOf(module, function, out var isInstance);
                foreach (var inst in function.Blocks.SelectMany(b => b.Instructions))
                {
                    switch (inst)
                    {
                        case IRUnaryOp { Operation: UnaryOpKind.AddressOf }:
                            return true;
                        case IRCall call when call.CalleeValue != null:
                            return true;
                        case IRCall call when IsBareName(call.FunctionName):
                            if (IsDelegateType(module, DeclaredDelegateCandidate(module, function, ownerClass, isInstance, call.FunctionName)))
                                return true;
                            break;
                    }
                }
            }
            return false;
        }

        /// <summary>The declared type of a name a call could be invoking as a variable, looked up
        /// the way <see cref="Lowerer"/> does, without block scoping (a superset is fine here).</summary>
        private static TypeInfo DeclaredDelegateCandidate(IRModule module, IRFunction function, IRClass ownerClass,
            bool isInstance, string name)
        {
            foreach (var p in function.Parameters ?? new List<IRVariable>())
                if (NameEquals(p?.Name, name)) return p.Type;
            foreach (var l in function.LocalVariables ?? new List<IRVariable>())
                if (NameEquals(l?.Name, name)) return l.Type;
            foreach (var fe in function.Blocks.SelectMany(b => b.Instructions).OfType<IRForEach>())
                if (NameEquals(fe.VariableName, name)) return fe.ElementType;
            if (ownerClass != null && TryFindField(module, ownerClass, name, out _, out var field)
                && (field.IsStatic || isInstance))
                return field.Type;
            foreach (var g in module.GlobalVariables.Values)
                if (NameEquals(g?.Name, name)) return g.Type;
            return null;
        }

        // =====================================================================================
        // Shared helpers: names, types, delegates.
        // =====================================================================================

        internal static bool NameEquals(string a, string b) =>
            a != null && b != null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        private static bool IsBareName(string name) =>
            !string.IsNullOrEmpty(name) && name.IndexOf('.') < 0 && !name.Contains("::");

        private static bool IsLambdaReferenceName(string name) =>
            name != null && name.StartsWith("__lambda_", StringComparison.Ordinal);

        private static bool IsMeName(string name) =>
            NameEquals(name, "Me") || NameEquals(name, "MyBase");

        private static string StripSystem(string name) =>
            name != null && name.StartsWith("System.", StringComparison.OrdinalIgnoreCase) ? name.Substring(7) : name;

        /// <summary>Every function body in the module: module procedures, class members, interface
        /// default implementations. Distinct, in a stable order.</summary>
        internal static List<IRFunction> AllFunctions(IRModule module)
        {
            var all = new List<IRFunction>();
            var seen = new HashSet<IRFunction>(ReferenceEqualityComparer.Instance);
            void Add(IRFunction f)
            {
                if (f != null && seen.Add(f)) all.Add(f);
            }
            foreach (var f in module.Functions) Add(f);
            foreach (var cls in module.Classes.Values)
            {
                foreach (var m in cls.Methods) Add(m?.Implementation);
                foreach (var c in cls.Constructors) Add(c?.Implementation);
                foreach (var p in cls.Properties) { Add(p?.Getter); Add(p?.Setter); }
            }
            foreach (var iface in module.Interfaces.Values)
                foreach (var m in iface.Methods) Add(m?.DefaultImplementation);
            return all;
        }

        /// <summary>The class <paramref name="function"/> is a member of, and whether it runs with
        /// a <c>Me</c>; null for a module procedure.</summary>
        internal static IRClass OwnerClassOf(IRModule module, IRFunction function, out bool isInstance)
        {
            isInstance = false;
            foreach (var cls in module.Classes.Values)
            {
                var method = cls.Methods.FirstOrDefault(m => ReferenceEquals(m?.Implementation, function));
                if (method != null) { isInstance = !method.IsStatic; return cls; }
                if (cls.Constructors.Any(c => ReferenceEquals(c?.Implementation, function))) { isInstance = true; return cls; }
                var prop = cls.Properties.FirstOrDefault(p => ReferenceEquals(p?.Getter, function) || ReferenceEquals(p?.Setter, function));
                if (prop != null) { isInstance = !prop.IsStatic; return cls; }
            }
            return null;
        }

        private static bool TryFindClass(IRModule module, string name, out IRClass cls)
        {
            cls = null;
            if (string.IsNullOrEmpty(name)) return false;
            return module.Classes.TryGetValue(name, out cls) && cls != null;
        }

        /// <summary>A field declared by <paramref name="cls"/> or a base in the module, nearest first.</summary>
        internal static bool TryFindField(IRModule module, IRClass cls, string name, out IRClass declaring, out IRField field)
        {
            declaring = null;
            field = null;
            var seen = new HashSet<IRClass>(ReferenceEqualityComparer.Instance);
            for (var current = cls; current != null && seen.Add(current);)
            {
                field = current.Fields?.FirstOrDefault(f => NameEquals(f?.Name, name));
                if (field != null) { declaring = current; return true; }
                if (current.Properties != null && current.Properties.Any(p => NameEquals(p?.Name, name))) return false;
                if (!TryFindClass(module, current.BaseClass, out current)) break;
            }
            field = null;
            return false;
        }

        /// <summary>A property declared by <paramref name="cls"/> or a base in the module, nearest
        /// first — unless a field of the same name is nearer.</summary>
        private static bool TryFindProperty(IRModule module, IRClass cls, string name, out IRClass declaring, out IRProperty prop)
        {
            declaring = null;
            prop = null;
            var seen = new HashSet<IRClass>(ReferenceEqualityComparer.Instance);
            for (var current = cls; current != null && seen.Add(current);)
            {
                if (current.Fields != null && current.Fields.Any(f => NameEquals(f?.Name, name))) return false;
                prop = current.Properties?.FirstOrDefault(p => NameEquals(p?.Name, name));
                if (prop != null) { declaring = current; return true; }
                if (!TryFindClass(module, current.BaseClass, out current)) break;
            }
            prop = null;
            return false;
        }

        /// <summary>A method declared by <paramref name="cls"/> or a base in the module, nearest first.</summary>
        private static bool TryFindMethod(IRModule module, IRClass cls, string name, out IRClass declaring, out IRMethod method)
        {
            declaring = null;
            method = null;
            var seen = new HashSet<IRClass>(ReferenceEqualityComparer.Instance);
            for (var current = cls; current != null && seen.Add(current);)
            {
                method = current.Methods?.FirstOrDefault(m => NameEquals(m?.Name, name));
                if (method != null) { declaring = current; return true; }
                if (!TryFindClass(module, current.BaseClass, out current)) break;
            }
            method = null;
            return false;
        }

        /// <summary>
        /// True for a type the program uses as a delegate: a user <c>Delegate</c> declaration, the
        /// BCL <c>Action</c>/<c>Func</c> (unless the program declares a class of that name), or
        /// anything the analyzer typed as a delegate.
        /// </summary>
        internal static bool IsDelegateType(IRModule module, TypeInfo type)
        {
            if (type?.Name == null) return false;
            if (module.Delegates.ContainsKey(type.Name)) return true;
            if (module.Classes.ContainsKey(type.Name) || module.Interfaces.ContainsKey(type.Name)
                || module.Enums.ContainsKey(type.Name)) return false;
            var bare = StripSystem(type.Name);
            if (NameEquals(bare, "Action") || NameEquals(bare, "Func")) return true;
            return type.Kind == TypeKind.Delegate;
        }

        /// <summary>
        /// The <c>Invoke</c> signature of delegate type <paramref name="type"/>, after generic
        /// substitution: parameter types and return type (null = Sub). False, with
        /// <paramref name="problem"/>, for a type that is not a delegate this pass can bind.
        /// Throws for an arity above the measured cap (D7).
        /// </summary>
        internal static bool TryInvokeSignature(IRModule module, TypeInfo type,
            out List<TypeInfo> parameters, out TypeInfo returnType, out string problem)
        {
            parameters = null;
            returnType = null;
            problem = null;
            if (type?.Name == null) { problem = "it has no type"; return false; }

            if (module.Delegates.TryGetValue(type.Name, out var declared) && declared != null)
            {
                if (declared.Parameters.Any(p => p.IsByRef))
                {
                    problem = $"the Delegate '{declared.Name}' takes a ByRef parameter";
                    return false;
                }
                parameters = declared.Parameters
                    .Select(p => p.Type ?? new TypeInfo(p.TypeName ?? "Object", TypeKind.Class)).ToList();
                returnType = IsVoid(declared.ReturnType) ? null : declared.ReturnType;
                return true;
            }

            if (module.Classes.ContainsKey(type.Name) || module.Interfaces.ContainsKey(type.Name))
            {
                problem = $"'{type.Name}' is a class or interface of this program, not a delegate";
                return false;
            }

            var bare = StripSystem(type.Name);
            var args = type.GenericArguments ?? new List<TypeInfo>();
            if (NameEquals(bare, "Action"))
            {
                if (args.Count > MaxActionTypeArguments)
                    throw new ForeignFeatureException(
                        $"MSIL: 'Action' with {args.Count} type arguments is above the supported arity. "
                        + $"Action`1..`{MaxActionTypeArguments} are the ones the generated [mscorlib] "
                        + "reference reaches on .NET 8 (measured: Action`9 is a TypeLoadException at run "
                        + "time — the facade does not forward it), so this is refused here rather than "
                        + "emitted. Declare your own Delegate type, or pass fewer arguments.");
                parameters = args.ToList();
                returnType = null;
                return true;
            }
            if (NameEquals(bare, "Func"))
            {
                if (args.Count == 0)
                {
                    problem = "'Func' arrived with its type arguments lost";
                    return false;
                }
                if (args.Count > MaxFuncTypeArguments)
                    throw new ForeignFeatureException(
                        $"MSIL: 'Func' with {args.Count} type arguments is above the supported arity. "
                        + $"Func`1..`{MaxFuncTypeArguments} are the ones the generated [mscorlib] "
                        + "reference reaches on .NET 8 (measured: Func`10 is a TypeLoadException at run "
                        + "time — the facade does not forward it), so this is refused here rather than "
                        + "emitted. Declare your own Delegate type, or pass fewer arguments.");
                parameters = args.Take(args.Count - 1).ToList();
                returnType = args[args.Count - 1];
                return true;
            }

            problem = type.Kind == TypeKind.Delegate
                ? $"'{type.Name}' is a delegate type this backend cannot name"
                : $"'{type.Name}' is not a delegate type";
            return false;
        }

        internal static bool IsVoid(TypeInfo type) =>
            type == null || NameEquals(type.Name, "Void") || type.Kind == TypeKind.Void;

        private static readonly Dictionary<string, string> TypeAliases = new(StringComparer.OrdinalIgnoreCase)
        {
            ["int32"] = "integer", ["int"] = "integer", ["integer"] = "integer",
            ["int64"] = "long", ["long"] = "long",
            ["int16"] = "short", ["short"] = "short",
            ["single"] = "single", ["float"] = "single", ["float32"] = "single",
            ["double"] = "double", ["float64"] = "double",
            ["boolean"] = "boolean", ["bool"] = "boolean",
            ["string"] = "string", ["object"] = "object", ["void"] = "void",
            ["char"] = "char", ["byte"] = "byte", ["uint8"] = "byte", ["decimal"] = "decimal",
        };

        /// <summary>A canonical spelling of a type for EXACT comparison (D7: no relaxation). The
        /// kind is ignored — the analyzer types the same delegate <c>Class</c> in one place and
        /// <c>Delegate</c> in another — and so is the <c>System.</c> prefix and case.</summary>
        internal static string Canon(TypeInfo type)
        {
            if (IsVoid(type)) return "void";
            var name = StripSystem(type.Name) ?? "";
            if (type.Kind == TypeKind.Array || name.EndsWith("[]", StringComparison.Ordinal))
            {
                var element = type.ElementType != null ? Canon(type.ElementType)
                    : Canon(new TypeInfo(name.EndsWith("[]", StringComparison.Ordinal) ? name.Substring(0, name.Length - 2) : name, TypeKind.Class));
                return element + "[]";
            }
            var key = TypeAliases.TryGetValue(name, out var alias) ? alias : name.ToLowerInvariant();
            if (type.GenericArguments != null && type.GenericArguments.Count > 0)
                key += "<" + string.Join(",", type.GenericArguments.Select(Canon)) + ">";
            return key;
        }

        internal static bool TypesEqual(TypeInfo a, TypeInfo b) => Canon(a) == Canon(b);

        private static string Show(TypeInfo type)
        {
            if (IsVoid(type)) return "Void";
            var name = type.Name;
            if (type.GenericArguments != null && type.GenericArguments.Count > 0)
                name += "(Of " + string.Join(", ", type.GenericArguments.Select(Show)) + ")";
            return name;
        }

        // =====================================================================================
        // The clone. The lowering writes the IR it is handed; the caller's module must never see it.
        // =====================================================================================

        /// <summary>
        /// A deep copy of every function body, and a shallow copy of every container that points
        /// at one, so the lowering can rewrite freely. Values that are never written — variables,
        /// constants, types, fields, descriptors — are SHARED, which is what keeps the clone's
        /// output identical to the original's for every function the lowering does not touch.
        /// </summary>
        private static class ModuleCloner
        {
            private static readonly System.Reflection.MethodInfo MemberwiseCloneMethod =
                typeof(object).GetMethod("MemberwiseClone",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

            private static T Shallow<T>(T value) where T : class => (T)MemberwiseCloneMethod.Invoke(value, null);

            public static IRModule Clone(IRModule module)
            {
                var functionMap = new Dictionary<IRFunction, IRFunction>(ReferenceEqualityComparer.Instance);
                var instructionMaps = new Dictionary<IRFunction, Dictionary<IRInstruction, IRInstruction>>(ReferenceEqualityComparer.Instance);
                foreach (var f in AllFunctions(module))
                {
                    var map = new Dictionary<IRInstruction, IRInstruction>(ReferenceEqualityComparer.Instance);
                    functionMap[f] = CloneFunction(f, map);
                    instructionMaps[f] = map;
                }

                IRFunction F(IRFunction f) => f != null && functionMap.TryGetValue(f, out var c) ? c : f;

                var clone = new IRModule(module.Name);
                foreach (var f in module.Functions) clone.Functions.Add(F(f));
                foreach (var kv in module.GlobalVariables) clone.GlobalVariables[kv.Key] = kv.Value;
                foreach (var kv in module.Types) clone.Types[kv.Key] = kv.Value;
                foreach (var kv in module.ExternDeclarations) clone.ExternDeclarations[kv.Key] = kv.Value;
                foreach (var kv in module.Enums) clone.Enums[kv.Key] = kv.Value;
                foreach (var kv in module.Delegates) clone.Delegates[kv.Key] = kv.Value;
                foreach (var kv in module.SourceLineOffsets) clone.SourceLineOffsets[kv.Key] = kv.Value;
                clone.Namespaces.AddRange(module.Namespaces);
                clone.NetUsings.AddRange(module.NetUsings);
                clone.CppIncludes.AddRange(module.CppIncludes);
                clone.JsImports.AddRange(module.JsImports);

                foreach (var kv in module.Interfaces)
                {
                    var iface = kv.Value;
                    if (iface == null || !iface.Methods.Any(m => m?.DefaultImplementation != null))
                    {
                        clone.Interfaces[kv.Key] = iface;
                        continue;
                    }
                    var ci = Shallow(iface);
                    ci.Methods = iface.Methods.Select(m =>
                    {
                        if (m?.DefaultImplementation == null) return m;
                        var cm = Shallow(m);
                        cm.DefaultImplementation = F(m.DefaultImplementation);
                        return cm;
                    }).ToList();
                    clone.Interfaces[kv.Key] = ci;
                }

                foreach (var kv in module.Classes)
                {
                    var cls = kv.Value;
                    if (cls == null) { clone.Classes[kv.Key] = null; continue; }
                    var cc = Shallow(cls);
                    cc.Interfaces = new List<string>(cls.Interfaces);
                    cc.Fields = new List<IRField>(cls.Fields);
                    cc.Events = new List<IREvent>(cls.Events);
                    cc.GenericParameters = new List<string>(cls.GenericParameters);
                    cc.GenericTypeParams = new List<BasicLang.Compiler.AST.GenericTypeParameter>(cls.GenericTypeParams);
                    cc.Methods = cls.Methods.Select(m =>
                    {
                        if (m == null) return null;
                        var cm = Shallow(m);
                        cm.Parameters = new List<IRVariable>(m.Parameters);
                        cm.GenericParameters = new List<string>(m.GenericParameters);
                        cm.Implementation = F(m.Implementation);
                        return cm;
                    }).ToList();
                    cc.Properties = cls.Properties.Select(p =>
                    {
                        if (p == null) return null;
                        var cp = Shallow(p);
                        cp.Getter = F(p.Getter);
                        cp.Setter = F(p.Setter);
                        return cp;
                    }).ToList();
                    cc.Constructors = cls.Constructors.Select(c =>
                    {
                        if (c == null) return null;
                        var ctor = Shallow(c);
                        ctor.Parameters = new List<IRVariable>(c.Parameters);
                        ctor.Implementation = F(c.Implementation);
                        // A base-constructor argument may be one of the body's own instructions
                        // (MSIL refuses those — see EmitBaseConstructorCall); keep the identity.
                        var map = c.Implementation != null && instructionMaps.TryGetValue(c.Implementation, out var m) ? m : null;
                        ctor.BaseConstructorArgs = c.BaseConstructorArgs
                            .Select(a => a is IRInstruction ai && map != null && map.TryGetValue(ai, out var mapped) ? (IRValue)mapped : a)
                            .ToList();
                        return ctor;
                    }).ToList();
                    clone.Classes[kv.Key] = cc;
                }

                return clone;
            }

            private static IRFunction CloneFunction(IRFunction f, Dictionary<IRInstruction, IRInstruction> map)
            {
                var nf = new IRFunction(f.Name, f.ReturnType)
                {
                    IsExternal = f.IsExternal,
                    IsAsync = f.IsAsync,
                    IsIterator = f.IsIterator,
                    IsExtension = f.IsExtension,
                    ExtendedType = f.ExtendedType,
                    IsLambda = f.IsLambda,
                    ModuleName = f.ModuleName,
                    SourceFilePath = f.SourceFilePath,
                    Access = f.Access,
                };
                nf.Parameters = new List<IRVariable>(f.Parameters);
                nf.LocalVariables = new List<IRVariable>(f.LocalVariables);
                nf.GenericParameters = new List<string>(f.GenericParameters);
                nf.GenericTypeParams = new List<BasicLang.Compiler.AST.GenericTypeParameter>(f.GenericTypeParams);
                nf.CapturedVariables = new List<(string name, TypeInfo type)>(f.CapturedVariables ?? new List<(string, TypeInfo)>());
                nf.LambdaCapturedNames = f.LambdaCapturedNames == null ? null : new HashSet<string>(f.LambdaCapturedNames, StringComparer.Ordinal);
                nf.LambdaCaptureSources = f.LambdaCaptureSources == null ? null : new HashSet<string>(f.LambdaCaptureSources, StringComparer.Ordinal);

                var blockMap = new Dictionary<BasicBlock, BasicBlock>(ReferenceEqualityComparer.Instance);
                foreach (var b in f.Blocks)
                {
                    blockMap[b] = new BasicBlock(b.Name)
                    {
                        Id = b.Id,
                        ParentFunction = nf,
                        IsVisited = b.IsVisited,
                    };
                }
                BasicBlock B(BasicBlock b) => b != null && blockMap.TryGetValue(b, out var nb) ? nb : b;

                // Every instruction in a block first, so an operand pointing at one finds its clone.
                foreach (var b in f.Blocks)
                    foreach (var inst in b.Instructions)
                        if (inst != null && !(inst is IRVariable) && !(inst is IRConstant))
                            map[inst] = Shallow(inst);

                IRValue V(IRValue v)
                {
                    if (v == null || v is IRVariable || v is IRConstant) return v;
                    if (map.TryGetValue(v, out var c)) return (IRValue)c;
                    // An operand tree outside every block (a When guard): cloned on demand.
                    var nc = Shallow(v);
                    map[v] = nc;
                    Fix(nc);
                    return nc;
                }

                void Fix(IRInstruction c)
                {
                    switch (c)
                    {
                        case IRCall call:
                            call.Arguments = new List<IRValue>(call.Arguments);
                            call.ByRefArguments = new List<bool>(call.ByRefArguments);
                            call.NetArgumentRefKinds = new List<BasicLang.Net.NetRefKind>(call.NetArgumentRefKinds);
                            call.GenericArguments = new List<TypeInfo>(call.GenericArguments);
                            break;
                        case IRInstanceMethodCall mc:
                            mc.Arguments = new List<IRValue>(mc.Arguments);
                            mc.ByRefArguments = new List<bool>(mc.ByRefArguments);
                            mc.GenericArguments = new List<TypeInfo>(mc.GenericArguments);
                            break;
                        case IRBaseMethodCall bc:
                            bc.Arguments = new List<IRValue>(bc.Arguments);
                            break;
                        case IRNewObject no:
                            no.Arguments = new List<IRValue>(no.Arguments);
                            break;
                        case IRGetElementPtr gep:
                            gep.Indices = new List<IRValue>(gep.Indices);
                            break;
                        case IRIndexerAccess ia:
                            ia.Indices = new List<IRValue>(ia.Indices);
                            break;
                        case IRIndexerStore ist:
                            ist.Indices = new List<IRValue>(ist.Indices);
                            break;
                        case IRPhi phi:
                            phi.Operands = phi.Operands.Select(o => (V(o.Value), B(o.Block))).ToList();
                            break;
                        case IRBranch br:
                            br.Target = B(br.Target);
                            break;
                        case IRConditionalBranch cb:
                            cb.TrueTarget = B(cb.TrueTarget);
                            cb.FalseTarget = B(cb.FalseTarget);
                            break;
                        case IRSwitch sw:
                            sw.DefaultTarget = B(sw.DefaultTarget);
                            sw.EndBlock = B(sw.EndBlock);
                            sw.Cases = sw.Cases.Select(cs => (cs.CaseValue, B(cs.Target))).ToList();
                            sw.PatternCases = sw.PatternCases.Select(ClonePattern).ToList();
                            break;
                        case IRForEach fe:
                            fe.BodyBlock = B(fe.BodyBlock);
                            fe.EndBlock = B(fe.EndBlock);
                            break;
                        case IRTryCatch tc:
                            tc.TryBlock = B(tc.TryBlock);
                            tc.FinallyBlock = B(tc.FinallyBlock);
                            tc.EndBlock = B(tc.EndBlock);
                            tc.CatchClauses = tc.CatchClauses.Select(cc => new IRCatchClause(cc.ExceptionType, cc.VariableName, B(cc.Block))
                            {
                                NetExceptionFullName = cc.NetExceptionFullName,
                            }).ToList();
                            break;
                        case IRConstant:
                        case IRVariable:
                        case IRBinaryOp:
                        case IRUnaryOp:
                        case IRCompare:
                        case IRLoad:
                        case IRStore:
                        case IRAlloca:
                        case IRReturn:
                        case IRCast:
                        case IRAssignment:
                        case IRLabel:
                        case IRComment:
                        case IRInlineCode:
                        case IRArrayAlloc:
                        case IRArrayStore:
                        case IRAwait:
                        case IRYield:
                        case IRThrow:
                        case IRFieldAccess:
                        case IRFieldStore:
                        case IRTupleElement:
                        case IRDelegateCreate:
                            break;
                        default:
                            // A node kind added after this was written: cloning it field-by-field
                            // might share a list the lowering then writes into the ORIGINAL module.
                            throw new InvalidOperationException(
                                $"ClosureLowering cannot clone the IR node kind {c.GetType().Name}. Give it an "
                                + "arm in ModuleCloner.Fix saying which of its members are mutable.");
                    }
                    OptimizationPass.MapOperands(c, V);
                }

                IRPatternCase ClonePattern(IRPatternCase p)
                {
                    if (p == null) return null;
                    var cp = Shallow(p);
                    cp.Target = B(p.Target);
                    cp.WhenGuard = V(p.WhenGuard);
                    switch (cp)
                    {
                        case IRRangePatternCase r:
                            r.LowerBound = V(r.LowerBound);
                            r.UpperBound = V(r.UpperBound);
                            break;
                        case IRComparisonPatternCase cmp:
                            cmp.CompareValue = V(cmp.CompareValue);
                            break;
                        case IRConstantPatternCase k:
                            k.Value = V(k.Value);
                            break;
                        case IROrPatternCase or:
                            or.Alternatives = or.Alternatives.Select(ClonePattern).ToList();
                            break;
                        case IRTuplePatternCase tuple:
                            tuple.Elements = tuple.Elements.Select(ClonePattern).ToList();
                            break;
                    }
                    return cp;
                }

                foreach (var b in f.Blocks)
                {
                    var nb = blockMap[b];
                    foreach (var inst in b.Instructions)
                    {
                        if (inst == null) { nb.Instructions.Add(null); continue; }
                        if (inst is IRVariable || inst is IRConstant)
                        {
                            nb.Instructions.Add(inst);   // shared, never written
                            continue;
                        }
                        var c = map[inst];
                        c.ParentBlock = nb;
                        nb.Instructions.Add(c);
                    }
                }

                // Operands and targets, after every block clone exists. The pattern cases of a
                // switch are cloned inside Fix, which also maps their operands.
                foreach (var b in f.Blocks)
                    foreach (var inst in b.Instructions)
                        if (inst != null && !(inst is IRVariable) && !(inst is IRConstant))
                            Fix(map[inst]);

                foreach (var b in f.Blocks)
                {
                    var nb = blockMap[b];
                    nb.Successors.AddRange(b.Successors.Select(B));
                    nb.Predecessors.AddRange(b.Predecessors.Select(B));
                    nb.ImmediateDominator = B(b.ImmediateDominator);
                    foreach (var d in b.Dominators) nb.Dominators.Add(B(d));
                    foreach (var d in b.DominanceFrontier) nb.DominanceFrontier.Add(B(d));
                    nf.Blocks.Add(nb);
                }
                nf.EntryBlock = B(f.EntryBlock);
                return nf;
            }
        }

        // =====================================================================================
        // The lowering.
        // =====================================================================================

        /// <summary>One environment: a synthesised class, and the local (or chain) it is reached by.</summary>
        private sealed class Level
        {
            public IRClass Env;
            public TypeInfo EnvType;
            public IRFunction Owner;
            public IRForEach Loop;                       // null for a function environment
            public HashSet<BasicBlock> Region;            // a loop level's body blocks
            public Level Parent;
            public string ParentField;
            public IRVariable LocalRef;                   // the Owner's local holding the instance
            public readonly Dictionary<string, (string Field, TypeInfo Type)> Vars =
                new(StringComparer.OrdinalIgnoreCase);
            public string MeField;                        // set on the root's function env when Me is captured
            public TypeInfo MeType;

            public bool Holds(string name) => name != null && Vars.ContainsKey(name);
        }

        private sealed class Root
        {
            public IRFunction Function;
            public IRClass Class;
            public bool IsInstance;
            public Level MeHolder;
            public bool AnyNeedsMe;
        }

        private sealed class FunctionContext
        {
            public IRFunction Function;
            public bool IsLambda;
            public Level Host;
            public readonly List<Level> HostChain = new();
            public Root Root;
            public Level FunctionEnv;
            public readonly List<Level> LoopLevels = new();
            public readonly List<(IRForEach Loop, HashSet<BasicBlock> Region)> Loops = new();
            public readonly Dictionary<string, IRVariable> Params = new(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, IRVariable> Locals = new(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, TypeInfo> CatchVars = new(StringComparer.OrdinalIgnoreCase);
            public readonly List<IRFunction> Lambdas = new();
            public readonly Dictionary<string, IRFunction> LambdaByName = new(StringComparer.Ordinal);
            public readonly Dictionary<IRFunction, HashSet<string>> Captures = new(ReferenceEqualityComparer.Instance);
            public readonly Dictionary<IRFunction, Level> Hosts = new(ReferenceEqualityComparer.Instance);
            public HashSet<IRInstruction> InBlock;
            public HashSet<string> TakenNames;
            public int TempCounter;
            public IRVariable MeRef;                      // `Me` of a lambda: its host environment

            public bool Declares(string name) =>
                Params.ContainsKey(name) || Locals.ContainsKey(name) || CatchVars.ContainsKey(name);
        }

        private enum BindingKind { None, Env, Me, InstanceMember, StaticMember }

        private readonly struct Binding
        {
            public Binding(BindingKind kind, Level level = null, string field = null, TypeInfo type = null,
                IRClass declaring = null, bool storage = true)
            {
                Kind = kind; Level = level; Field = field; Type = type; Declaring = declaring; Storage = storage;
            }
            public BindingKind Kind { get; }
            public Level Level { get; }
            public string Field { get; }
            public TypeInfo Type { get; }
            public IRClass Declaring { get; }
            public bool Storage { get; }
            public static Binding None => new(BindingKind.None);
        }

        private sealed class Lowerer
        {
            private readonly IRModule _module;
            private readonly Dictionary<string, IRFunction> _lambdas = new(StringComparer.Ordinal);
            private readonly Dictionary<IRFunction, IRFunction> _creatorOf = new(ReferenceEqualityComparer.Instance);
            private readonly Dictionary<IRFunction, List<IRFunction>> _created = new(ReferenceEqualityComparer.Instance);
            private readonly HashSet<IRFunction> _classMembers;
            private readonly HashSet<IRVariable> _synthetic = new(ReferenceEqualityComparer.Instance);
            private readonly HashSet<string> _userTempNames;
            private readonly List<FunctionContext> _lowered = new();
            private int _envCounter;

            public Lowerer(IRModule module)
            {
                _module = module;
                _classMembers = module.CollectMemberImplementations();
                _userTempNames = IRTempNames.UserOwned(module);
            }

            public void Run()
            {
                foreach (var f in _module.Functions)
                    if (f != null && f.IsLambda && f.Name != null) _lambdas[f.Name] = f;

                var functions = AllFunctions(_module);
                RefuseLambdasOutsideFunctionBodies();

                foreach (var f in functions)
                {
                    var created = LambdasReferencedBy(f);
                    _created[f] = created;
                    foreach (var lambda in created)
                    {
                        if (_creatorOf.TryGetValue(lambda, out var other) && !ReferenceEquals(other, f))
                            throw new InvalidOperationException(
                                $"ClosureLowering: lambda '{lambda.Name}' is referenced by both '{other.Name}' and "
                                + $"'{f.Name}'. IRBuilder gives every lambda exactly one creator.");
                        _creatorOf[lambda] = f;
                    }
                }

                foreach (var f in functions)
                {
                    if (f.IsLambda || f.IsExternal || f.Blocks == null) continue;
                    var ownerClass = OwnerClassOf(_module, f, out var isInstance);
                    var root = new Root { Function = f, Class = ownerClass, IsInstance = isInstance };
                    Process(new FunctionContext { Function = f, IsLambda = false, Root = root });
                }

                // A lambda nobody reachable creates is dead code: it is never called and has no
                // environment to live on. Dropped rather than emitted as a free static method.
                _module.Functions.RemoveAll(f => f != null && f.IsLambda);

                AssertPostConditions();

                foreach (var ctx in _lowered)
                {
                    ctx.Function.CapturedVariables = new List<(string name, TypeInfo type)>();
                    ctx.Function.LambdaCapturedNames = null;
                    ctx.Function.LambdaCaptureSources = null;
                }
            }

            // ---------------------------------------------------------------------------------
            // Discovery
            // ---------------------------------------------------------------------------------

            /// <summary>Every value a function's blocks reach, operand trees included.</summary>
            private static IEnumerable<IRValue> ValuesIn(IRFunction f)
            {
                var seen = new HashSet<IRValue>(ReferenceEqualityComparer.Instance);
                var stack = new Stack<IRValue>();
                foreach (var b in f.Blocks)
                {
                    foreach (var inst in b.Instructions)
                    {
                        if (inst == null) continue;
                        if (inst is IRValue self && seen.Add(self)) yield return self;
                        foreach (var u in OptimizationPass.UsesOf(inst)) stack.Push(u);
                        while (stack.Count > 0)
                        {
                            var v = stack.Pop();
                            if (v == null || !seen.Add(v)) continue;
                            yield return v;
                            if (v is IRInstruction nested && !(v is IRVariable))
                                foreach (var u in OptimizationPass.UsesOf(nested)) stack.Push(u);
                        }
                    }
                }
            }

            private List<IRFunction> LambdasReferencedBy(IRFunction f)
            {
                var result = new List<IRFunction>();
                if (f?.Blocks == null) return result;
                var seen = new HashSet<IRFunction>(ReferenceEqualityComparer.Instance);
                foreach (var v in ValuesIn(f))
                    if (v is IRVariable variable && IsLambdaReferenceName(variable.Name)
                        && _lambdas.TryGetValue(variable.Name, out var lambda) && !ReferenceEquals(lambda, f)
                        && seen.Add(lambda))
                        result.Add(lambda);
                return result;
            }

            private void RefuseLambdasOutsideFunctionBodies()
            {
                static bool IsLambdaValue(IRValue v) => v is IRVariable variable && IsLambdaReferenceName(variable.Name);

                foreach (var cls in _module.Classes.Values)
                {
                    if (cls == null) continue;
                    foreach (var field in cls.Fields)
                        if (IsLambdaValue(field?.Initializer))
                            throw new ForeignFeatureException(
                                $"MSIL: the lambda initialising field '{cls.Name}.{field.Name}' has no IL lowering. A "
                                + "field initializer runs outside every function body, so the lambda has no creator "
                                + "to hold its environment. Assign it in a constructor instead.");
                    foreach (var ctor in cls.Constructors)
                        if (ctor?.BaseConstructorArgs != null && ctor.BaseConstructorArgs.Any(IsLambdaValue))
                            throw new ForeignFeatureException(
                                $"MSIL: a lambda passed to MyBase.New in '{cls.Name}' has no IL lowering. It is "
                                + "invisible to the capture analysis (task #170), and IL requires the base "
                                + "constructor call before any environment could exist.");
                }
                foreach (var global in _module.GlobalVariables.Values)
                    if (IsLambdaValue(global?.InitialValue))
                        throw new ForeignFeatureException(
                            $"MSIL: the lambda initialising module variable '{global.Name}' has no IL lowering. A "
                            + "module initializer runs outside every function body, so the lambda has no creator "
                            + "to hold its environment. Assign it inside Sub Main (or another procedure) instead.");
            }

            // ---------------------------------------------------------------------------------
            // One function
            // ---------------------------------------------------------------------------------

            private void Process(FunctionContext ctx)
            {
                var g = ctx.Function;
                _lowered.Add(ctx);
                CollectDeclarations(ctx);
                ctx.InBlock = new HashSet<IRInstruction>(g.Blocks.SelectMany(b => b.Instructions).Where(i => i != null),
                    ReferenceEqualityComparer.Instance);
                ctx.TakenNames = TakenNames(g);

                if (_created.TryGetValue(g, out var created))
                    foreach (var lambda in created)
                    {
                        ctx.Lambdas.Add(lambda);
                        ctx.LambdaByName[lambda.Name] = lambda;
                    }

                if (!ctx.IsLambda && ctx.Root.Class != null && ctx.Root.IsInstance && ctx.Lambdas.Count > 0)
                    ctx.Root.AnyNeedsMe = TransitiveLambdas(g).Any(NeedsMeSelf);

                if (ctx.Lambdas.Count > 0) BuildEnvironments(ctx);

                LowerAddressOf(ctx);
                DetermineHosts(ctx);
                RewriteBody(ctx);
                InsertPrologues(ctx);

                foreach (var lambda in ctx.Lambdas)
                {
                    var host = ctx.Hosts[lambda];
                    host.Env.Methods.Add(new IRMethod
                    {
                        Name = lambda.Name,
                        ReturnType = IsVoid(lambda.ReturnType) ? new TypeInfo("Void", TypeKind.Void) : lambda.ReturnType,
                        Access = AccessModifier.Public,
                        IsStatic = false,
                        Parameters = lambda.Parameters,
                        Implementation = lambda,
                    });
                    lambda.IsLambda = false;

                    var lctx = new FunctionContext
                    {
                        Function = lambda,
                        IsLambda = true,
                        Host = host,
                        Root = ctx.Root,
                    };
                    for (var level = host; level != null; level = level.Parent) lctx.HostChain.Add(level);
                    Process(lctx);
                }
            }

            private void CollectDeclarations(FunctionContext ctx)
            {
                var g = ctx.Function;
                foreach (var p in g.Parameters) if (p?.Name != null) ctx.Params.TryAdd(p.Name, p);
                foreach (var l in g.LocalVariables) if (l?.Name != null) ctx.Locals.TryAdd(l.Name, l);
                foreach (var inst in g.Blocks.SelectMany(b => b.Instructions))
                {
                    if (inst is IRTryCatch tc)
                        foreach (var clause in tc.CatchClauses)
                        {
                            if (string.IsNullOrEmpty(clause?.VariableName) || ctx.Locals.ContainsKey(clause.VariableName)) continue;
                            var type = clause.ExceptionType ?? new TypeInfo("Exception", TypeKind.Class);
                            if (ctx.CatchVars.TryGetValue(clause.VariableName, out var earlier) && !TypesEqual(earlier, type))
                                ctx.CatchVars[clause.VariableName] = null;   // conflicting types: refused if captured
                            else ctx.CatchVars.TryAdd(clause.VariableName, type);
                        }
                    if (inst is IRForEach fe && fe.BodyBlock != null)
                        ctx.Loops.Add((fe, LoopRegion(fe)));
                }
            }

            /// <summary>
            /// The blocks a For Each's body runs: everything reachable from its body block without
            /// passing its continuation — the same walk <c>MSILCodeGenerator.EmitForEachBody</c>
            /// emits the body from, so the two agree on which blocks are "inside the loop".
            /// </summary>
            private static HashSet<BasicBlock> LoopRegion(IRForEach fe)
            {
                var region = new HashSet<BasicBlock>(ReferenceEqualityComparer.Instance);
                var stack = new Stack<BasicBlock>();
                stack.Push(fe.BodyBlock);
                while (stack.Count > 0)
                {
                    var b = stack.Pop();
                    if (b == null || ReferenceEquals(b, fe.EndBlock) || !region.Add(b)) continue;
                    foreach (var s in ControlFlowGraph.SuccessorsOf(b)) stack.Push(s);
                }
                return region;
            }

            private bool IsDeclaringLoop(FunctionContext ctx, IRForEach fe) =>
                !string.IsNullOrEmpty(fe.VariableName)
                && !ctx.Locals.ContainsKey(fe.VariableName) && !ctx.Params.ContainsKey(fe.VariableName);

            private HashSet<string> TakenNames(IRFunction g)
            {
                var taken = new HashSet<string>(_userTempNames, StringComparer.OrdinalIgnoreCase);
                foreach (var p in g.Parameters) if (p?.Name != null) taken.Add(p.Name);
                foreach (var l in g.LocalVariables) if (l?.Name != null) taken.Add(l.Name);
                foreach (var v in ValuesIn(g)) if (v?.Name != null) taken.Add(v.Name);
                return taken;
            }

            private string NewTemp(FunctionContext ctx)
            {
                string name;
                do { name = $"t{ctx.TempCounter++}"; } while (!ctx.TakenNames.Add(name));
                return name;
            }

            private string NewLocalName(FunctionContext ctx)
            {
                var k = 0;
                string name;
                do { name = $"__closure_env{k++}"; } while (!ctx.TakenNames.Add(name));
                return name;
            }

            private IEnumerable<IRFunction> TransitiveLambdas(IRFunction creator)
            {
                var seen = new HashSet<IRFunction>(ReferenceEqualityComparer.Instance);
                var stack = new Stack<IRFunction>();
                if (_created.TryGetValue(creator, out var direct)) foreach (var l in direct) stack.Push(l);
                while (stack.Count > 0)
                {
                    var l = stack.Pop();
                    if (!seen.Add(l)) continue;
                    yield return l;
                    if (_created.TryGetValue(l, out var nested)) foreach (var n in nested) stack.Push(n);
                }
            }

            private bool NeedsMeTransitive(IRFunction lambda) =>
                NeedsMeSelf(lambda) || TransitiveLambdas(lambda).Any(NeedsMeSelf);

            /// <summary>
            /// Whether a lambda's OWN body reaches its outermost creator's <c>Me</c>: <c>Me</c> or
            /// <c>MyBase</c> itself, or a bare instance member of the creator's class (a field, a
            /// property, a method, an <c>AddressOf</c> of a method). A superset — a member name the
            /// lambda's own declaration or a hoisted variable shadows still counts — which only
            /// costs a field.
            /// </summary>
            private bool NeedsMeSelf(IRFunction lambda)
            {
                if (!_creatorOf.ContainsKey(lambda)) return false;
                var root = RootOf(lambda);
                var cls = OwnerClassOf(_module, root, out var isInstance);
                if (cls == null || !isInstance) return false;

                bool OwnDecl(string name) =>
                    lambda.Parameters.Any(p => NameEquals(p?.Name, name))
                    || lambda.LocalVariables.Any(l => NameEquals(l?.Name, name));
                bool InstanceMember(string name) =>
                    name != null && !OwnDecl(name)
                    && ((TryFindField(_module, cls, name, out _, out var f) && !f.IsStatic)
                        || (TryFindProperty(_module, cls, name, out _, out var p) && !p.IsStatic)
                        || (TryFindMethod(_module, cls, name, out _, out var m) && !m.IsStatic));

                foreach (var v in ValuesIn(lambda))
                {
                    switch (v)
                    {
                        case IRVariable variable when IsMeName(variable.Name):
                            return true;
                        case IRVariable variable when InstanceMember(variable.Name):
                            return true;
                        case IRBaseMethodCall:
                            return true;
                        case IRCall call when call.CalleeValue == null && IsBareName(call.FunctionName)
                                              && InstanceMember(call.FunctionName):
                            return true;
                    }
                    if (v is IRValue named && OptimizationPass.NamedDestination(named) is string dest
                        && !(v is IRVariable) && InstanceMember(dest))
                        return true;
                }
                foreach (var inst in lambda.Blocks.SelectMany(b => b.Instructions))
                    if (inst is IRAssignment a && InstanceMember(a.Target?.Name)) return true;
                return false;
            }

            private IRFunction RootOf(IRFunction f)
            {
                var guard = 0;
                while (_creatorOf.TryGetValue(f, out var creator) && guard++ < 1000) f = creator;
                return f;
            }

            // ---------------------------------------------------------------------------------
            // Environments (D2 amended, D3, D4, D6, D9)
            // ---------------------------------------------------------------------------------

            private void BuildEnvironments(FunctionContext ctx)
            {
                var g = ctx.Function;

                if (g.IsIterator || g.IsAsync)
                    throw new ForeignFeatureException(
                        $"MSIL: '{g.Name}' is an {(g.IsIterator ? "Iterator" : "Async")} function that creates a "
                        + "lambda. The first cut of closure conversion does not decide how an environment "
                        + "interacts with a state-machine lowering (ADR-0010 D9), so this is refused rather "
                        + "than emitted.");

                var capSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var lambda in ctx.Lambdas)
                {
                    if (lambda.IsIterator || lambda.IsAsync)
                        throw new ForeignFeatureException(
                            $"MSIL: an {(lambda.IsIterator ? "Iterator" : "Async")} lambda ('{lambda.Name}' in "
                            + $"'{g.Name}') has no IL lowering in the first cut of closure conversion "
                            + "(ADR-0010 D9).");

                    var recomputed = OptimizationPass.LambdaCapturesOf(lambda);
                    if (recomputed == null || g.LambdaCaptureSources == null || !g.LambdaCaptureSources.Contains(lambda.Name))
                        throw new ForeignFeatureException(
                            $"MSIL: the capture set of lambda '{lambda.Name}' in '{g.Name}' could not be enumerated "
                            + "(it holds raw inline code, or a nested lambda whose captures were not recorded). "
                            + "Closure conversion hoists exactly the captured variables, so without the set it "
                            + "cannot know what to hoist; refused rather than guessed (ADR-0010 D3).");

                    var caps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var (name, _) in lambda.CapturedVariables) if (name != null) caps.Add(name);
                    foreach (var name in recomputed.Keys) caps.Add(name);

                    // A delegate VARIABLE invoked by name — `greet(s)` — is a read of that variable
                    // (D8's canonical form is a CalleeValue read of it), but the name-form call carries
                    // it as a FunctionName, which #122's set does not list. Every bare call name of the
                    // lambda and of the lambdas nested in it is added: a name that is not one of the
                    // creator's variables is dropped by the intersection with its declarations below,
                    // and one that is but is not a delegate is only over-hoisted (D3: neutral).
                    foreach (var body in new[] { lambda }.Concat(TransitiveLambdas(lambda)))
                        foreach (var v in ValuesIn(body))
                            if (v is IRCall { CalleeValue: null } bare && IsBareName(bare.FunctionName))
                                caps.Add(bare.FunctionName);

                    ctx.Captures[lambda] = caps;
                    capSet.UnionWith(caps);

                    // #169: a name spelled like a lambda parameter in another case binds to the
                    // creator in the IR, while VB binds it to the parameter.
                    foreach (var name in caps)
                        foreach (var p in lambda.Parameters)
                            if (p?.Name != null && !string.Equals(p.Name, name, StringComparison.Ordinal) && NameEquals(p.Name, name))
                                throw new ForeignFeatureException(
                                    $"MSIL: in lambda '{lambda.Name}', '{name}' differs from its parameter '{p.Name}' "
                                    + "only by case. VB binds it to the parameter; the IR binds it to a variable of "
                                    + "the creator (task #169). Closure conversion never re-resolves a name, so the "
                                    + "shape is refused until the front end binds it. Spell it like the parameter.");
                }

                // D4: a captured ByRef parameter.
                foreach (var p in g.Parameters)
                    if (p?.Name != null && p.IsByRef && capSet.Contains(p.Name))
                        throw new ForeignFeatureException(
                            $"MSIL: ByRef parameter '{p.Name}' of '{g.Name}' is captured by a lambda. VB forbids it "
                            + "(BC36639), and IL cannot keep a managed pointer in a field, so it is refused "
                            + "(ADR-0010 D4). Copy it into a local first.");

                var generics = new HashSet<string>(StringComparer.Ordinal);
                foreach (var gp in g.GenericParameters) generics.Add(gp);
                foreach (var gp in ctx.Root.Function.GenericParameters) generics.Add(gp);
                if (ctx.Root.Class != null) foreach (var gp in ctx.Root.Class.GenericParameters) generics.Add(gp);
                void RefuseGeneric(string name, TypeInfo type)
                {
                    if (type != null && (type.Kind == TypeKind.TypeParameter || generics.Contains(type.Name)))
                        throw new ForeignFeatureException(
                            $"MSIL: captured variable '{name}' of '{g.Name}' is typed by the generic parameter "
                            + $"'{type.Name}'. Generic environment classes are out of scope for the first cut of "
                            + "closure conversion (ADR-0010 D9).");
                }
                if (ctx.Root.Class != null && ctx.Root.Class.GenericParameters.Count > 0)
                    throw new ForeignFeatureException(
                        $"MSIL: '{g.Name}' creates a lambda inside the generic class '{ctx.Root.Class.Name}'. Its "
                        + "environment would be nested in a generic type and so would have to be generic too, "
                        + "which is out of scope for the first cut of closure conversion (ADR-0010 D9).");

                // The function environment.
                var fl = NewLevel(ctx, loop: null);
                ctx.FunctionEnv = fl;

                void Hoist(string name, TypeInfo type)
                {
                    RefuseGeneric(name, type);
                    if (type?.Kind == TypeKind.Array && type.ArrayDimensionSizes.Count > 1)
                        throw new ForeignFeatureException(
                            $"MSIL: captured variable '{name}' of '{g.Name}' is a {type.ArrayDimensionSizes.Count}-dimensional "
                            + "array, which this backend has no IL lowering for at all.");
                    fl.Vars[name] = (name, type ?? new TypeInfo("Object", TypeKind.Class));
                }

                foreach (var p in g.Parameters)
                    if (p?.Name != null && !p.IsByRef && capSet.Contains(p.Name)) Hoist(p.Name, p.Type);
                foreach (var l in g.LocalVariables)
                    if (l?.Name != null && capSet.Contains(l.Name) && !fl.Holds(l.Name)) Hoist(l.Name, l.Type);
                foreach (var (name, type) in ctx.CatchVars)
                {
                    if (!capSet.Contains(name) || fl.Holds(name)) continue;
                    if (type == null)
                        throw new ForeignFeatureException(
                            $"MSIL: Catch variable '{name}' of '{g.Name}' is captured by a lambda and declared by "
                            + "more than one Catch with different exception types. It is function-level in the "
                            + "closure environment (ADR-0010 D2), which needs one type; rename one of them.");
                    Hoist(name, type);
                }

                if (ctx.IsLambda)
                {
                    fl.Parent = ctx.Host;
                    fl.ParentField = FreshField(fl, "__parent");
                    AddField(fl.Env, fl.ParentField, ctx.Host.EnvType);
                }

                if (!ctx.IsLambda && ctx.Root.AnyNeedsMe)
                {
                    if (ctx.Root.Class.IsStruct)
                        throw new ForeignFeatureException(
                            $"MSIL: a lambda in '{ctx.Root.Class.Name}.{g.Name}' uses Me, and "
                            + $"'{ctx.Root.Class.Name}' is a Structure. VB forbids it (BC36638) — the lambda would "
                            + "capture a copy of the value — so it is refused (ADR-0010 D6).");
                    fl.MeField = FreshField(fl, "__me");
                    fl.MeType = new TypeInfo(ctx.Root.Class.Name, TypeKind.Class);
                    AddField(fl.Env, fl.MeField, fl.MeType);
                    ctx.Root.MeHolder = fl;
                }

                foreach (var (name, (field, type)) in fl.Vars) AddField(fl.Env, field, type);

                // Iteration environments: one per captured declaring For Each, chained to the
                // next enclosing captured loop, else to the function environment.
                var captured = ctx.Loops.Where(l => IsDeclaringLoop(ctx, l.Loop) && capSet.Contains(l.Loop.VariableName)).ToList();
                foreach (var (loop, region) in captured)
                {
                    RefuseGeneric(loop.VariableName, loop.ElementType);
                    var level = NewLevel(ctx, loop);
                    level.Region = region;
                    level.Vars[loop.VariableName] = (loop.VariableName, loop.ElementType ?? new TypeInfo("Object", TypeKind.Class));
                    ctx.LoopLevels.Add(level);
                }
                foreach (var level in ctx.LoopLevels)
                {
                    var head = level.Loop.ParentBlock;
                    level.Parent = ctx.LoopLevels
                        .Where(o => !ReferenceEquals(o, level) && head != null && o.Region.Contains(head))
                        .OrderBy(o => o.Region.Count).FirstOrDefault() ?? fl;
                    level.ParentField = FreshField(level, "__parent");
                    AddField(level.Env, level.ParentField, level.Parent.EnvType);
                    foreach (var (_, (field, type)) in level.Vars) AddField(level.Env, field, type);
                }
            }

            private Level NewLevel(FunctionContext ctx, IRForEach loop)
            {
                var name = $"{EnvironmentPrefix}{_envCounter++}";
                var env = new IRClass(name)
                {
                    EnclosingClass = ctx.Root.Class?.Name,
                    Namespace = ctx.Root.Class?.Namespace,
                };
                _module.Classes[name] = env;
                var type = new TypeInfo(name, TypeKind.Class);
                var local = new IRVariable(NewLocalName(ctx), type);
                _synthetic.Add(local);
                return new Level { Env = env, EnvType = type, Owner = ctx.Function, Loop = loop, LocalRef = local };
            }

            private static string FreshField(Level level, string wanted)
            {
                var name = wanted;
                for (var k = 1; level.Vars.ContainsKey(name) || NameEquals(name, level.MeField) || NameEquals(name, level.ParentField); k++)
                    name = wanted + k;
                return name;
            }

            private static void AddField(IRClass env, string name, TypeInfo type) =>
                env.Fields.Add(new IRField { Name = name, Type = type, Access = AccessModifier.Public, IsStatic = false });

            // ---------------------------------------------------------------------------------
            // Scope and binding
            // ---------------------------------------------------------------------------------

            /// <summary>The environments in scope at <paramref name="block"/>, innermost first.</summary>
            private static List<Level> ChainAt(FunctionContext ctx, BasicBlock block)
            {
                var chain = ctx.LoopLevels.Where(l => l.Region.Contains(block)).OrderBy(l => l.Region.Count).ToList();
                if (ctx.FunctionEnv != null) chain.Add(ctx.FunctionEnv);
                chain.AddRange(ctx.HostChain);
                return chain;
            }

            /// <summary>
            /// What <paramref name="name"/> denotes at <paramref name="block"/> of the function being
            /// lowered: its own declaration first (hoisted or not), then an enclosing environment,
            /// then — in a lambda — a member of the outermost creator's class, which the lambda
            /// used to reach from that class's context and now reaches through the captured Me.
            /// </summary>
            private Binding Resolve(FunctionContext ctx, BasicBlock block, string name)
            {
                if (string.IsNullOrEmpty(name)) return Binding.None;

                if (IsMeName(name))
                    return ctx.IsLambda && ctx.Root.IsInstance && ctx.Root.Class != null
                        ? new Binding(BindingKind.Me) : Binding.None;

                // A For Each variable, inside its own body.
                var loop = ctx.Loops.Where(l => NameEquals(l.Loop.VariableName, name) && l.Region.Contains(block))
                    .OrderBy(l => l.Region.Count).Select(l => l.Loop).FirstOrDefault();
                if (loop != null && IsDeclaringLoop(ctx, loop))
                {
                    var level = ctx.LoopLevels.FirstOrDefault(l => ReferenceEquals(l.Loop, loop));
                    return level != null ? EnvBinding(level, name) : Binding.None;
                }

                if (ctx.Declares(name))
                    return ctx.FunctionEnv != null && ctx.FunctionEnv.Holds(name)
                        ? EnvBinding(ctx.FunctionEnv, name) : Binding.None;

                if (!ctx.IsLambda) return Binding.None;

                foreach (var level in ctx.HostChain)
                    if (level.Holds(name)) return EnvBinding(level, name);

                var cls = ctx.Root.Class;
                if (cls != null)
                {
                    if (TryFindField(_module, cls, name, out var fieldOwner, out var field))
                    {
                        if (field.IsStatic) return new Binding(BindingKind.StaticMember, field: field.Name, type: field.Type, declaring: fieldOwner);
                        if (ctx.Root.IsInstance) return new Binding(BindingKind.InstanceMember, field: field.Name, type: field.Type, declaring: fieldOwner);
                    }
                    else if (TryFindProperty(_module, cls, name, out var propOwner, out var prop))
                    {
                        var storage = !prop.IsAccessorBacked;
                        if (prop.IsStatic) return new Binding(BindingKind.StaticMember, field: prop.Name, type: prop.Type, declaring: propOwner, storage: storage);
                        if (ctx.Root.IsInstance) return new Binding(BindingKind.InstanceMember, field: prop.Name, type: prop.Type, declaring: propOwner, storage: storage);
                    }
                }
                return Binding.None;
            }

            private static Binding EnvBinding(Level level, string name)
            {
                var (field, type) = level.Vars[name];
                return new Binding(BindingKind.Env, level, field, type);
            }

            /// <summary>The declared type of a VARIABLE <paramref name="name"/> in scope at
            /// <paramref name="block"/>, or null when the name is not a variable here.</summary>
            private TypeInfo VariableType(FunctionContext ctx, BasicBlock block, string name)
            {
                var loop = ctx.Loops.Where(l => NameEquals(l.Loop.VariableName, name) && l.Region.Contains(block))
                    .OrderBy(l => l.Region.Count).Select(l => l.Loop).FirstOrDefault();
                if (loop != null) return loop.ElementType;
                if (ctx.Params.TryGetValue(name, out var p)) return p.Type;
                if (ctx.Locals.TryGetValue(name, out var l)) return l.Type;
                if (ctx.CatchVars.TryGetValue(name, out var c)) return c;
                if (ctx.IsLambda)
                    foreach (var level in ctx.HostChain)
                        if (level.Vars.TryGetValue(name, out var v)) return v.Type;
                if (ctx.Root.Class != null && TryFindField(_module, ctx.Root.Class, name, out _, out var field)
                    && (field.IsStatic || ctx.Root.IsInstance))
                    return field.Type;
                foreach (var global in _module.GlobalVariables.Values)
                    if (NameEquals(global?.Name, name)) return global.Type;
                return null;
            }

            // ---------------------------------------------------------------------------------
            // Emission helpers
            // ---------------------------------------------------------------------------------

            private IRVariable MeRef(FunctionContext ctx)
            {
                if (ctx.MeRef == null)
                {
                    ctx.MeRef = new IRVariable("Me", ctx.Host.EnvType);
                    _synthetic.Add(ctx.MeRef);
                }
                return ctx.MeRef;
            }

            private IRFieldAccess Load(FunctionContext ctx, IRValue obj, string field, TypeInfo type, bool storage,
                List<IRInstruction> into, int sourceLine)
            {
                var load = new IRFieldAccess(NewTemp(ctx), obj, field, type) { IsStorageAccess = storage, SourceLine = sourceLine };
                into.Add(load);
                return load;
            }

            private static IRFieldStore Store(IRValue obj, string field, IRValue value, bool storage, int sourceLine) =>
                new(obj, field, value) { IsStorageAccess = storage, SourceLine = sourceLine };

            /// <summary>The environment object <paramref name="level"/> denotes, from the function
            /// being lowered: its own local, or its <c>Me</c> followed up the parent chain.</summary>
            private IRValue EnvRef(FunctionContext ctx, Level level, List<IRInstruction> into, int sourceLine)
            {
                if (ReferenceEquals(level.Owner, ctx.Function)) return level.LocalRef;

                var index = ctx.HostChain.IndexOf(level);
                if (index < 0)
                    throw new InvalidOperationException(
                        $"ClosureLowering: environment {level.Env.Name} is not in scope in '{ctx.Function.Name}'.");

                IRValue current = MeRef(ctx);
                for (var i = 0; i < index; i++)
                {
                    var step = ctx.HostChain[i];
                    current = Load(ctx, current, step.ParentField, step.Parent.EnvType, true, into, sourceLine);
                }
                return current;
            }

            private IRValue MeValue(FunctionContext ctx, List<IRInstruction> into, int sourceLine)
            {
                var holder = ctx.Root.MeHolder
                    ?? throw new InvalidOperationException(
                        $"ClosureLowering: '{ctx.Function.Name}' reaches Me, but no environment captured it — "
                        + "NeedsMeSelf under-approximated.");
                var env = EnvRef(ctx, holder, into, sourceLine);
                return Load(ctx, env, holder.MeField, holder.MeType, true, into, sourceLine);
            }

            private IRValue ReceiverFor(FunctionContext ctx, Binding binding, List<IRInstruction> into, int sourceLine) =>
                binding.Kind switch
                {
                    BindingKind.Env => EnvRef(ctx, binding.Level, into, sourceLine),
                    BindingKind.InstanceMember => MeValue(ctx, into, sourceLine),
                    BindingKind.StaticMember => new IRVariable(binding.Declaring.Name, new TypeInfo(binding.Declaring.Name, TypeKind.Class)),
                    _ => throw new InvalidOperationException("ClosureLowering: no receiver for " + binding.Kind),
                };

            // ---------------------------------------------------------------------------------
            // Hosts (D5)
            // ---------------------------------------------------------------------------------

            private void DetermineHosts(FunctionContext ctx)
            {
                if (ctx.Lambdas.Count == 0) return;

                foreach (var lambda in ctx.Lambdas)
                {
                    var caps = ctx.Captures[lambda];
                    var needsMe = ctx.Root.MeHolder != null && NeedsMeTransitive(lambda);
                    var lambdaDecls = LambdaDeclarations(lambda);
                    Level host = null;
                    var sites = 0;

                    foreach (var block in ctx.Function.Blocks)
                    {
                        if (!block.Instructions.Any(i => i != null && ReferencesLambda(ctx, i, lambda.Name))) continue;
                        sites++;
                        var chain = ChainAt(ctx, block);

                        // N9: the lambda declares a name an enclosing scope hoists, so the IR cannot say
                        // which of the two each of its mentions means.
                        foreach (var declared in lambdaDecls)
                            if (chain.Any(level => level.Holds(declared)))
                                throw new ForeignFeatureException(
                                    $"MSIL: lambda '{lambda.Name}' in '{ctx.Function.Name}' declares its own '{declared}' "
                                    + $"while an enclosing scope's '{declared}' is captured. The IR binds a mention "
                                    + "before the declaration to the enclosing variable (task #122 N9; VB refuses the "
                                    + "shape, BC30616), and closure conversion never re-resolves a name, so it is "
                                    + "refused. Rename one of them.");

                        var chosen = chain.FirstOrDefault(level =>
                                         level.Vars.Keys.Any(caps.Contains)
                                         || (needsMe && level.MeField != null))
                                     ?? ctx.FunctionEnv;
                        if (host != null && !ReferenceEquals(host, chosen))
                            throw new InvalidOperationException(
                                $"ClosureLowering: lambda '{lambda.Name}' is created at two sites that disagree on its "
                                + "environment.");
                        host = chosen;
                    }

                    ctx.Hosts[lambda] = host ?? ctx.FunctionEnv;
                }
            }

            private static HashSet<string> LambdaDeclarations(IRFunction lambda)
            {
                var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var l in lambda.LocalVariables) if (l?.Name != null) declared.Add(l.Name);
                foreach (var inst in lambda.Blocks.SelectMany(b => b.Instructions))
                {
                    if (inst is IRForEach fe && !string.IsNullOrEmpty(fe.VariableName)
                        && !lambda.Parameters.Any(p => NameEquals(p?.Name, fe.VariableName)))
                        declared.Add(fe.VariableName);
                    if (inst is IRTryCatch tc)
                        foreach (var c in tc.CatchClauses)
                            if (!string.IsNullOrEmpty(c?.VariableName)) declared.Add(c.VariableName);
                }
                return declared;
            }

            /// <summary>Whether <paramref name="inst"/> uses the lambda value — directly, or inside an
            /// operand tree that lives in no block (a When guard).</summary>
            private static bool ReferencesLambda(FunctionContext ctx, IRInstruction inst, string lambdaName)
            {
                var seen = new HashSet<IRValue>(ReferenceEqualityComparer.Instance);
                var stack = new Stack<IRValue>(OptimizationPass.UsesOf(inst));
                while (stack.Count > 0)
                {
                    var v = stack.Pop();
                    if (v == null || !seen.Add(v)) continue;
                    if (v is IRVariable variable)
                    {
                        if (string.Equals(variable.Name, lambdaName, StringComparison.Ordinal)) return true;
                        continue;
                    }
                    if (v is IRInstruction nested && !ctx.InBlock.Contains(nested))
                        foreach (var u in OptimizationPass.UsesOf(nested)) stack.Push(u);
                }
                return false;
            }

            // ---------------------------------------------------------------------------------
            // Delegates (D7, D8)
            // ---------------------------------------------------------------------------------

            /// <summary>Refuses unless <paramref name="method"/> is EXACTLY <paramref name="delegateType"/>'s
            /// Invoke, after generic substitution (D7: no VB relaxed delegate conversion).</summary>
            private void CheckSignature(TypeInfo delegateType, IReadOnlyList<IRVariable> parameters, TypeInfo returnType,
                string what, string where)
            {
                if (!TryInvokeSignature(_module, delegateType, out var invokeParams, out var invokeReturn, out var problem))
                    throw new ForeignFeatureException(
                        $"MSIL: {what} in '{where}' is bound to '{Show(delegateType)}', and {problem}. A lambda or "
                        + "AddressOf is target-typed to the delegate of the slot it fills (ADR-0010 D7), so that "
                        + "slot must be an Action, a Func or a Delegate this program declares.");

                string Relax(string detail) =>
                    $"MSIL: {what} in '{where}' does not match '{Show(delegateType)}' exactly ({detail}). The first "
                    + "cut of closure conversion has no VB relaxed delegate conversion (ADR-0010 D7): the "
                    + "signature must match after generic substitution. Declare the lambda's parameter and "
                    + "return types to match the delegate.";

                if (parameters.Count != invokeParams.Count)
                    throw new ForeignFeatureException(Relax($"{parameters.Count} parameter(s) for {invokeParams.Count}"));
                for (var i = 0; i < parameters.Count; i++)
                {
                    if (parameters[i].IsByRef)
                        throw new ForeignFeatureException(Relax($"parameter '{parameters[i].Name}' is ByRef"));
                    if (!TypesEqual(parameters[i].Type, invokeParams[i]))
                        throw new ForeignFeatureException(Relax(
                            $"parameter '{parameters[i].Name}' is {Show(parameters[i].Type)}, the delegate's is {Show(invokeParams[i])}"));
                }
                if (!TypesEqual(IsVoid(returnType) ? null : returnType, invokeReturn))
                    throw new ForeignFeatureException(Relax(
                        $"it returns {Show(returnType)}, the delegate returns {Show(invokeReturn)}"));
            }

            /// <summary>
            /// The slots of <paramref name="consumer"/> a value flows into, with the DECLARED type of
            /// each where one is known — the target a lambda or AddressOf is typed to (D7), and what a
            /// delegate value must already be (no delegate-to-delegate conversion).
            /// </summary>
            private IEnumerable<(IRValue Value, TypeInfo Slot)> Slots(FunctionContext ctx, BasicBlock block, IRInstruction consumer)
            {
                switch (consumer)
                {
                    case IRAssignment a:
                        yield return (a.Value, VariableType(ctx, block, a.Target?.Name) ?? a.Target?.Type);
                        break;
                    case IRStore s when s.Address is IRVariable address:
                        yield return (s.Value, VariableType(ctx, block, address.Name) ?? address.Type);
                        break;
                    case IRFieldStore fs:
                        yield return (fs.Value, MemberType(fs.Object?.Type, fs.FieldName));
                        break;
                    case IRReturn r:
                        yield return (r.Value, ctx.Function.ReturnType);
                        break;
                    case IRArrayStore ast:
                        yield return (ast.Value, ast.Array?.Type?.ElementType);
                        break;
                    case IRIndexerStore ist:
                        yield return (ist.Value, ist.Collection?.Type?.GenericArguments?.LastOrDefault());
                        break;
                    case IRCall call:
                        for (var i = 0; i < call.Arguments.Count; i++)
                            yield return (call.Arguments[i], CallParameterType(ctx, block, call, i));
                        break;
                    case IRInstanceMethodCall mc:
                        for (var i = 0; i < mc.Arguments.Count; i++)
                            yield return (mc.Arguments[i], InstanceParameterType(mc, i));
                        break;
                    case IRNewObject no:
                        for (var i = 0; i < no.Arguments.Count; i++)
                            yield return (no.Arguments[i], ConstructorParameterType(no, i));
                        break;
                }
            }

            private TypeInfo MemberType(TypeInfo receiver, string member)
            {
                if (!TryFindClass(_module, receiver?.Name, out var cls)) return null;
                if (TryFindField(_module, cls, member, out _, out var f)) return f.Type;
                if (TryFindProperty(_module, cls, member, out _, out var p)) return p.Type;
                return null;
            }

            private TypeInfo CallParameterType(FunctionContext ctx, BasicBlock block, IRCall call, int index)
            {
                if (call.CalleeValue != null)
                    return TryInvokeSignature(_module, call.CalleeValue.Type, out var ps, out _, out _) && index < ps.Count ? ps[index] : null;

                var name = call.FunctionName;
                if (string.IsNullOrEmpty(name)) return null;

                if (IsBareName(name))
                {
                    var asVariable = VariableType(ctx, block, name);
                    if (asVariable != null)
                        return TryInvokeSignature(_module, asVariable, out var ps, out _, out _) && index < ps.Count ? ps[index] : null;
                    if (ctx.Root.Class != null && TryFindMethod(_module, ctx.Root.Class, name, out _, out var own))
                        return ParameterType(own.Implementation?.Parameters, index);
                    var fn = ModuleFunction(name);
                    return fn != null ? ParameterType(fn.Parameters, index) : null;
                }

                var dot = name.LastIndexOf('.');
                if (dot > 0 && TryFindClass(_module, name.Substring(0, dot), out var owner)
                    && TryFindMethod(_module, owner, name.Substring(dot + 1), out _, out var method))
                    return ParameterType(method.Implementation?.Parameters, index);
                return null;
            }

            private TypeInfo InstanceParameterType(IRInstanceMethodCall mc, int index)
            {
                var receiver = mc.Object?.Type;
                if (receiver?.Name == null) return null;

                if (TryFindClass(_module, receiver.Name, out var cls)
                    && TryFindMethod(_module, cls, mc.MethodName, out _, out var method))
                    return ParameterType(method.Implementation?.Parameters, index);

                if (_module.Interfaces.TryGetValue(receiver.Name, out var iface))
                {
                    var im = iface.Methods.FirstOrDefault(m => NameEquals(m?.Name, mc.MethodName));
                    return im != null && index < im.Parameters.Count ? im.Parameters[index].Type : null;
                }

                // The BCL collections: the element-typed parameter of the members that take one.
                var args = receiver.GenericArguments;
                if (args == null || args.Count == 0) return null;
                var collection = StripSystem(receiver.Name);
                if (args.Count == 1 && (NameEquals(collection, "List") || NameEquals(collection, "HashSet")
                        || NameEquals(collection, "Queue") || NameEquals(collection, "Stack")))
                {
                    var m = mc.MethodName;
                    if (index == 0 && (NameEquals(m, "Add") || NameEquals(m, "Contains") || NameEquals(m, "Remove")
                            || NameEquals(m, "Enqueue") || NameEquals(m, "Push") || NameEquals(m, "IndexOf")))
                        return args[0];
                    if (index == 1 && NameEquals(m, "Insert")) return args[0];
                    if (index == 1 && NameEquals(m, "set_Item")) return args[0];
                    return null;
                }
                if (args.Count == 2 && NameEquals(collection, "Dictionary"))
                {
                    var m = mc.MethodName;
                    if (NameEquals(m, "Add") || NameEquals(m, "set_Item")) return index == 0 ? args[0] : args[1];
                    if (NameEquals(m, "ContainsKey") && index == 0) return args[0];
                    if (NameEquals(m, "ContainsValue") && index == 0) return args[1];
                }
                return null;
            }

            private TypeInfo ConstructorParameterType(IRNewObject no, int index)
            {
                if (!TryFindClass(_module, no.ClassName, out var cls)) return null;
                var ctor = cls.Constructors.FirstOrDefault(c => c?.Implementation?.Parameters != null
                                                              && c.Implementation.Parameters.Count == no.Arguments.Count);
                return ParameterType(ctor?.Implementation?.Parameters, index);
            }

            private static TypeInfo ParameterType(IReadOnlyList<IRVariable> parameters, int index) =>
                parameters != null && index < parameters.Count ? parameters[index]?.Type : null;

            private IRFunction ModuleFunction(string name) =>
                _module.Functions.FirstOrDefault(f => f != null && !f.IsLambda && !f.IsExternal
                                                     && !_classMembers.Contains(f) && NameEquals(f.Name, name));

            private bool IsDelegateValue(IRValue value) =>
                value != null && !(value is IRConstant) && IsDelegateType(_module, value.Type);

            // ---------------------------------------------------------------------------------
            // AddressOf (D8)
            // ---------------------------------------------------------------------------------

            private void LowerAddressOf(FunctionContext ctx)
            {
                var g = ctx.Function;
                var replaced = new Dictionary<IRValue, IRValue>(ReferenceEqualityComparer.Instance);
                var phantoms = new List<(BasicBlock Block, IRFieldAccess Access)>();

                foreach (var block in g.Blocks)
                {
                    for (var i = 0; i < block.Instructions.Count; i++)
                    {
                        if (!(block.Instructions[i] is IRUnaryOp { Operation: UnaryOpKind.AddressOf } u)) continue;

                        IRFunction method;
                        IRValue target = null;
                        TypeInfo methodReturn;
                        var isVirtual = false;
                        string shape;

                        switch (u.Operand)
                        {
                            case IRVariable m when ModuleFunction(m.Name) is IRFunction fn:
                                method = fn;
                                methodReturn = fn.ReturnType;
                                shape = m.Name;
                                break;

                            case IRVariable m when ctx.Root.Class != null
                                                   && TryFindMethod(_module, ctx.Root.Class, m.Name, out _, out var own)
                                                   && own.Implementation != null:
                                method = own.Implementation;
                                methodReturn = own.ReturnType;
                                shape = m.Name;
                                if (!own.IsStatic)
                                {
                                    if (!ctx.Root.IsInstance)
                                        throw new ForeignFeatureException(
                                            $"MSIL: 'AddressOf {m.Name}' in '{g.Name}' names an instance method from a "
                                            + "Shared context, where there is no Me to bind it to.");
                                    target = new IRVariable("Me", new TypeInfo(ctx.Root.Class.Name, TypeKind.Class));
                                    isVirtual = own.IsVirtual || own.IsOverride || own.IsAbstract;
                                }
                                break;

                            case IRFieldAccess fa when TryFindClass(_module, fa.Object?.Type?.Name, out var receiverClass)
                                                      && TryFindMethod(_module, receiverClass, fa.FieldName, out _, out var viaObject)
                                                      && viaObject.Implementation != null:
                                method = viaObject.Implementation;
                                methodReturn = viaObject.ReturnType;
                                shape = $"{fa.Object?.Name}.{fa.FieldName}";
                                if (!viaObject.IsStatic)
                                {
                                    target = fa.Object;
                                    isVirtual = viaObject.IsVirtual || viaObject.IsOverride || viaObject.IsAbstract;
                                }
                                // The member "read" IRBuilder emitted for `obj.M` is not a read.
                                if (ctx.InBlock.Contains(fa))
                                {
                                    var other = g.Blocks.SelectMany(b => b.Instructions)
                                        .Any(x => x != null && !ReferenceEquals(x, u) && OptimizationPass.UsesOf(x).Any(v => ReferenceEquals(v, fa)));
                                    if (other)
                                        throw new ForeignFeatureException(
                                            $"MSIL: 'AddressOf {shape}' in '{g.Name}' has a method reference that is also "
                                            + "used as a value; that shape has no IL lowering.");
                                    phantoms.Add((g.Blocks.First(b => b.Instructions.Contains(fa)), fa));
                                }
                                break;

                            default:
                                throw new ForeignFeatureException(
                                    $"MSIL: 'AddressOf' of {(u.Operand == null ? "nothing" : $"'{u.Operand.Name}' ({u.Operand.GetType().Name})")} "
                                    + $"in '{g.Name}' has no IL lowering. Supported: a module procedure, a method of the "
                                    + "enclosing class, and obj.Method on an object of a class this program declares.");
                        }

                        var slot = OptimizationPass.NamedDestination(u) is string dest
                            ? VariableType(ctx, block, dest)
                            : ConsumerSlotType(ctx, u);
                        var delegateType = slot ?? u.Type;
                        CheckSignature(delegateType, method.Parameters, methodReturn, $"'AddressOf {shape}'", g.Name);

                        var create = new IRDelegateCreate(u.Name, delegateType, target, method, isVirtual)
                        {
                            NamedAfterVariable = u.NamedAfterVariable,
                            SourceLine = u.SourceLine,
                            ParentBlock = block,
                        };
                        if (target is IRVariable me && IsMeName(me.Name) && !ctx.IsLambda) _synthetic.Add(me);
                        block.Instructions[i] = create;
                        ctx.InBlock.Remove(u);
                        ctx.InBlock.Add(create);
                        replaced[u] = create;
                    }
                }

                foreach (var (block, access) in phantoms)
                {
                    block.Instructions.Remove(access);
                    ctx.InBlock.Remove(access);
                }

                if (replaced.Count > 0)
                    foreach (var inst in g.Blocks.SelectMany(b => b.Instructions))
                        if (inst != null)
                            OptimizationPass.MapOperands(inst, v => v != null && replaced.TryGetValue(v, out var r) ? r : v);
            }

            private TypeInfo ConsumerSlotType(FunctionContext ctx, IRValue value)
            {
                foreach (var block in ctx.Function.Blocks)
                    foreach (var inst in block.Instructions)
                    {
                        if (inst == null) continue;
                        foreach (var (v, slot) in Slots(ctx, block, inst))
                            if (ReferenceEquals(v, value)) return slot;
                    }
                return null;
            }

            // ---------------------------------------------------------------------------------
            // The rewrite
            // ---------------------------------------------------------------------------------

            private void RewriteBody(FunctionContext ctx)
            {
                var g = ctx.Function;
                var replacedCalls = new Dictionary<IRValue, IRValue>(ReferenceEqualityComparer.Instance);

                foreach (var block in g.Blocks)
                {
                    var output = new List<IRInstruction>(block.Instructions.Count);
                    foreach (var original in block.Instructions)
                    {
                        if (original == null) { output.Add(null); continue; }
                        var pre = new List<IRInstruction>();
                        var post = new List<IRInstruction>();
                        var inst = original;
                        var line = original.SourceLine;

                        RefuseUnsupported(ctx, block, inst);

                        // Delegate-to-delegate conversion (D7), judged on the slots as written.
                        foreach (var (value, slot) in Slots(ctx, block, inst))
                        {
                            if (value is IRVariable v && IsLambdaReferenceName(v.Name)) continue;
                            if (value is IRDelegateCreate) continue;
                            if (slot == null || !IsDelegateType(_module, slot) || !IsDelegateValue(value)) continue;
                            if (!TypesEqual(slot, value.Type))
                                throw new ForeignFeatureException(
                                    $"MSIL: in '{g.Name}', a '{Show(value.Type)}' value flows into a '{Show(slot)}' slot. "
                                    + "Converting a delegate value between delegate types needs VB's relaxation, which "
                                    + "the first cut of closure conversion does not have (ADR-0010 D7); VB itself "
                                    + "requires 'AddressOf value.Invoke'.");
                        }

                        // A bare call: a delegate VARIABLE shadows a method (D8); in a lambda, a method
                        // of the creator's class is reached through the captured Me.
                        if (inst is IRCall call && call.CalleeValue == null && IsBareName(call.FunctionName))
                        {
                            var varType = VariableType(ctx, block, call.FunctionName);
                            if (varType != null && IsDelegateType(_module, varType))
                            {
                                call.CalleeValue = new IRVariable(call.FunctionName, varType);
                            }
                            else if (varType == null && ctx.IsLambda && ctx.Root.Class != null
                                     && TryClassMethod(ctx, call.FunctionName, out var declaring, out var method))
                            {
                                if (method.IsStatic)
                                {
                                    call.FunctionName = $"{declaring.Name}.{method.Name}";
                                }
                                else
                                {
                                    var viaMe = new IRInstanceMethodCall(call.Name,
                                        new IRVariable("Me", new TypeInfo(ctx.Root.Class.Name, TypeKind.Class)), method.Name, call.Type)
                                    {
                                        NamedAfterVariable = call.NamedAfterVariable,
                                        SourceLine = call.SourceLine,
                                        ParentBlock = block,
                                    };
                                    viaMe.Arguments.AddRange(call.Arguments);
                                    viaMe.ByRefArguments.AddRange(call.ByRefArguments);
                                    viaMe.GenericArguments.AddRange(call.GenericArguments);
                                    replacedCalls[call] = viaMe;
                                    inst = viaMe;
                                }
                            }
                        }

                        // Operands. A store's VARIABLE address is its destination, not a read, and
                        // MapUses lists it as an operand — so it is set aside here and judged below.
                        var consumer = inst;
                        if (consumer is IRStore { Address: IRVariable } variableStore)
                            variableStore.Value = RewriteOperand(ctx, block, consumer, variableStore.Value, pre, line);
                        else
                            OptimizationPass.MapOperands(consumer, v => RewriteOperand(ctx, block, consumer, v, pre, line));

                        // Destinations.
                        switch (inst)
                        {
                            case IRAssignment assignment:
                            {
                                var b = Resolve(ctx, block, assignment.Target?.Name);
                                if (IsStorageBinding(b))
                                    inst = Store(ReceiverFor(ctx, b, pre, line), b.Field, assignment.Value, b.Storage, line);
                                break;
                            }
                            case IRStore store when store.Address is IRVariable address:
                            {
                                var b = Resolve(ctx, block, address.Name);
                                if (IsStorageBinding(b))
                                    inst = Store(ReceiverFor(ctx, b, pre, line), b.Field, store.Value, b.Storage, line);
                                break;
                            }
                            case IRValue value when !(value is IRVariable) && !(value is IRConstant) && !(value is IRAlloca)
                                                    && OptimizationPass.NamedDestination(value) is string destination:
                            {
                                var b = Resolve(ctx, block, destination);
                                if (IsStorageBinding(b))
                                {
                                    // The value keeps its identity for its other consumers; only the
                                    // write to the variable moves into the environment.
                                    value.Name = NewTemp(ctx);
                                    value.NamedAfterVariable = false;
                                    post.Add(Store(ReceiverFor(ctx, b, post, line), b.Field, value, b.Storage, line));
                                }
                                break;
                            }
                        }

                        inst.ParentBlock = block;
                        foreach (var p in pre) p.ParentBlock = block;
                        foreach (var p in post) p.ParentBlock = block;
                        output.AddRange(pre);
                        output.Add(inst);
                        output.AddRange(post);
                    }
                    block.Instructions = output;
                }

                if (replacedCalls.Count > 0)
                    foreach (var inst in g.Blocks.SelectMany(b => b.Instructions))
                        if (inst != null)
                            OptimizationPass.MapOperands(inst, v => v != null && replacedCalls.TryGetValue(v, out var r) ? r : v);
            }

            /// <summary>A method of the outermost creator's class (or a base) that a bare call in a
            /// lambda names — the class scope, nearer than a module procedure of the same name.</summary>
            private bool TryClassMethod(FunctionContext ctx, string name, out IRClass declaring, out IRMethod method) =>
                TryFindMethod(_module, ctx.Root.Class, name, out declaring, out method) && method.Implementation != null;

            private static bool IsStorageBinding(Binding b) =>
                b.Kind == BindingKind.Env || b.Kind == BindingKind.InstanceMember || b.Kind == BindingKind.StaticMember;

            private void RefuseUnsupported(FunctionContext ctx, BasicBlock block, IRInstruction inst)
            {
                var g = ctx.Function;
                if (ctx.IsLambda && inst is IRBaseMethodCall baseCall)
                    throw new ForeignFeatureException(
                        $"MSIL: 'MyBase.{baseCall.MethodName}' inside a lambda has no IL lowering. The lambda runs as a "
                        + "method of its closure environment, which has no base to call non-virtually.");

                // A captured variable passed ByRef: the write-back would land in a temporary.
                void CheckByRef(IReadOnlyList<IRValue> args, Func<int, bool> isByRef, string callee)
                {
                    for (var i = 0; i < args.Count; i++)
                        if (args[i] is IRVariable v && isByRef(i) && IsStorageBinding(Resolve(ctx, block, v.Name)))
                            throw new ForeignFeatureException(
                                $"MSIL: '{v.Name}' is captured by a lambda (or reached through Me inside one) and "
                                + $"passed ByRef to '{callee}' in '{g.Name}'. It lives in a closure environment's "
                                + "field, and this backend passes no field of one by reference; assign it to a "
                                + "local first.");
                }
                switch (inst)
                {
                    case IRLoad { Address: IRVariable loadAddress }
                        when IsStorageBinding(Resolve(ctx, block, loadAddress.Name)):
                        // The backend reads a variable-addressed load as the variable itself; with the
                        // variable moved into a field the load would dereference the field's value.
                        throw new ForeignFeatureException(
                            $"MSIL: an address load of '{loadAddress.Name}' in '{g.Name}', which a lambda captures, "
                            + "has no IL lowering in the first cut of closure conversion.");

                    case IRCall call when call.CalleeValue == null:
                    {
                        var declared = IsBareName(call.FunctionName) ? ModuleFunction(call.FunctionName)?.Parameters : null;
                        CheckByRef(call.Arguments,
                            i => (i < call.ByRefArguments.Count && call.ByRefArguments[i])
                                 || (declared != null && i < declared.Count && declared[i].IsByRef),
                            call.FunctionName);
                        break;
                    }
                    case IRInstanceMethodCall mc:
                    {
                        var declared = TryFindClass(_module, mc.Object?.Type?.Name, out var cls)
                                       && TryFindMethod(_module, cls, mc.MethodName, out _, out var m)
                            ? m.Implementation?.Parameters : null;
                        CheckByRef(mc.Arguments,
                            i => (i < mc.ByRefArguments.Count && mc.ByRefArguments[i])
                                 || (declared != null && i < declared.Count && declared[i].IsByRef),
                            mc.MethodName);
                        break;
                    }
                    case IRSwitch sw:
                        foreach (var name in PatternBindings(sw.PatternCases))
                            if (IsStorageBinding(Resolve(ctx, block, name)))
                                throw new ForeignFeatureException(
                                    $"MSIL: Select Case pattern variable '{name}' in '{g.Name}' is captured by a lambda. "
                                    + "The pattern binds it by storing to a local slot, which a closure environment's "
                                    + "field is not; the first cut of closure conversion refuses the shape.");
                        break;
                }
            }

            private static IEnumerable<string> PatternBindings(IEnumerable<IRPatternCase> cases)
            {
                foreach (var p in cases ?? Enumerable.Empty<IRPatternCase>())
                {
                    if (p == null) continue;
                    if (!string.IsNullOrEmpty(p.BindingVariable)) yield return p.BindingVariable;
                    if (p is IROrPatternCase or) foreach (var n in PatternBindings(or.Alternatives)) yield return n;
                    if (p is IRTuplePatternCase tuple) foreach (var n in PatternBindings(tuple.Elements)) yield return n;
                }
            }

            private IRValue RewriteOperand(FunctionContext ctx, BasicBlock block, IRInstruction consumer, IRValue value,
                List<IRInstruction> pre, int line)
            {
                switch (value)
                {
                    case null:
                    case IRConstant:
                        return value;

                    case IRVariable v when _synthetic.Contains(v):
                        return value;

                    case IRVariable v when IsLambdaReferenceName(v.Name):
                    {
                        if (!ctx.LambdaByName.TryGetValue(v.Name, out var lambda)) return value;
                        var target = EnvRef(ctx, ctx.Hosts[lambda], pre, line);
                        var slot = Slots(ctx, block, consumer).Where(s => ReferenceEquals(s.Value, v)).Select(s => s.Slot).FirstOrDefault();
                        var delegateType = slot ?? v.Type;
                        CheckSignature(delegateType, lambda.Parameters, lambda.ReturnType, $"lambda '{lambda.Name}'", ctx.Function.Name);
                        var create = new IRDelegateCreate(NewTemp(ctx), delegateType, target, lambda, isVirtual: false) { SourceLine = line };
                        pre.Add(create);
                        return create;
                    }

                    case IRVariable v:
                    {
                        var b = Resolve(ctx, block, v.Name);
                        switch (b.Kind)
                        {
                            case BindingKind.Env:
                            case BindingKind.InstanceMember:
                            case BindingKind.StaticMember:
                                return Load(ctx, ReceiverFor(ctx, b, pre, line), b.Field, b.Type, b.Storage, pre, line);
                            case BindingKind.Me:
                                return MeValue(ctx, pre, line);
                            default:
                                return value;
                        }
                    }

                    case IRInstruction tree when !ctx.InBlock.Contains(tree):
                        // An operand tree that lives in no block — a When guard, rendered inline by the
                        // backend. Nothing can be inserted in front of it, so it must need nothing.
                        if (TreeNeedsRewrite(ctx, block, tree))
                            throw new ForeignFeatureException(
                                $"MSIL: a Select Case 'When' guard in '{ctx.Function.Name}' reads a variable that a "
                                + "lambda captures (or a lambda itself). A guard is rendered inline, where no "
                                + "closure-environment load can be placed; the first cut refuses the shape. Compute "
                                + "the guard's value into a local before the Select Case.");
                        return value;

                    default:
                        return value;
                }
            }

            private bool TreeNeedsRewrite(FunctionContext ctx, BasicBlock block, IRInstruction tree)
            {
                var seen = new HashSet<IRValue>(ReferenceEqualityComparer.Instance);
                var stack = new Stack<IRValue>();
                stack.Push((IRValue)tree);
                while (stack.Count > 0)
                {
                    var v = stack.Pop();
                    if (v == null || !seen.Add(v)) continue;
                    if (v is IRVariable variable)
                    {
                        if (IsLambdaReferenceName(variable.Name) && ctx.LambdaByName.ContainsKey(variable.Name)) return true;
                        if (Resolve(ctx, block, variable.Name).Kind != BindingKind.None) return true;
                        continue;
                    }
                    if (v is IRInstruction nested && !ctx.InBlock.Contains(nested))
                        foreach (var u in OptimizationPass.UsesOf(nested)) stack.Push(u);
                }
                return false;
            }

            // ---------------------------------------------------------------------------------
            // Allocation (D2 amended, D4, D6)
            // ---------------------------------------------------------------------------------

            private void InsertPrologues(FunctionContext ctx)
            {
                var g = ctx.Function;
                var fl = ctx.FunctionEnv;
                if (fl == null) return;

                // Catch variables live at function level: the handler binds the exception to its
                // own slot, and the handler's first act copies it into the environment.
                foreach (var tc in g.Blocks.SelectMany(b => b.Instructions).OfType<IRTryCatch>().ToList())
                    foreach (var clause in tc.CatchClauses)
                    {
                        if (clause?.Block == null || string.IsNullOrEmpty(clause.VariableName)) continue;
                        if (!fl.Holds(clause.VariableName) || !ctx.CatchVars.ContainsKey(clause.VariableName)) continue;
                        var (field, type) = fl.Vars[clause.VariableName];
                        var caught = new IRVariable(clause.VariableName, type);
                        _synthetic.Add(caught);
                        Prepend(clause.Block, new List<IRInstruction>
                        {
                            Store(fl.LocalRef, field, caught, true, clause.Block.Instructions.FirstOrDefault()?.SourceLine ?? 0),
                        });
                    }

                // One fresh environment per iteration of each captured declaring For Each.
                foreach (var level in ctx.LoopLevels)
                {
                    var body = level.Loop.BodyBlock;
                    var line = level.Loop.SourceLine;
                    var list = new List<IRInstruction>();
                    var create = new IRNewObject(NewTemp(ctx), level.Env.Name, level.EnvType) { SourceLine = line };
                    list.Add(create);
                    list.Add(new IRAssignment(level.LocalRef, create) { SourceLine = line });
                    list.Add(Store(level.LocalRef, level.ParentField, level.Parent.LocalRef, true, line));
                    var (field, type) = level.Vars[level.Loop.VariableName];
                    var element = new IRVariable(level.Loop.VariableName, type);
                    _synthetic.Add(element);
                    list.Add(Store(level.LocalRef, field, element, true, line));
                    Prepend(body, list);
                    g.LocalVariables.Add(level.LocalRef);
                }

                // The function environment, at entry.
                {
                    var entry = g.EntryBlock ?? g.Blocks.FirstOrDefault();
                    if (entry == null) return;
                    if (g.Blocks.Any(b => ControlFlowGraph.SuccessorsOf(b).Any(s => ReferenceEquals(s, entry))))
                        throw new InvalidOperationException(
                            $"ClosureLowering: the entry block of '{g.Name}' is a branch target, so an environment "
                            + "allocated there would be re-created on every pass through it.");

                    var line = entry.Instructions.FirstOrDefault(i => i != null)?.SourceLine ?? 0;
                    var list = new List<IRInstruction>();
                    var create = new IRNewObject(NewTemp(ctx), fl.Env.Name, fl.EnvType) { SourceLine = line };
                    list.Add(create);
                    list.Add(new IRAssignment(fl.LocalRef, create) { SourceLine = line });
                    if (fl.ParentField != null)
                        list.Add(Store(fl.LocalRef, fl.ParentField, MeRef(ctx), true, line));
                    if (fl.MeField != null)
                    {
                        var me = new IRVariable("Me", fl.MeType);
                        _synthetic.Add(me);
                        list.Add(Store(fl.LocalRef, fl.MeField, me, true, line));
                    }
                    foreach (var p in g.Parameters)
                    {
                        if (p?.Name == null || p.IsByRef || !fl.Holds(p.Name)) continue;
                        var (field, _) = fl.Vars[p.Name];
                        var arg = new IRVariable(p.Name, p.Type) { IsParameter = true };
                        _synthetic.Add(arg);
                        list.Add(Store(fl.LocalRef, field, arg, true, line));
                    }
                    foreach (var local in g.LocalVariables.ToList())
                    {
                        if (local?.Name == null || !fl.Holds(local.Name) || ReferenceEquals(local, fl.LocalRef)) continue;
                        var type = local.Type;
                        if (type?.Kind == TypeKind.Array && type.ElementType != null
                            && type.ArrayDimensionSizes.Count == 1 && type.ArrayDimensionSizes[0] > 0)
                        {
                            // A sized array local's storage was created at method entry by the backend;
                            // as a field it has to be created here.
                            var alloc = new IRArrayAlloc(NewTemp(ctx), type.ElementType, type.ArrayDimensionSizes[0]) { SourceLine = line };
                            list.Add(alloc);
                            list.Add(Store(fl.LocalRef, fl.Vars[local.Name].Field, alloc, true, line));
                        }
                    }
                    Prepend(entry, list);

                    g.LocalVariables.RemoveAll(l => l?.Name != null && fl.Holds(l.Name) && !ReferenceEquals(l, fl.LocalRef));
                    g.LocalVariables.Insert(0, fl.LocalRef);
                }
            }

            private static void Prepend(BasicBlock block, List<IRInstruction> list)
            {
                foreach (var i in list) i.ParentBlock = block;
                block.Instructions.InsertRange(0, list);
            }

            // ---------------------------------------------------------------------------------
            // Post-conditions (D1)
            // ---------------------------------------------------------------------------------

            private void AssertPostConditions()
            {
                var leftover = _module.Functions.FirstOrDefault(f => f != null && f.IsLambda);
                if (leftover != null)
                    throw new InvalidOperationException(
                        $"ClosureLowering post-condition violated: lambda '{leftover.Name}' is still a lambda.");

                foreach (var ctx in _lowered)
                {
                    var functionLevel = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    if (ctx.FunctionEnv != null) functionLevel.UnionWith(ctx.FunctionEnv.Vars.Keys);
                    var enclosing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var level in ctx.HostChain) enclosing.UnionWith(level.Vars.Keys);

                    foreach (var block in ctx.Function.Blocks)
                    {
                        // A loop variable is hoisted only inside its own loop's body; the same name in
                        // another, uncaptured loop is that loop's own variable.
                        var loopLevel = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var level in ctx.LoopLevels)
                            if (level.Region.Contains(block)) loopLevel.UnionWith(level.Vars.Keys);

                        foreach (var variable in VariableMentions(block))
                        {
                            var name = variable.Name;
                            if (ctx.IsLambda && IsMeName(name))
                                Violation(ctx, name, "still names the creator's Me");
                            if (functionLevel.Contains(name) || loopLevel.Contains(name))
                                Violation(ctx, name, "still names a variable that lives in its environment");
                            if (ctx.IsLambda && enclosing.Contains(name) && !ctx.Declares(name))
                                Violation(ctx, name, "still names a variable of an enclosing function");
                        }
                    }
                }
            }

            /// <summary>Every variable a block's instructions read or write, operand trees included,
            /// except the ones the lowering itself placed (an environment's own reads of the slots
            /// it copies from).</summary>
            private IEnumerable<IRVariable> VariableMentions(BasicBlock block)
            {
                var seen = new HashSet<IRValue>(ReferenceEqualityComparer.Instance);
                var stack = new Stack<IRValue>();
                foreach (var inst in block.Instructions)
                {
                    if (inst == null) continue;
                    if (inst is IRAssignment a && a.Target != null && !_synthetic.Contains(a.Target)) yield return a.Target;
                    if (inst is IRStore s && s.Address is IRVariable av && !_synthetic.Contains(av)) yield return av;
                    foreach (var u in OptimizationPass.UsesOf(inst)) stack.Push(u);
                    while (stack.Count > 0)
                    {
                        var v = stack.Pop();
                        if (v == null || !seen.Add(v)) continue;
                        if (v is IRVariable variable)
                        {
                            if (!_synthetic.Contains(variable)) yield return variable;
                            continue;
                        }
                        if (v is IRInstruction nested && nested.ParentBlock == null)
                            foreach (var u in OptimizationPass.UsesOf(nested)) stack.Push(u);
                    }
                }
            }

            private static void Violation(FunctionContext ctx, string name, string what) =>
                throw new InvalidOperationException(
                    $"ClosureLowering post-condition violated: '{ctx.Function.Name}' {what} ('{name}').");
        }
    }

}
