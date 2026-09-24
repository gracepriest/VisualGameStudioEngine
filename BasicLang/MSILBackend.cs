using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.SemanticAnalysis;
using BasicLang.Compiler.CodeGen.CPlusPlus;

namespace BasicLang.Compiler.CodeGen.MSIL
{
    /// <summary>
    /// MSIL (.NET IL) code generator - emits textual IL assembly (.il files)
    /// Can be compiled with ilasm to create .NET assemblies
    /// </summary>
    public class MSILCodeGenerator : CodeGeneratorBase
    {
        private readonly StringBuilder _output;
        private readonly MSILCodeGenOptions _options;
        private readonly Dictionary<string, int> _localIndices;
        private readonly Dictionary<string, int> _paramIndices;
        private readonly Dictionary<IRValue, int> _tempIndices;
        private readonly Dictionary<string, int> _tempNameIndices;
        private readonly HashSet<string> _declaredIdentifiers;
        private readonly List<string> _stringConstants;

        /// <summary>
        /// Module-level variables, keyed by name, emitted as STATIC FIELDS on the module class.
        ///
        /// <para>⛔ This backend used to ignore <see cref="IRModule.GlobalVariables"/> entirely —
        /// the string "GlobalVariables" did not appear in this file. A module-level
        /// <c>Dim n As Integer = 7</c> therefore had no storage anywhere: reading it emitted
        /// <c>// WARNING: Unknown local 'n'</c> and pushed NOTHING, so the next instruction ran an
        /// operand short and the CLR rejected the method with InvalidProgramException. Writing it
        /// emitted <c>// WARNING: Cannot store to 'n'</c> and left the value on the stack, which
        /// is the more dangerous half: in <c>s = "SET" : Console.WriteLine(s)</c> the abandoned
        /// <c>ldstr</c> was consumed by the WriteLine that followed and the program printed "SET"
        /// — the right answer, reached by a stack accident that the next statement would
        /// destroy.</para>
        ///
        /// <para>Case-insensitive to match BasicLang name resolution, and populated before ANY
        /// type is emitted because a method on a user class can read a module global too.</para>
        /// </summary>
        private readonly Dictionary<string, IRVariable> _moduleGlobals =
            new Dictionary<string, IRVariable>(StringComparer.OrdinalIgnoreCase);

        private string _moduleName;
        private int _localCounter;
        private int _labelCounter;
        private int _maxStack;
        private int _currentStack;
        private IRModule _module;
        private IRClass _currentClass;

        // ---- Exception-handling region state ------------------------------------------
        //
        // IL's protected regions are not blocks you can branch into or out of freely: leaving
        // one needs `leave`, a `finally` ends with `endfinally`, and `ret` is illegal inside
        // either. The emitter therefore needs to know, at every branch and return, whether it is
        // currently writing inside a region — so the three visitors that emit control flow
        // (IRBranch, IRConditionalBranch, IRReturn) consult this state rather than each
        // re-deriving it. Null/false means "not in a region", and everything behaves as before.

        /// <summary>
        /// The block set <see cref="GenerateBasicBlock"/> is walking, held as a field so
        /// <see cref="Visit(IRTryCatch)"/> can mark a region's blocks emitted. Without that the
        /// same blocks are written twice — once inside the region and once as ordinary labelled
        /// blocks — which is the defect this rewrite replaced.
        /// </summary>
        private HashSet<BasicBlock> _visitedBlocks;

        /// <summary>Blocks of the protected region currently being emitted; null when outside one.</summary>
        private HashSet<BasicBlock> _regionBlocks;

        /// <summary>True while emitting a <c>finally</c> handler, whose only exit is <c>endfinally</c>.</summary>
        private bool _regionIsFinally;

        /// <summary>True while emitting a <c>catch</c> handler — the only place <c>rethrow</c> is legal.</summary>
        private bool _regionIsCatch;

        /// <summary>Label a region block falls out to when its IR block has no terminator.</summary>
        private string _regionLeaveTarget;

        /// <summary>Synthetic common exit for a <c>Return</c> lowered out of a protected region.</summary>
        private string _methodExitLabel;

        /// <summary>Slot holding a lowered <c>Return</c>'s value; -1 for a void method.</summary>
        private int _methodExitResultLocal;

        /// <summary>Whether any <c>Return</c> was actually lowered, so the exit block is needed.</summary>
        private bool _methodExitUsed;

        /// <summary>
        /// Locals this backend declares that <c>IRFunction.LocalVariables</c> does not carry:
        /// catch-clause exception variables, the lowered-return result slot, and field-store
        /// scratch slots.
        /// </summary>
        private readonly List<(int Index, string Spec, string Name)> _syntheticLocals = new();

        // ---- For Each state -----------------------------------------------------------
        //
        // A For Each is a STRUCTURED instruction like IRTryCatch: it carries its body and its
        // continuation as blocks rather than as branch terminators, so it has the same three
        // obligations — reserve the slots it needs BEFORE `.locals init` is written, mark the
        // blocks it emits so nothing writes them a second time, and re-spell the branches that
        // cross its own boundary. The old emitter met none of them.

        /// <summary>
        /// The two slots each <c>For Each</c> needs, reserved before <c>.locals init</c> is
        /// written: one for the loop VARIABLE and one for the enumerator.
        ///
        /// <para>⛔ <b>Neither existed.</b> <c>IRBuilder</c> deliberately keeps the loop variable
        /// out of <c>IRFunction.LocalVariables</c> ("the foreach statement declares it") — which
        /// is right for C#, C++ and JavaScript, where the emitted loop header really does declare
        /// it, and leaves IL with no storage at all: every read emitted
        /// <c>// WARNING: Unknown local 'n'</c> and pushed NOTHING, so the next instruction ran an
        /// operand short. The enumerator was worse than absent: it was allocated during emission
        /// with <c>_localCounter++</c>, a counter unrelated to <c>_localIndices</c>, so
        /// <c>stloc.s 0</c> landed on the COLLECTION's own slot — measured, the same defect the
        /// indexer path records.</para>
        ///
        /// <para>Keyed by the instruction, not by the variable's name, so two loops in one method
        /// get two slots even when they share a name and differ in element type.</para>
        /// </summary>
        private readonly Dictionary<IRForEach, (int VarIndex, int EnumIndex)> _foreachSlots = new();

        /// <summary>
        /// Continuation blocks whose incoming branch means "next iteration", mapped to the loop
        /// head to branch to instead.
        ///
        /// <para>⛔ <b>The IR gives the end of an iteration and the end of the loop the SAME
        /// target.</b> <c>IRBuilder</c> terminates the body with <c>IRBranch(endBlock)</c> and
        /// pushes <c>LoopContext(endBlock, endBlock)</c>, so falling off the end of the body is
        /// spelled exactly like leaving it. Emitting that branch literally runs the body once and
        /// walks out — which is what every other backend has to work around too (C++ turns the
        /// same edge into <c>continue;</c>, C# lets the emitted <c>foreach</c> header own
        /// iteration). Saved and restored around each body so a nested loop classifies its edges
        /// against the INNER loop while it is being emitted and the outer one afterwards.</para>
        /// </summary>
        private readonly Dictionary<BasicBlock, string> _foreachContinueLabels = new();

        /// <summary>
        /// Blocks a structured instruction has already written, so a surrounding walk does not
        /// write them again.
        ///
        /// <para>⛔ <b>The For Each body was emitted TWICE</b> — once inlined by
        /// <c>Visit(IRForEach)</c> and once as an ordinary labelled block, because
        /// <c>ControlFlowGraph.Build</c> wires the body in as a CFG successor of the block holding
        /// the instruction. That is the same defect <c>Try</c> had; <c>Visit(IRTryCatch)</c> fixed
        /// it by marking <c>_visitedBlocks</c>, which is enough for
        /// <see cref="GenerateBasicBlock"/> but NOT for <see cref="EmitRegionBody"/>, whose block
        /// list is collected up front. A For Each inside a Try needs both, so both consult
        /// this.</para>
        /// </summary>
        private readonly HashSet<BasicBlock> _consumedBlocks = new();

        /// <summary>The slot spec for a For Each's enumerator.</summary>
        private const string EnumeratorSpec = "class [mscorlib]System.Collections.IEnumerator";

        // ---- Instance-method state ----------------------------------------------------
        //
        // An instance method is handed `Me` in argument slot 0, which shifts EVERY declared
        // parameter up by one and gives the body a receiver it can reach fields and sibling
        // methods through. The emitter had no notion of any of that: it numbered parameters from
        // 0 (so `Add(a, b)` emitted `ldarg.0 / ldarg.1 / add` and added the OBJECT REFERENCE to
        // `a` — a silent wrong answer), and a bare field name resolved to nothing at all.

        /// <summary>True while emitting a method that has <c>Me</c> in argument slot 0.</summary>
        private bool _currentMethodIsInstance;

        /// <summary>
        /// The instance fields reachable as bare names in the current method body. Empty for a
        /// static or module method, which is what keeps their output byte-identical.
        /// </summary>
        private readonly Dictionary<string, TypeInfo> _currentClassFields =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Scratch slot per field that the current method ASSIGNS — see <see cref="EmitStoreLocal"/>.</summary>
        private readonly Dictionary<string, int> _fieldStoreScratch =
            new(StringComparer.OrdinalIgnoreCase);

        // ================================================================================
        // ByRef. Two separate defects lived here, and only one of them is ByRef's own.
        //
        // (A) EmitStoreLocal had no `starg` arm AT ALL: it walked locals, fields, properties,
        //     static fields and module globals, and fell off the end with
        //     `// WARNING: Cannot store to 'n'` for any PARAMETER. The computed value stayed on
        //     the stack, `ret` followed with a non-empty stack, and the CLR rejected the method.
        //     That is why `Sub Bump(n As Integer) : n = n + 1` and `For n = 1 To 3` over a
        //     parameter BOTH threw InvalidProgramException with no ByRef anywhere in sight.
        //
        // (B) ByRef itself: the signature carried no `&` and the call site pushed the VALUE, so
        //     a write-through had nowhere to land even once (A) let it be emitted.
        //
        // A ByRef parameter's slot holds a MANAGED POINTER: reading it is `ldarg` + `ldind.*`,
        // writing it is `ldarg` + value + `stind.*`, and passing it on is the bare `ldarg`.

        /// <summary>
        /// The current method's ByRef parameters, by name, holding the POINTED-AT type — what
        /// <c>ldind</c>/<c>stind</c> need and what the <c>&amp;</c> in the signature is applied to.
        /// Empty for every method that has none, which is what keeps their output unchanged.
        /// </summary>
        private readonly Dictionary<string, TypeInfo> _byRefParams =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Scratch slot per ByRef parameter the current method ASSIGNS THROUGH. <c>stind</c> wants
        /// the address UNDER the value and this backend arrives with the value already on the
        /// stack, so the value has to be parked while the address goes down — the same park-and-
        /// re-push <see cref="_fieldStoreScratch"/> exists for, for the same reason: IL has no swap.
        /// </summary>
        private readonly Dictionary<string, int> _byRefStoreScratch =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The enclosing class's properties, for a BARE name inside one of its own methods.
        ///
        /// <para>⛔ Separate from <see cref="_currentClassFields"/> because a property is not
        /// storage: the bare name has to become an accessor CALL, not an <c>ldfld</c>. Without
        /// this, <c>Alpha = Alpha + 1</c> inside the class emitted
        /// <c>// WARNING: Unknown local 'Alpha'</c>, pushed nothing, ran the <c>add</c> an operand
        /// short and stored the result into a temporary — the CLR rejected the method.</para>
        /// </summary>
        private readonly Dictionary<string, IRProperty> _currentClassProperties =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>The class <see cref="_currentClassProperties"/> was filled from.</summary>
        private IRClass _currentClassOwner;

        /// <summary>The IL name of the type whose fields <see cref="_currentClassFields"/> holds.</summary>
        private string _currentClassToken;

        /// <summary>
        /// The IL token of the class that DECLARES each bare-nameable field, and the class that
        /// declares each bare-nameable property.
        ///
        /// <para>⛔ An <c>ldfld</c>/<c>stfld</c> names a type, and for an INHERITED field that
        /// type is the BASE, not the class being emitted. Both tables used to hold the emitted
        /// class's own members only and both emission sites spelled <see cref="_currentClassToken"/>,
        /// so once the front end let a derived method name an inherited field the IL said
        /// <c>Box::Total</c> for a field only <c>Base</c> defines: MissingFieldException, and for a
        /// read whose miss pushed nothing, an operand-short body the CLR refuses outright
        /// (InvalidProgramException). Same rule as <see cref="TryFindStaticMethod"/>'s, recorded
        /// there: name the DECLARING class.</para>
        ///
        /// <para>⚠ A PROPERTY twin of this table was here and is gone: it survived mutation. A
        /// bare property goes out as an accessor CALL through <c>EmitPropertyGet</c>/<c>Set</c>,
        /// and those resolve the accessor through <see cref="TryFindProperty"/>, which already
        /// walks the base chain — so recording the declaring class a second time decided
        /// nothing. The FIELD table has no such second walk, which is why it stays.</para>
        /// </summary>
        private readonly Dictionary<string, string> _currentClassFieldOwner =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>The IL token naming the class that declares bare field <paramref name="name"/>.</summary>
        private string FieldOwnerToken(string name) =>
            _currentClassFieldOwner.TryGetValue(name, out var token) ? token : _currentClassToken;

        /// <summary>
        /// The IL token for an <c>ldfld</c>/<c>stfld</c> through a RECEIVER: the class that
        /// declares the field, found by walking up from the receiver's static type, else the
        /// receiver's own token.
        ///
        /// <para>⛔ Naming the receiver's type is right only while the field is the receiver
        /// class's own. <c>b.InheritedField</c> emitted <c>Box::Total</c> for a field that only
        /// <c>Base</c> declares — MissingFieldException.</para>
        /// </summary>
        private string DeclaringFieldToken(TypeInfo receiver, string fieldName)
        {
            var fallback = IlReceiverToken(receiver);
            if (string.IsNullOrEmpty(receiver?.Name) || string.IsNullOrEmpty(fieldName)) return fallback;
            if (!TryFindClass(receiver.Name, out var cls)) return fallback;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var current = cls; current != null; )
            {
                if (!seen.Add(current.Name)) break;

                if (current.Fields != null && current.Fields.Any(f => f?.Name != null && !f.IsStatic
                        && string.Equals(f.Name, fieldName, StringComparison.OrdinalIgnoreCase)))
                    return SanitizeName(current.Name);

                if (string.IsNullOrEmpty(current.BaseClass)) break;
                if (!TryFindClass(current.BaseClass, out current)) break;
            }
            return fallback;
        }

        /// <summary>
        /// The DECLARED type of instance field <paramref name="fieldName"/>, found by the same
        /// base-chain walk <see cref="DeclaringFieldToken"/> uses — its type-bearing twin.
        ///
        /// <para>⛔ AN ldfld/stfld OPERAND NAMES THE FIELD'S DECLARED TYPE, NOT THE VALUE'S.
        /// The store used to render <c>MapType(fieldStore.Value?.Type)</c>, so `k.Pet = New Dog()`
        /// against `Public Pet As Animal` emitted <c>stfld class Dog 'Kennel'::'Pet'</c> — a field
        /// reference that binds to nothing, MissingFieldException at RUN time. The matching LOAD
        /// always used the field's own type, which is why reads worked and writes did not.</para>
        /// </summary>
        private TypeInfo DeclaredFieldType(TypeInfo receiver, string fieldName)
        {
            if (string.IsNullOrEmpty(receiver?.Name) || string.IsNullOrEmpty(fieldName)) return null;
            if (!TryFindClass(receiver.Name, out var cls)) return null;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var current = cls; current != null; )
            {
                if (!seen.Add(current.Name)) break;

                var match = current.Fields?.FirstOrDefault(f => f?.Name != null && !f.IsStatic
                    && string.Equals(f.Name, fieldName, StringComparison.OrdinalIgnoreCase));
                if (match?.Type != null) return match.Type;

                if (string.IsNullOrEmpty(current.BaseClass)) break;
                if (!TryFindClass(current.BaseClass, out current)) break;
            }
            return null;
        }

        /// <summary>
        /// The parameter list of a call, rendered from the CALLEE'S DECLARATION when one can be
        /// found and from the arguments only as a last resort.
        ///
        /// <para>⛔ THE CALL SITE IS NOT A RELIABLE SOURCE — the rule this file already states at
        /// <see cref="EmitStaticUserCall"/>. Rendering `Report(New Dog())` from the argument emits
        /// <c>Report(class Dog)</c> against a method declared <c>Report(class Animal)</c>: it
        /// assembles, because ilasm does not resolve member references, and then fails at RUN time
        /// with MissingMethodException. Passing an EXACTLY-typed argument hid it, which is why the
        /// control shape has always worked.</para>
        /// </summary>
        private string DeclaredParamList(IReadOnlyList<IRVariable> declared, IEnumerable<IRValue> arguments) =>
            declared != null && declared.Count > 0
                ? string.Join(", ", declared.Select(ParamSpec))
                : string.Join(", ", (arguments ?? Enumerable.Empty<IRValue>()).Select(a => IlTypeSpec(a.Type)));

        /// <summary>
        /// True when <paramref name="irClass"/> itself implements (lists) an interface that declares
        /// a member named <paramref name="memberName"/> — see
        /// <see cref="InterfaceImplementationLookup"/> for why an interface inherited from a base
        /// class does not count.
        ///
        /// <para>⛔ AN INTERFACE SLOT CAN ONLY BE FILLED BY A VIRTUAL METHOD. The class emitted
        /// its `implements` clause correctly and then emitted the implementing method as an
        /// ordinary non-virtual one, so the type would not even load: TypeLoadException "Method
        /// 'Speak' in type 'Dog' does not have an implementation" — before a line of it ran, and
        /// for a class that plainly does declare Speak. C# marks such a method
        /// `newslot virtual final`, which is what the caller emits.</para>
        /// </summary>
        private bool ImplementsInterfaceMember(IRClass irClass, string memberName)
        {
            if (irClass == null || string.IsNullOrEmpty(memberName) || _module?.Interfaces == null) return false;

            return InterfaceImplementationLookup.ImplementedInterfaces(_module, irClass).Any(iface =>
                (iface.Methods != null && iface.Methods.Any(m => m?.Name != null
                    && string.Equals(m.Name, memberName, StringComparison.OrdinalIgnoreCase)))
                || (iface.Properties != null && iface.Properties.Any(pr => pr?.Name != null
                    && string.Equals(pr.Name, memberName, StringComparison.OrdinalIgnoreCase))));
        }

        /// <summary>
        /// The interface method <paramref name="memberName"/> declared by <paramref name="typeName"/>
        /// or one of its base interfaces, else null.
        ///
        /// <para>⛔ AN INTERFACE RECEIVER IS NOT A CLASS, so the class-side lookups all miss it and
        /// the signature fell back to the CALL SITE. The front end types an interface-method call
        /// <c>Object</c>, so <c>s.Speak()</c> emitted
        /// <c>callvirt instance object ISpeaker::Speak()</c> against a method declared to return
        /// <c>string</c> — MissingMethodException at RUN time. The declaration is the only
        /// reliable source here too.</para>
        /// </summary>
        private IRInterfaceMethod DeclaredInterfaceMethod(string typeName, string memberName)
        {
            if (string.IsNullOrEmpty(typeName) || string.IsNullOrEmpty(memberName)
                || _module?.Interfaces == null) return null;

            var pending = new Queue<string>();
            pending.Enqueue(typeName);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (pending.Count > 0)
            {
                var name = pending.Dequeue();
                if (string.IsNullOrEmpty(name) || !seen.Add(name)) continue;
                if (!_module.Interfaces.TryGetValue(name, out var iface) || iface == null) continue;

                var m = iface.Methods?.FirstOrDefault(x => x?.Name != null
                    && string.Equals(x.Name, memberName, StringComparison.OrdinalIgnoreCase));
                if (m != null) return m;

                foreach (var b in iface.BaseInterfaces ?? new List<string>()) pending.Enqueue(b);
            }
            return null;
        }

        /// <summary>The declared parameters of free/module function <paramref name="name"/>, or null.</summary>
        private IReadOnlyList<IRVariable> DeclaredFunctionParams(string name)
        {
            if (string.IsNullOrEmpty(name) || _module?.Functions == null) return null;
            var fn = _module.Functions.FirstOrDefault(
                f => f?.Name != null && string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
            return fn?.Parameters;
        }

        /// <summary>The declared parameters of instance method <paramref name="name"/> on
        /// <paramref name="owner"/> or a base of it, or null.</summary>
        private IReadOnlyList<IRVariable> DeclaredMethodParams(IRClass owner, string name)
        {
            if (owner == null || string.IsNullOrEmpty(name)) return null;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var current = owner; current != null; )
            {
                if (!seen.Add(current.Name)) break;
                var m = current.Methods?.FirstOrDefault(x => x?.Name != null
                    && string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                if (m?.Implementation?.Parameters != null) return m.Implementation.Parameters;
                if (string.IsNullOrEmpty(current.BaseClass)) break;
                if (!TryFindClass(current.BaseClass, out current)) break;
            }
            return null;
        }

        /// <summary>The declared parameters of the constructor on <paramref name="className"/>
        /// that takes <paramref name="argCount"/> arguments, or null.</summary>
        private IReadOnlyList<IRVariable> DeclaredCtorParams(string className, int argCount)
        {
            if (!TryFindClass(className, out var cls) || cls.Constructors == null) return null;

            // ⛔ THE PARAMETERS LIVE ON Implementation, NOT ON IRConstructor.Parameters. The IR
            // builder sets only Access and Implementation, so IRConstructor.Parameters is forever
            // the empty list its own initializer made — JavaScriptBackend already carries a
            // comment saying so, and C#, C++, LLVM and this file's own line ~1308 all read
            // Implementation.Parameters. Reading the empty one made this helper DEAD CODE: no
            // arity above zero could ever match, every caller silently fell back to spelling the
            // ARGUMENT types, and that is the very defect this helper exists to prevent. Measured:
            // `newobj instance void 'Shelter'::.ctor(class 'Dog')` against a constructor declared
            // `.ctor(class 'Animal')` — MissingMethodException at run time.
            var exact = cls.Constructors.FirstOrDefault(
                c => c?.Implementation?.Parameters != null && c.Implementation.Parameters.Count == argCount);
            return exact?.Implementation?.Parameters;
        }

        /// <summary>
        /// The class that declares INSTANCE method <paramref name="name"/>, reachable from
        /// <paramref name="irClass"/> by walking bases — the instance counterpart of
        /// <see cref="TryFindStaticMethod"/>, visited set and all.
        /// </summary>
        private IRClass DeclaringClassOfInstanceMethod(IRClass irClass, string name)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var current = irClass; current != null; )
            {
                if (!seen.Add(current.Name)) break;

                if (current.Methods != null && current.Methods.Any(m => m != null && !m.IsStatic
                        && string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)))
                    return current;

                if (string.IsNullOrEmpty(current.BaseClass)) break;
                if (!TryFindClass(current.BaseClass, out current)) break;
            }
            return null;
        }

        public override string BackendName => "MSIL";
        public override TargetPlatform Target => TargetPlatform.MSIL;

        public MSILCodeGenerator(MSILCodeGenOptions options = null)
        {
            _output = new StringBuilder();
            _options = options ?? new MSILCodeGenOptions();
            _localIndices = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            _paramIndices = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            _tempIndices = new Dictionary<IRValue, int>();
            _tempNameIndices = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            _declaredIdentifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _stringConstants = new List<string>();
            _typeMapper = new MSILTypeMapper();
        }

        protected override void InitializeTypeMap()
        {
            // MSIL type mappings
            _typeMap["Integer"] = "int32";
            _typeMap["Long"] = "int64";
            _typeMap["Single"] = "float32";
            _typeMap["Double"] = "float64";
            _typeMap["String"] = "string";
            _typeMap["Boolean"] = "bool";
            _typeMap["Char"] = "char";
            _typeMap["Void"] = "void";
            _typeMap["Object"] = "object";
            _typeMap["Byte"] = "uint8";
            _typeMap["Short"] = "int16";
            _typeMap["SByte"] = "int8";
            _typeMap["UByte"] = "uint8";
            _typeMap["UShort"] = "uint16";
            _typeMap["UInteger"] = "uint32";
            _typeMap["ULong"] = "uint64";
            _typeMap["Decimal"] = "valuetype [System.Runtime]System.Decimal";
        }

        public override string Generate(IRModule module)
        {
            _module = module;

            // Backend honesty (spec decision 12): MSIL rejects C++-only passthrough
            // (#CppInclude / :: foreign types / cpp{} inline blocks) AND collections
            // (List/Dictionary/HashSet are not yet lowered to IL) with a clean error
            // before any emission. Its own inline language is "msil".
            //
            // ⛔ allowForeignIdentifiers stays at its default FALSE — see the note on the C#
            // backend's call. It is JavaScript-only, because only there does `::name` mean a real
            // identifier in the target language.
            // ⛔ Collections stay REFUSED until generic member signatures are emitted. The type
            // NAMING is done (TryCollectionToken), and that was the easy half; IL also requires a
            // method on a generic instantiation to carry the GENERIC DEFINITION's signature —
            // `List`1<string>::Add(!0)`, never `Add(string)` — or the call fails at run time with
            // MissingMethodException.
            //
            // ⛔ And the obvious shortcut is UNSOUND: substituting any parameter whose type equals
            // a generic argument gets List(Of Integer).Add(5) right (!0) and RemoveAt(0) wrong
            // (must stay int32). It needs a per-member table saying which positions are generic.
            // Flipping this to false before that exists trades a clean BL diagnostic for a
            // runtime crash, which is the one thing the honesty matrix exists to prevent.
            ForeignFeatureChecker.Check(module, "MSIL", rejectCollections: false, ownInlineLanguage: "msil");

            _output.Clear();
            _stringConstants.Clear();
            _labelCounter = 0;

            // ⛔ Both of these must be set BEFORE the first type is emitted, not inside
            // GenerateClass where the module class is built LAST. A method on a user class can
            // read a module-level variable, and resolving one needs the owning class's name.
            _moduleName = SanitizeName(module.Name);
            CollectModuleGlobals(module);

            // Generate assembly header
            GenerateHeader(module);

            // Generate enums
            foreach (var irEnum in module.Enums.Values)
            {
                GenerateEnum(irEnum);
                WriteLine();
            }

            // Generate delegates
            foreach (var irDelegate in module.Delegates.Values)
            {
                GenerateDelegate(irDelegate);
                WriteLine();
            }

            // Generate interfaces
            foreach (var irInterface in module.Interfaces.Values)
            {
                GenerateInterface(irInterface);
                WriteLine();
            }

            // Generate user-defined classes
            foreach (var irClass in module.Classes.Values)
            {
                GenerateUserClass(irClass);
                WriteLine();
            }

            // Generate main module class with standalone methods
            GenerateClass(module);

            return _output.ToString();
        }

        private void GenerateEnum(IREnum irEnum)
        {
            var enumName = SanitizeName(irEnum.Name);
            var underlyingType = "int32";
            if (irEnum.UnderlyingType != null)
            {
                underlyingType = MapType(irEnum.UnderlyingType);
            }

            WriteLine($".class public auto ansi sealed {enumName}");
            WriteLine("       extends [mscorlib]System.Enum");
            WriteLine("{");

            // Value field
            WriteLine($"  .field public specialname rtspecialname {underlyingType} value__");

            // Enum members as static literal fields
            foreach (var member in irEnum.Members)
            {
                var value = member.Value ?? 0;
                WriteLine($"  .field public static literal valuetype {enumName} {SanitizeName(member.Name)} = {underlyingType}({value})");
            }

            WriteLine($"}} // end of class {enumName}");
        }

        private void GenerateDelegate(IRDelegate irDelegate)
        {
            var delegateName = SanitizeName(irDelegate.Name);
            var returnType = IlTypeSpec(irDelegate.ReturnType);
            var paramTypes = string.Join(", ", irDelegate.Parameters.Select(IlParameterSpec));

            WriteLine($".class public auto ansi sealed {delegateName}");
            WriteLine("       extends [mscorlib]System.MulticastDelegate");
            WriteLine("{");

            // Constructor
            WriteLine("  .method public hidebysig specialname rtspecialname");
            WriteLine("          instance void .ctor(object 'object', native int 'method') runtime managed");
            WriteLine("  {");
            WriteLine("  } // end of method .ctor");
            WriteLine();

            // Invoke method
            WriteLine("  .method public hidebysig newslot virtual");
            WriteLine($"          instance {returnType} Invoke({paramTypes}) runtime managed");
            WriteLine("  {");
            WriteLine("  } // end of method Invoke");
            WriteLine();

            // BeginInvoke
            WriteLine("  .method public hidebysig newslot virtual");
            WriteLine($"          instance class [mscorlib]System.IAsyncResult BeginInvoke({paramTypes}, class [mscorlib]System.AsyncCallback callback, object 'object') runtime managed");
            WriteLine("  {");
            WriteLine("  } // end of method BeginInvoke");
            WriteLine();

            // EndInvoke
            WriteLine("  .method public hidebysig newslot virtual");
            WriteLine($"          instance {returnType} EndInvoke(class [mscorlib]System.IAsyncResult result) runtime managed");
            WriteLine("  {");
            WriteLine("  } // end of method EndInvoke");

            WriteLine($"}} // end of class {delegateName}");
        }

        /// <summary>
        /// A BasicLang type as an IL <b>type spec</b> — what goes in a local, parameter, field
        /// or signature position.
        ///
        /// <para>IL primitives are keywords there (<c>int32</c>, <c>string</c>), but a
        /// reference type is NOT: a class must be spelled <c>class Foo</c>, and an array is the
        /// element spec followed by <c>[]</c>. Emitting the BasicLang name raw produced
        /// <c>[0] Greeter g</c> and <c>[0] String[] a</c>, both of which ilasm rejects outright
        /// — the backend could not declare a local of any user class or any array.</para>
        /// </summary>
        private string IlTypeSpec(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return "object";

            // An array: judge the element and re-attach the rank. Done before the primitive
            // lookup so `String[]` is not mistaken for an unknown class named "String[]".
            var trimmed = typeName.Trim();
            if (trimmed.EndsWith("[]", StringComparison.Ordinal))
                return IlTypeSpec(trimmed.Substring(0, trimmed.Length - 2)) + "[]";

            var mapped = MapTypeName(trimmed);

            // MapTypeName passes an unknown name through SanitizeName, so anything that did not
            // land on an IL keyword is a class reference and needs the `class` prefix.
            return IlPrimitives.Contains(mapped) ? mapped : "class " + mapped;
        }

        /// <summary>
        /// A BasicLang type as an IL <b>type token</b> — what <c>box</c>, <c>newarr</c>,
        /// <c>castclass</c>, <c>isinst</c> and friends take as their operand.
        ///
        /// <para>⛔ This is NOT <see cref="IlTypeSpec"/> with different spacing. A token names a
        /// TYPE, so an IL primitive keyword is not valid here — <c>box</c> needs the BCL value
        /// type it stands for, <c>[mscorlib]System.Int32</c>. The backend used to emit
        /// <c>box [{mapped}]</c>, and in IL <c>[X]</c> means "assembly X", so <c>box [int32]</c>
        /// reads as a type in an assembly named <c>int32</c> and does not parse at all. That
        /// single mistake blocked every <c>CStr</c> of a number.</para>
        /// </summary>
        private string IlTypeToken(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return "[mscorlib]System.Object";

            var trimmed = typeName.Trim();
            if (trimmed.EndsWith("[]", StringComparison.Ordinal))
                return IlTypeToken(trimmed.Substring(0, trimmed.Length - 2)) + "[]";

            var mapped = MapTypeName(trimmed);
            return PrimitiveTokens.TryGetValue(mapped, out var bcl)
                ? bcl
                : mapped;
        }

        /// <summary>
        /// <see cref="IlTypeSpec(string)"/> for a resolved <c>TypeInfo</c>, routed through the
        /// existing <c>MapType</c> so there is still ONE place that decides what a BasicLang
        /// type is called in IL. Both string forms are accepted by the spec/token functions —
        /// a BasicLang name (<c>Integer</c>) and an already-mapped one (<c>int32</c>) — because
        /// callers hold one or the other depending on how far through lowering they are.
        ///
        /// <para><b>Collections are handled here, not in the string form</b>, because only the
        /// <c>TypeInfo</c> carries <see cref="TypeInfo.GenericArguments"/>. A <c>List</c> whose
        /// element type has been lost is not a type IL can name.</para>
        /// </summary>
        private string IlTypeSpec(TypeInfo type) =>
            TryCollectionToken(type, out var token) ? "class " + token : IlTypeSpec(MapType(type));

        /// <inheritdoc cref="IlTypeToken(string)"/>
        private string IlTypeToken(TypeInfo type) =>
            TryCollectionToken(type, out var token) ? token : IlTypeToken(MapType(type));

        /// <summary>
        /// The BCL generic token for a BasicLang collection — <c>List</c> →
        /// <c>[mscorlib]System.Collections.Generic.List`1&lt;string&gt;</c>.
        ///
        /// <para><b>Why MSIL gets these natively and LLVM does not.</b> Until 2026-09-15 the
        /// backend honesty matrix (spec decision 12) refused collections on LLVM and MSIL
        /// together. That grouping was right for LLVM, which has no BCL to reach for — and
        /// wrong for MSIL the moment it became a maintained target, because MSIL RUNS on .NET:
        /// <c>List`1</c> is already in the runtime it targets, exactly as the C# backend uses
        /// it. Nothing needs implementing, only naming.</para>
        ///
        /// <para>⛔ Generic ARGUMENTS are required, not decorative. <c>class List</c> — the name
        /// with its arguments dropped — is what the backend emitted before this, and ilasm
        /// rejects it as an undefined class. An arity-1 collection with no recorded argument is
        /// therefore NOT guessed at: this returns false and lets the ordinary path refuse it,
        /// rather than emitting <c>List`1&lt;object&gt;</c> and silently widening the element
        /// type.</para>
        /// </summary>
        private bool TryCollectionToken(TypeInfo type, out string token)
        {
            token = null;
            if (type?.Name == null) return false;

            if (!CollectionArities.TryGetValue(type.Name, out var shape)) return false;

            var args = type.GenericArguments;
            if (args == null || args.Count != shape.Arity) return false;

            // ⛔ Arguments are type SPECS, not tokens: IL writes `List`1<string>`, never
            // `List`1<[mscorlib]System.String>`. The generated indexer call has always spelled
            // it the first way (`IList`1<string>::get_Item`), so getting this wrong here would
            // have produced two different spellings of one type in a single method.
            var rendered = args.Select(IlTypeSpec);

            token = $"[mscorlib]{shape.ClrName}`{shape.Arity}<{string.Join(", ", rendered)}>";
            return true;
        }

        /// <summary>
        /// A parameter's IL type spec for a SIGNATURE position (interface members, delegate
        /// Invoke, method declarations).
        ///
        /// <para>⛔ These sites used to read <c>p.TypeName</c>, a STRING, which cannot carry
        /// generic arguments — so a <c>List(Of Integer)</c> parameter emitted a bare
        /// <c>List</c> and the whole method failed to assemble. The parameter already holds the
        /// resolved <see cref="TypeInfo"/>; that is what knows the element type.</para>
        ///
        /// <para>The name is kept as the fallback for a parameter whose TypeInfo never got
        /// populated, so this cannot be a regression for shapes that worked before.</para>
        /// </summary>
        private string IlParameterSpec(IRParameter parameter) =>
            parameter?.Type != null ? IlTypeSpec(parameter.Type) : MapTypeName(parameter?.TypeName);

        /// <summary>
        /// One collection member's IL signature, written in terms of the GENERIC DEFINITION.
        /// </summary>
        /// <param name="Il">
        /// The member name as IL spells it — a property becomes its accessor
        /// (<c>Count</c> → <c>get_Count</c>).
        /// </param>
        /// <param name="Ret">Return type, with <c>!0</c>/<c>!1</c> for generic positions.</param>
        /// <param name="Params">Parameter types, same convention.</param>
        private sealed record CollectionMember(string Il, string Ret, string Params);

        /// <summary>
        /// <b>The narrow supported surface of <c>List</c> and <c>Dictionary</c> on MSIL</b>
        /// (2026-09-15), keyed by CLR type name and BasicLang member name.
        ///
        /// <para><b>Why a table and not a rule.</b> IL requires a method on a generic
        /// instantiation to carry the generic definition's signature —
        /// <c>List`1&lt;string&gt;::Add(!0)</c>, never <c>Add(string)</c> — so something has to
        /// say which positions are generic. The tempting rule, "substitute any parameter whose
        /// type equals a generic argument", is UNSOUND: it gets <c>List(Of Integer).Add(5)</c>
        /// right (<c>!0</c>) and <c>RemoveAt(0)</c> wrong, because that index must stay
        /// <c>int32</c>. Nothing about the argument types distinguishes those two cases, so the
        /// positions are recorded rather than inferred.</para>
        ///
        /// <para><b>A member missing from this table is REFUSED, never guessed.</b> That is the
        /// whole reason a narrow table is safe to ship: the supported set is exactly what is
        /// written here, and everything else still gets the clean BasicLang diagnostic it got
        /// when collections were refused wholesale. Widening the set means adding a row and a
        /// round-trip test, not relaxing a check.</para>
        /// </summary>
        private static readonly Dictionary<string, Dictionary<string, CollectionMember>> CollectionMembers =
            new(StringComparer.Ordinal)
            {
                ["System.Collections.Generic.List"] = new(StringComparer.OrdinalIgnoreCase)
                {
                    ["Add"] = new("Add", "void", "!0"),
                    ["Count"] = new("get_Count", "int32", ""),
                    ["Contains"] = new("Contains", "bool", "!0"),
                    // The indexer. `Item` is the BasicLang spelling; IL wants the accessors, and
                    // the INDEX is int32 while the element is generic — the exact asymmetry the
                    // unsound rule above would have flattened.
                    ["get_Item"] = new("get_Item", "!0", "int32"),
                    ["set_Item"] = new("set_Item", "void", "int32, !0"),
                },
                ["System.Collections.Generic.Dictionary"] = new(StringComparer.OrdinalIgnoreCase)
                {
                    ["Add"] = new("Add", "void", "!0, !1"),
                    ["Count"] = new("get_Count", "int32", ""),
                    ["ContainsKey"] = new("ContainsKey", "bool", "!0"),
                    // Keyed by !0, yielding !1 — and the backend used to emit this as
                    // `IList`1<int32>::get_Item(string)`, which names the VALUE type as the
                    // list's element and the KEY type as an integer index. Wrong in both
                    // positions, on a call that assembled.
                    ["get_Item"] = new("get_Item", "!1", "!0"),
                    ["set_Item"] = new("set_Item", "void", "!0, !1"),
                },
            };

        /// <summary>
        /// The supported signature for <paramref name="member"/> on a collection receiver, or
        /// null when the receiver is not a collection at all (leave it to the ordinary path).
        /// Throws for a collection member OUTSIDE the table — see
        /// <see cref="CollectionMembers"/> for why that refusal is the point.
        /// </summary>
        private bool TryCollectionMember(
            TypeInfo receiver, string member, out string token, out CollectionMember signature)
        {
            token = null;
            signature = null;
            if (!TryCollectionToken(receiver, out token)) return false;

            var clr = CollectionArities[receiver.Name].ClrName;
            if (CollectionMembers.TryGetValue(clr, out var members)
                && members.TryGetValue(member, out signature))
            {
                return true;
            }

            throw new ForeignFeatureException(
                $"MSIL: '{receiver.Name}.{member}' is outside the supported collection surface. "
                + "MSIL carries List and Dictionary natively, but only the members whose IL "
                + "generic signatures are recorded (Add, Count, Contains/ContainsKey, and the "
                + "indexer). Emitting a guessed signature produces a call that assembles and "
                + "then fails with MissingMethodException at run time, so it is refused here "
                + "instead. Add a row to MSILCodeGenerator.CollectionMembers plus a round-trip "
                + "test to widen the set.");
        }

        /// <summary>
        /// The token naming the type a member is called ON, for <c>callvirt</c>/<c>call</c>/
        /// <c>newobj</c>.
        ///
        /// <para>Three shapes, and the difference is not cosmetic. A generic needs the
        /// <c>class</c> prefix (<c>class [mscorlib]…List`1&lt;string&gt;</c>) — the existing
        /// indexer emission already spells it that way. A non-generic BCL type is written bare
        /// (<c>[mscorlib]System.String</c>), matching the <c>Object::ToString</c> call this file
        /// has always emitted. A type in the assembly being generated is just its name.</para>
        ///
        /// <para>Before this, the receiver was <c>SanitizeName(type.Name)</c> unconditionally,
        /// so <c>s.ToUpper()</c> emitted a call on a class literally named <c>String</c> and
        /// <c>l.Add(x)</c> one on <c>List</c> — neither of which exists. One missing resolution
        /// step, two symptoms.</para>
        /// </summary>
        private string IlReceiverToken(TypeInfo type)
        {
            if (type?.Name == null) return "[mscorlib]System.Object";

            if (TryCollectionToken(type, out var collection)) return "class " + collection;

            var mapped = MapTypeName(type.Name);
            return PrimitiveTokens.TryGetValue(mapped, out var bcl) ? bcl : mapped;
        }

        /// <summary>
        /// The collections <c>ForeignFeatureChecker.IsCollectionName</c> recognises, with the
        /// CLR type each denotes. Kept beside <see cref="TryCollectionToken"/> so the set the
        /// checker ADMITS and the set this backend can NAME cannot drift apart — a type allowed
        /// through the gate with no entry here would reach ilasm as a bare name.
        /// </summary>
        private static readonly Dictionary<string, (string ClrName, int Arity)> CollectionArities =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["List"] = ("System.Collections.Generic.List", 1),
                ["HashSet"] = ("System.Collections.Generic.HashSet", 1),
                ["Queue"] = ("System.Collections.Generic.Queue", 1),
                ["Stack"] = ("System.Collections.Generic.Stack", 1),
                ["Dictionary"] = ("System.Collections.Generic.Dictionary", 2),
            };

        /// <summary>The IL keywords <see cref="MapTypeName"/> can produce — anything else is a class.</summary>
        private static readonly HashSet<string> IlPrimitives = new(StringComparer.Ordinal)
        {
            "int8", "uint8", "int16", "uint16", "int32", "uint32", "int64", "uint64",
            "float32", "float64", "bool", "char", "string", "object", "void", "native int",
        };

        /// <summary>
        /// True for an IL keyword that denotes a VALUE type — the ones that need boxing to sit in
        /// a reference slot. ⚠ NOT the same as <see cref="IlPrimitives"/>: that set carries
        /// <c>string</c>, <c>object</c> and <c>void</c> too, which are not value types, and using
        /// it as a boxing test silently never fires.
        /// </summary>
        private static bool IsIlValueType(string spec) =>
            IlPrimitives.Contains(spec) && spec != "string" && spec != "object" && spec != "void";

        /// <summary>
        /// IL keyword → the BCL type it denotes, for token positions. <c>string</c> and
        /// <c>object</c> are reference types and never need boxing, but they DO need a real
        /// token for <c>newarr</c>/<c>castclass</c>, so they are carried here too.
        /// </summary>
        private static readonly Dictionary<string, string> PrimitiveTokens = new(StringComparer.Ordinal)
        {
            ["int8"] = "[mscorlib]System.SByte",
            ["uint8"] = "[mscorlib]System.Byte",
            ["int16"] = "[mscorlib]System.Int16",
            ["uint16"] = "[mscorlib]System.UInt16",
            ["int32"] = "[mscorlib]System.Int32",
            ["uint32"] = "[mscorlib]System.UInt32",
            ["int64"] = "[mscorlib]System.Int64",
            ["uint64"] = "[mscorlib]System.UInt64",
            ["float32"] = "[mscorlib]System.Single",
            ["float64"] = "[mscorlib]System.Double",
            ["bool"] = "[mscorlib]System.Boolean",
            ["char"] = "[mscorlib]System.Char",
            ["string"] = "[mscorlib]System.String",
            ["object"] = "[mscorlib]System.Object",
        };

        private string MapTypeName(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return "object";

            // ⛔ ALREADY an IL spelling — return it untouched. This path is reached: something
            // maps a type and then feeds the result back in, which was a silent no-op only while
            // the fallback below was the identity. Once that fallback quotes, `int32` came back as
            // `'int32'` and `IlTypeSpec` no longer recognized it as a primitive, so
            // `Dim n As Integer` declared `[0] class 'int32' 'n'` — a local of a class that does
            // not exist.
            if (IlPrimitives.Contains(typeName)) return typeName;

            switch (typeName.ToLowerInvariant())
            {
                case "integer": return "int32";
                case "long": return "int64";
                case "single": return "float32";
                case "double": return "float64";
                case "string": return "string";
                case "boolean": return "bool";
                case "byte": return "uint8";
                case "short": return "int16";
                case "object": return "object";
                case "void": return "void";
            }

            // A recognized .NET exception is a BCL type and must be spelled with its assembly, in
            // EVERY position — the local that holds it, the `newobj` that builds it, a parameter
            // that carries it. Resolving it here rather than at each site is what keeps those in
            // agreement: `Catch ex As Exception` used to declare `[0] class Exception` and
            // `Throw New Exception(m)` used to emit `newobj instance void Exception::.ctor(string)`,
            // both naming a class in no assembly, and ilasm refused the whole file with
            // "Reference to undefined class 'Exception'".
            if (CppExceptionTypes.TryGetNetFullName(typeName, out var exceptionFullName))
                return "[mscorlib]" + exceptionFullName;

            return SanitizeName(typeName);
        }

        private void GenerateInterface(IRInterface irInterface)
        {
            var interfaceName = SanitizeName(irInterface.Name);

            // Build implements list
            var implements = "";
            if (irInterface.BaseInterfaces.Count > 0)
            {
                implements = " implements " + string.Join(", ", irInterface.BaseInterfaces.Select(b => SanitizeName(b)));
            }

            WriteLine($".class interface public abstract auto ansi {interfaceName}{implements}");
            WriteLine("{");

            // Interface methods
            foreach (var method in irInterface.Methods)
            {
                var returnType = IlTypeSpec(method.ReturnType);
                var methodName = SanitizeName(method.Name);
                var paramTypes = string.Join(", ", method.Parameters.Select(IlParameterSpec));

                WriteLine("  .method public hidebysig newslot abstract virtual");
                WriteLine($"          instance {returnType} {methodName}({paramTypes}) cil managed");
                WriteLine("  {");
                WriteLine("  } // end of method " + methodName);
                WriteLine();
            }

            // Interface properties
            foreach (var prop in irInterface.Properties)
            {
                var propType = IlTypeSpec(prop.Type);
                var propRaw = RawName(prop.Name);
                var propName = IlName(propRaw);
                var getter = $"get_{propRaw}";
                var setter = $"set_{propRaw}";

                WriteLine($"  .property instance {propType} {propName}()");
                WriteLine("  {");
                if (prop.HasGetter)
                    WriteLine($"    .get instance {propType} {interfaceName}::{getter}()");
                if (prop.HasSetter)
                    WriteLine($"    .set instance void {interfaceName}::{setter}({propType})");
                WriteLine("  }");
                WriteLine();

                // Getter method
                if (prop.HasGetter)
                {
                    WriteLine("  .method public hidebysig newslot specialname abstract virtual");
                    WriteLine($"          instance {propType} {getter}() cil managed");
                    WriteLine("  {");
                    WriteLine("  }");
                }

                // Setter method
                if (prop.HasSetter)
                {
                    WriteLine("  .method public hidebysig newslot specialname abstract virtual");
                    WriteLine($"          instance void {setter}({propType} 'value') cil managed");
                    WriteLine("  {");
                    WriteLine("  }");
                }
            }

            WriteLine($"}} // end of interface {interfaceName}");
        }

        /// <summary>
        /// An explicit implementation stub for an interface accessor whose implementation this
        /// class INHERITS: <c>Holder</c> lists <c>IHolder</c>, and only <c>BaseHolder</c>
        /// declares <c>Slot</c>. The stub is private, carries <c>.override</c> naming the
        /// interface slot, and forwards to the base accessor with a non-virtual <c>call</c> —
        /// the shape the C# compiler emits when a base-class member implements a derived class's
        /// interface.
        ///
        /// <para>⛔ WITHOUT IT THE TYPE DID NOT LOAD: TypeLoadException "Method 'get_Slot' in type
        /// 'Holder' ... does not have an implementation", because the base's accessor is not
        /// virtual and so cannot fill the slot. Making the BASE accessor virtual instead was
        /// rejected: it changes the base's own dispatch, which a same-named member in a further
        /// derived class would then override rather than shadow.</para>
        /// </summary>
        private void GenerateInheritedAccessorStub(InterfaceImplementationLookup.InheritedAccessor inherited)
        {
            var propType = IlTypeSpec(inherited.InterfaceProperty.Type);
            var iface = SanitizeName(inherited.Interface.Name);
            var owner = SanitizeName(inherited.DeclaringClass.Name);
            var slotRaw = (inherited.Getter ? "get_" : "set_") + RawName(inherited.InterfaceProperty.Name);
            var baseRaw = (inherited.Getter ? "get_" : "set_") + RawName(inherited.InheritedProperty.Name);
            var stubName = IlName($"{RawName(inherited.Interface.Name)}.{slotRaw}");

            WriteLine("  .method private hidebysig newslot virtual final specialname");
            WriteLine(inherited.Getter
                ? $"          instance {propType} {stubName}() cil managed"
                : $"          instance void {stubName}({propType} 'value') cil managed");
            WriteLine("  {");
            WriteLine($"    .override {iface}::{slotRaw}");
            WriteLine("    .maxstack 8");
            WriteLine("    ldarg.0");
            if (inherited.Getter)
            {
                WriteLine($"    call instance {propType} {owner}::{baseRaw}()");
            }
            else
            {
                WriteLine("    ldarg.1");
                WriteLine($"    call instance void {owner}::{baseRaw}({propType})");
            }
            WriteLine("    ret");
            WriteLine($"  }} // end of method {slotRaw} (inherited implementation)");
            WriteLine();
        }

        private void GenerateUserClass(IRClass irClass)
        {
            _currentClass = irClass;
            var className = SanitizeName(irClass.Name);

            // Build extends and implements. The base goes through IlTypeToken, not SanitizeName:
            // `Class MyError Inherits Exception` emitted `extends Exception`, naming a class in no
            // assembly, and ilasm refused the file. A base class defined in this compilation still
            // resolves to its bare sanitized name.
            var extends = "[mscorlib]System.Object";
            if (!string.IsNullOrEmpty(irClass.BaseClass))
            {
                extends = IlTypeToken(irClass.BaseClass);
            }

            var implements = "";
            if (irClass.Interfaces.Count > 0)
            {
                implements = " implements " + string.Join(", ", irClass.Interfaces.Select(i => SanitizeName(i)));
            }

            // Same rule as the module class: `beforefieldinit` is dropped when a type initializer
            // exists, as the C# compiler does for a class with a static constructor.
            var needsInitializer = irClass.Fields.Any(NeedsStaticFieldInitialization);
            WriteLine($".class public auto ansi{(needsInitializer ? "" : " beforefieldinit")} {className}");
            WriteLine($"       extends {extends}{implements}");
            WriteLine("{");

            // Fields
            foreach (var field in irClass.Fields)
            {
                var access = MapAccessModifier(field.Access);
                var staticMod = field.IsStatic ? "static " : "";
                // IlTypeSpec, not MapType: a field declaration is a type SPEC position, so a class
                // needs its `class` prefix and an array needs its element mapped. `Public Cells(3)
                // As Integer` emitted `.field public Integer[] Cells` — the BasicLang name, which
                // ilasm rejects outright ("syntax error at token 'Integer'").
                var fieldType = IlTypeSpec(field.Type);
                var fieldName = SanitizeName(field.Name);
                WriteLine($"  .field {access} {staticMod}{fieldType} {fieldName}");
            }

            if (irClass.Fields.Count > 0)
                WriteLine();

            // Events
            foreach (var evt in irClass.Events)
            {
                GenerateEvent(irClass, evt);
            }

            // Properties
            foreach (var prop in irClass.Properties)
            {
                GenerateProperty(irClass, prop);
            }

            // Interface accessors this class takes on through its own Implements list but
            // inherits the implementation of — see GenerateInheritedAccessorStub.
            foreach (var inherited in InterfaceImplementationLookup.InheritedInterfaceAccessors(_module, irClass))
            {
                GenerateInheritedAccessorStub(inherited);
            }

            // Constructors
            foreach (var ctor in irClass.Constructors)
            {
                GenerateConstructor(irClass, ctor);
            }

            // Default constructor if none defined
            if (irClass.Constructors.Count == 0)
            {
                GenerateDefaultCtorForClass(irClass);
            }

            // Methods
            foreach (var method in irClass.Methods)
            {
                GenerateClassMethod(irClass, method);
            }

            if (needsInitializer)
            {
                GenerateClassStaticConstructor(irClass, className);
            }

            WriteLine($"}} // end of class {className}");
            _currentClass = null;
        }

        private string MapAccessModifier(AccessModifier access)
        {
            return access switch
            {
                AccessModifier.Public => "public",
                AccessModifier.Private => "private",
                AccessModifier.Protected => "family",
                AccessModifier.Friend => "assembly",
                _ => "private"
            };
        }

        private void GenerateEvent(IRClass irClass, IREvent evt)
        {
            var delegateType = SanitizeName(evt.DelegateType);
            var eventRaw = RawName(evt.Name);
            var eventName = IlName(eventRaw);
            var adder = $"add_{eventRaw}";
            var remover = $"remove_{eventRaw}";
            var staticMod = evt.IsStatic ? "static " : "";

            // Backing field
            WriteLine($"  .field private {staticMod}class {delegateType} {eventName}");

            // Event declaration
            WriteLine($"  .event class {delegateType} {eventName}");
            WriteLine("  {");
            WriteLine($"    .addon instance void {SanitizeName(irClass.Name)}::{adder}(class {delegateType})");
            WriteLine($"    .removeon instance void {SanitizeName(irClass.Name)}::{remover}(class {delegateType})");
            WriteLine("  }");
            WriteLine();

            // Add method
            WriteLine($"  .method public hidebysig specialname {staticMod}instance void");
            WriteLine($"          {adder}(class {delegateType} 'value') cil managed");
            WriteLine("  {");
            WriteLine("    .maxstack 8");
            if (!evt.IsStatic) WriteLine("    ldarg.0");
            if (!evt.IsStatic) WriteLine($"    ldarg.0");
            if (!evt.IsStatic) WriteLine($"    ldfld class {delegateType} {SanitizeName(irClass.Name)}::{eventName}");
            else WriteLine($"    ldsfld class {delegateType} {SanitizeName(irClass.Name)}::{eventName}");
            WriteLine("    ldarg.1");
            WriteLine($"    call class [mscorlib]System.Delegate [mscorlib]System.Delegate::Combine(class [mscorlib]System.Delegate, class [mscorlib]System.Delegate)");
            WriteLine($"    castclass {delegateType}");
            if (!evt.IsStatic) WriteLine($"    stfld class {delegateType} {SanitizeName(irClass.Name)}::{eventName}");
            else WriteLine($"    stsfld class {delegateType} {SanitizeName(irClass.Name)}::{eventName}");
            WriteLine("    ret");
            WriteLine("  }");
            WriteLine();

            // Remove method
            WriteLine($"  .method public hidebysig specialname {staticMod}instance void");
            WriteLine($"          {remover}(class {delegateType} 'value') cil managed");
            WriteLine("  {");
            WriteLine("    .maxstack 8");
            if (!evt.IsStatic) WriteLine("    ldarg.0");
            if (!evt.IsStatic) WriteLine($"    ldarg.0");
            if (!evt.IsStatic) WriteLine($"    ldfld class {delegateType} {SanitizeName(irClass.Name)}::{eventName}");
            else WriteLine($"    ldsfld class {delegateType} {SanitizeName(irClass.Name)}::{eventName}");
            WriteLine("    ldarg.1");
            WriteLine($"    call class [mscorlib]System.Delegate [mscorlib]System.Delegate::Remove(class [mscorlib]System.Delegate, class [mscorlib]System.Delegate)");
            WriteLine($"    castclass {delegateType}");
            if (!evt.IsStatic) WriteLine($"    stfld class {delegateType} {SanitizeName(irClass.Name)}::{eventName}");
            else WriteLine($"    stsfld class {delegateType} {SanitizeName(irClass.Name)}::{eventName}");
            WriteLine("    ret");
            WriteLine("  }");
            WriteLine();
        }

        /// <summary>
        /// The compiler-generated field an AUTO property stores into, spelled the way the C# and
        /// VB compilers spell it so it reads as generated and cannot collide with a user field.
        ///
        /// <para>⚠ The angle brackets are not an identifier character, which is exactly why the
        /// name is safe — and why it has to be quoted, like every other name this backend
        /// prints.</para>
        /// </summary>
        private static string BackingFieldName(string propRaw) => IlName($"<{propRaw}>k__BackingField");

        /// <summary>True when the IR carries no accessor body, i.e. <c>Public Property X As T</c>.</summary>
        private static bool IsAutoProperty(IRProperty prop) =>
            prop.Getter == null && prop.Setter == null;

        private void GenerateProperty(IRClass irClass, IRProperty prop)
        {
            var propType = IlTypeSpec(prop.Type);
            // ⚠ The property NAME is quoted; the accessor names built from it are not. A composed
            // name always begins with `get_`/`set_` and so can never be an IL keyword, and quoting
            // has to happen around the WHOLE identifier — `get_'Alpha'` is not one.
            var propRaw = RawName(prop.Name);
            var propName = IlName(propRaw);
            var getter = $"get_{propRaw}";
            var setter = $"set_{propRaw}";
            var className = SanitizeName(irClass.Name);
            var staticMod = prop.IsStatic ? "static " : "";
            var instanceMod = prop.IsStatic ? "" : "instance ";

            // ⛔ WITHOUT THIS BOTH ACCESSORS WERE NON-VIRTUAL and the override never dispatched.
            // The CALL SITE was already right — `callvirt instance string Animal::get_Name()` —
            // but callvirt on a non-virtual method binds statically, so reading an overridden
            // property through a base-typed variable answered the BASE's value with no
            // diagnostic at all. Measured; a three-level chain answered the topmost value.
            // Same spelling GenerateMethod uses: an override takes the base's slot, a fresh
            // Overridable declares one. A Shared property is never virtual.
            var virtualMod = prop.IsStatic ? ""
                : prop.IsOverride ? "virtual "
                : prop.IsVirtual ? "newslot virtual "
                : "";

            // ⛔ AN INTERFACE SLOT CAN ONLY BE FILLED BY A VIRTUAL METHOD — the property twin of
            // the GenerateClassMethod arm. An accessor an implemented interface declares was
            // emitted non-virtual, and the type did not load: TypeLoadException "Method
            // 'get_Slot' in type 'Holder' ... does not have an implementation" (measured with an
            // interface property carrying an empty Get block). `newslot virtual final` is what
            // C# emits for an implicit implementation. Decided PER ACCESSOR, on what the
            // interface declares, so a class setter the interface never asked for stays an
            // ordinary method. An Overridable/Overrides property is already virtual and keeps
            // its own spelling.
            var getterVirtualMod = virtualMod.Length == 0 && !prop.IsStatic
                && InterfaceImplementationLookup.ImplementsInterfaceAccessor(_module, irClass, prop.Name, getter: true)
                ? "newslot virtual final " : virtualMod;
            var setterVirtualMod = virtualMod.Length == 0 && !prop.IsStatic
                && InterfaceImplementationLookup.ImplementsInterfaceAccessor(_module, irClass, prop.Name, getter: false)
                ? "newslot virtual final " : virtualMod;

            // ⛔ WHAT WILL ACTUALLY BE EMITTED, computed once and used for both the `.property`
            // block and the methods. They used to disagree: the block was written
            // unconditionally while each method was gated on `prop.Getter != null`, so an AUTO
            // property — which carries neither accessor in the IR — declared `.get`/`.set`
            // naming methods that were never emitted, and ilasm refused the whole file with
            // "Invalid Set method of property 'Alpha'". No property assembled, in any shape.
            var isAuto = IsAutoProperty(prop);
            var emitGetter = !prop.IsWriteOnly && (prop.Getter != null || isAuto);
            var emitSetter = !prop.IsReadOnly && (prop.Setter != null || isAuto);
            var backingField = BackingFieldName(propRaw);

            // ⚠ An auto property has nowhere to put the value. Nothing declared storage for it
            // before, so even with the `.property` block removed by hand the program died with
            // `MissingFieldException: Field not found: 'Box.Alpha'`.
            if (isAuto)
            {
                WriteLine($"  .field private {staticMod}{propType} {backingField}");
                WriteLine();
            }

            // Property declaration
            WriteLine($"  .property {instanceMod}{propType} {propName}()");
            WriteLine("  {");
            if (emitGetter)
                WriteLine($"    .get {instanceMod}{propType} {className}::{getter}()");
            if (emitSetter)
                WriteLine($"    .set {instanceMod}void {className}::{setter}({propType})");
            WriteLine("  }");
            WriteLine();

            // Synthesized accessors over the backing field, for an auto property.
            if (isAuto)
            {
                if (emitGetter)
                {
                    WriteLine($"  .method public hidebysig specialname {getterVirtualMod}{staticMod}");
                    WriteLine($"          {instanceMod}{propType} {getter}() cil managed");
                    WriteLine("  {");
                    WriteLine("    .maxstack 8");
                    if (prop.IsStatic)
                    {
                        WriteLine($"    ldsfld {propType} {className}::{backingField}");
                    }
                    else
                    {
                        WriteLine("    ldarg.0");
                        WriteLine($"    ldfld {propType} {className}::{backingField}");
                    }
                    WriteLine("    ret");
                    WriteLine($"  }} // end of method {getter}");
                    WriteLine();
                }

                if (emitSetter)
                {
                    WriteLine($"  .method public hidebysig specialname {setterVirtualMod}{staticMod}");
                    WriteLine($"          {instanceMod}void {setter}({propType} 'value') cil managed");
                    WriteLine("  {");
                    WriteLine("    .maxstack 8");
                    if (prop.IsStatic)
                    {
                        // ⚠ ldarg.0 is the VALUE in a static setter — there is no `Me` to skip.
                        WriteLine("    ldarg.0");
                        WriteLine($"    stsfld {propType} {className}::{backingField}");
                    }
                    else
                    {
                        WriteLine("    ldarg.0");
                        WriteLine("    ldarg.1");
                        WriteLine($"    stfld {propType} {className}::{backingField}");
                    }
                    WriteLine("    ret");
                    WriteLine($"  }} // end of method {setter}");
                    WriteLine();
                }

                return;
            }

            // Getter
            if (prop.Getter != null && !prop.IsWriteOnly)
            {
                WriteLine($"  .method public hidebysig specialname {getterVirtualMod}{staticMod}");
                WriteLine($"          {instanceMod}{propType} {getter}() cil managed");
                WriteLine("  {");
                EmitAccessorBody(prop.Getter, irClass, prop.IsStatic, closeWithRet: false);
                WriteLine($"  }} // end of method {getter}");
                WriteLine();
            }

            // Setter
            if (prop.Setter != null && !prop.IsReadOnly)
            {
                WriteLine($"  .method public hidebysig specialname {setterVirtualMod}{staticMod}");
                WriteLine($"          {instanceMod}void {setter}({propType} 'value') cil managed");
                WriteLine("  {");
                EmitAccessorBody(prop.Setter, irClass, prop.IsStatic, closeWithRet: true);
                WriteLine($"  }} // end of method {setter}");
                WriteLine();
            }
        }

        /// <summary>
        /// One explicit accessor's body, in the SAME order an ordinary method's body is written:
        /// establish the method context first, then <c>.maxstack</c>, then <c>.locals init</c>,
        /// then the code.
        ///
        /// <para>⛔ The order is the whole point. This used to write <c>.maxstack 8</c> BEFORE
        /// <c>InitializeMethodContext</c> and never declare locals at all, so
        /// <c>Set(value As Integer)</c> emitted <c>stloc.0</c> into a method with no locals
        /// directive and the CLR answered <b>InvalidProgramException</b>. The slot tables do not
        /// exist until the context is initialized, which is why the fix is a reordering rather
        /// than an added line.</para>
        ///
        /// <para>⚠ <paramref name="closeWithRet"/> for the setter only: a getter's last
        /// instruction is the <c>ret</c> that returns the value, while a setter's body can end on
        /// a store and needs one appended.</para>
        /// </summary>
        private void EmitAccessorBody(IRFunction impl, IRClass irClass, bool isStatic, bool closeWithRet)
        {
            if (impl.EntryBlock == null)
            {
                // No body to lower. A getter still has to leave something on the stack.
                WriteLine("    .maxstack 8");
                WriteLine("    ldnull");
                WriteLine("    ret");
                return;
            }

            _currentFunction = impl;
            InitializeMethodContext(impl, isInstance: !isStatic, owner: irClass);

            _maxStack = Math.Max(8, _localIndices.Count + _tempIndices.Count + 4);
            WriteLine($"    .maxstack {_maxStack}");

            if (_localIndices.Count > 0 || _tempIndices.Count > 0)
            {
                GenerateLocalsDeclaration(impl);
            }

            WriteLine();

            EmitArrayLocalAllocations(impl);

            // _visitedBlocks, not a local set: Visit(IRTryCatch) marks a region's blocks THERE so
            // GenerateBasicBlock will not write them a second time. A local set left the two
            // disagreeing, and a Try inside a class member emitted every region block twice
            // ("Duplicate label").
            _visitedBlocks = new HashSet<BasicBlock>();
            GenerateBasicBlock(impl.EntryBlock, _visitedBlocks, isEntry: true);
            EmitLoweredReturnExit();
            _currentFunction = null;

            if (closeWithRet && !EndsWithRet())
                WriteLine("    ret");
        }

        private void GenerateConstructor(IRClass irClass, IRConstructor ctor)
        {
            var className = SanitizeName(irClass.Name);
            var paramTypes = "";

            if (ctor.Implementation != null)
            {
                paramTypes = string.Join(", ", ctor.Implementation.Parameters.Select(p =>
                    $"{ParamSpec(p)} {SanitizeName(p.Name)}"));
            }

            WriteLine("  .method public hidebysig specialname rtspecialname");
            WriteLine($"          instance void .ctor({paramTypes}) cil managed");
            WriteLine("  {");
            WriteLine("    .maxstack 8");

            // ⛔ A constructor body gets locals like any other method, and this never declared
            // them: `Public Sub New(v As Integer) : N = v` needs a slot to park the value in
            // before `stfld`, and emitting a store against an undeclared slot is an invalid
            // program. The context is initialized here, ahead of the base call, so the tables the
            // declaration is written from are populated.
            if (ctor.Implementation != null)
            {
                _currentFunction = ctor.Implementation;
                InitializeMethodContext(ctor.Implementation, isInstance: true, owner: irClass);
                if (_localIndices.Count > 0 || _tempIndices.Count > 0)
                {
                    GenerateLocalsDeclaration(ctor.Implementation);
                }
            }

            // Call base constructor
            var baseClass = string.IsNullOrEmpty(irClass.BaseClass) ? "[mscorlib]System.Object" : IlTypeToken(irClass.BaseClass);
            WriteLine("    ldarg.0");
            EmitBaseConstructorCall(baseClass, irClass.BaseClass, ctor);

            EmitInstanceFieldInitialization(irClass);

            // Generate constructor body. The context was initialized above, before the locals
            // declaration was written from it — re-initializing here would just rebuild the same
            // tables.
            if (ctor.Implementation?.EntryBlock != null)
            {
                // _visitedBlocks, not a local set: Visit(IRTryCatch) marks a region's blocks THERE so
                    // GenerateBasicBlock will not write them a second time. A local set left the two
                    // disagreeing, and a Try inside a class member emitted every region block twice
                    // ("Duplicate label").
                    _visitedBlocks = new HashSet<BasicBlock>();
                    var visited = _visitedBlocks;
                GenerateBasicBlock(ctor.Implementation.EntryBlock, visited, isEntry: true);
                EmitLoweredReturnExit();
                _currentFunction = null;
            }

            if (!EndsWithRet())
                WriteLine("    ret");

            WriteLine("  } // end of method .ctor");
            WriteLine();
        }

        /// <summary>
        /// The <c>call</c> to the base constructor, with the arguments <c>MyBase.New(…)</c> gave it.
        ///
        /// <para>⛔ They used to be DROPPED. <c>IRConstructor.BaseConstructorArgs</c> was never read
        /// here — the emission was a fixed <c>call instance void Base::.ctor()</c> whatever was
        /// written — so <c>MyBase.New(7, 9)</c> against <c>Sub New(a As Integer, b As Integer)</c>
        /// died with <c>MissingMethodException: Void Base..ctor()</c>. C#, JavaScript and C++ all
        /// pass them; MSIL alone did not.</para>
        ///
        /// <para>⛔ <b>A body-computed argument is REFUSED, not emitted.</b> The base call is
        /// written BEFORE the constructor body, because IL requires it before any field access, so
        /// an argument whose value is produced by an instruction IN that body does not exist yet.
        /// Measured, <c>MyBase.New(v + 1)</c> hands this an <c>IRBinaryOp</c> temp: loading it here
        /// would read an uninitialized local and pass a silent <b>0</b>, which is worse than the
        /// exception it replaces. The same shape does not build on C# either — it emits
        /// <c>: base(t0)</c> naming a temp that is not in scope (CS0103) — so this is an IR-level
        /// gap, not an MSIL one, and refusing keeps MSIL honest about it rather than inventing an
        /// answer.</para>
        ///
        /// <para>⚠ The signature is spelled from the ARGUMENT types, the same way
        /// <see cref="Visit(IRNewObject)"/> spells <c>newobj</c>. That is sound because the IR
        /// builder coerces each base-constructor argument to its declared parameter type; the two
        /// sites share that contract, and spelling them differently is how they would drift.</para>
        /// </summary>
        private void EmitBaseConstructorCall(string baseClass, string baseClassName, IRConstructor ctor)
        {
            // ⚠ No special case for ZERO arguments: the loops below do nothing and the join is
            // empty, so the general path writes exactly `::.ctor()` — the same text the dedicated
            // early return produced. Measured: deleting that early return changed no emitted IL and
            // killed no test, so it was redundancy rather than an untested branch, and it is gone.
            // `BaseConstructorArgs` is created by IRConstructor's own constructor and never
            // reassigned, so there is no null to guard either.
            var args = ctor.BaseConstructorArgs;

            foreach (var arg in args)
            {
                if (IsLoadableBeforeBody(arg)) continue;

                throw new ForeignFeatureException(
                    "MSIL: a MyBase.New argument that is COMPUTED (" + (arg?.Name ?? "?")
                    + ") has no IL lowering. IL requires the base constructor call before the "
                    + "constructor body, so a value the body computes does not exist yet — "
                    + "emitting it would load an uninitialized local and pass 0 silently, which is "
                    + "worse than failing. Pass a parameter or a literal (MyBase.New(v), not "
                    + "MyBase.New(v + 1)); the computed form does not build on C# either "
                    + "(it emits `: base(t0)`, CS0103).");
            }

            foreach (var arg in args)
            {
                EmitLoadValue(arg);
            }

            var paramTypes = DeclaredParamList(DeclaredCtorParams(baseClassName, args.Count), args);
            WriteLine($"    call instance void {baseClass}::.ctor({paramTypes})");
        }

        /// <summary>
        /// Whether <paramref name="value"/> can be pushed at the top of a constructor, before any
        /// of its body has run: a literal, or one of the constructor's own parameters (an
        /// <c>ldarg</c>). Anything else is a temp some instruction has yet to produce.
        /// </summary>
        private static bool IsLoadableBeforeBody(IRValue value) =>
            value is IRConstant || (value is IRVariable variable && variable.IsParameter);

        private void GenerateDefaultCtorForClass(IRClass irClass)
        {
            var baseClass = string.IsNullOrEmpty(irClass.BaseClass) ? "[mscorlib]System.Object" : IlTypeToken(irClass.BaseClass);

            WriteLine("  .method public hidebysig specialname rtspecialname");
            WriteLine("          instance void .ctor() cil managed");
            WriteLine("  {");
            WriteLine("    .maxstack 8");
            WriteLine("    ldarg.0");
            WriteLine($"    call instance void {baseClass}::.ctor()");
            EmitInstanceFieldInitialization(irClass);
            WriteLine("    ret");
            WriteLine("  } // end of method .ctor");
            WriteLine();
        }

        private void GenerateClassMethod(IRClass irClass, IRMethod method)
        {
            var className = SanitizeName(irClass.Name);
            var methodName = SanitizeName(method.Name);
            var returnType = IlTypeSpec(method.ReturnType);
            var staticMod = method.IsStatic ? "static " : "";
            var instanceMod = method.IsStatic ? "" : "instance ";

            // Handle virtual/override/abstract/sealed modifiers properly
            var modifiers = "";
            if (method.IsAbstract)
            {
                modifiers = "abstract virtual ";
            }
            else if (method.IsOverride && method.IsSealed)
            {
                modifiers = "final virtual ";
            }
            else if (method.IsOverride)
            {
                modifiers = "virtual ";
            }
            else if (method.IsVirtual)
            {
                modifiers = "newslot virtual ";
            }
            else if (!method.IsStatic && ImplementsInterfaceMember(_currentClass, method.Name))
            {
                // `final` because nothing in the language marks this Overridable — it fills the
                // interface slot without opening itself to further overriding, which is exactly
                // what C# emits for an implicit interface implementation.
                modifiers = "newslot virtual final ";
            }

            var paramTypes = "";
            if (method.Implementation != null)
            {
                paramTypes = string.Join(", ", method.Implementation.Parameters.Select(p =>
                    $"{ParamSpec(p)} {SanitizeName(p.Name)}"));
            }

            WriteLine($"  .method public hidebysig {modifiers}{staticMod}");
            WriteLine($"          {instanceMod}{returnType} {methodName}({paramTypes}) cil managed");
            WriteLine("  {");

            if (method.Implementation != null && !method.IsAbstract)
            {
                _currentFunction = method.Implementation;
                InitializeMethodContext(method.Implementation, isInstance: !method.IsStatic, owner: irClass);

                // Calculate max stack
                _maxStack = Math.Max(8, _localIndices.Count + _tempIndices.Count + 4);
                WriteLine($"    .maxstack {_maxStack}");

                // Declare locals
                if (_localIndices.Count > 0 || _tempIndices.Count > 0)
                {
                    GenerateLocalsDeclaration(method.Implementation);
                }

                WriteLine();

                // Generate body
                if (method.Implementation.EntryBlock != null)
                {
                    // _visitedBlocks, not a local set: Visit(IRTryCatch) marks a region's blocks THERE so
                    // GenerateBasicBlock will not write them a second time. A local set left the two
                    // disagreeing, and a Try inside a class member emitted every region block twice
                    // ("Duplicate label").
                    _visitedBlocks = new HashSet<BasicBlock>();
                    var visited = _visitedBlocks;
                    GenerateBasicBlock(method.Implementation.EntryBlock, visited, isEntry: true);
                    EmitLoweredReturnExit();
                }

                _currentFunction = null;
            }

            // Ensure return for non-abstract methods
            if (!method.IsAbstract && returnType == "void" && !EndsWithRet())
            {
                WriteLine("    ret");
            }

            WriteLine($"  }} // end of method {methodName}");
            WriteLine();
        }

        /// <summary>
        /// Per-method setup for a CLASS member (method, constructor, property accessor).
        ///
        /// <para><paramref name="isInstance"/> is the whole point: an instance member is handed
        /// <c>Me</c> in argument slot 0, so its first declared parameter is <c>ldarg.1</c>. Passing
        /// false reproduces the previous numbering exactly, which is why static members and
        /// module-level functions are untouched by this.</para>
        /// </summary>
        private void InitializeMethodContext(IRFunction function, bool isInstance = false, IRClass owner = null)
        {
            _localIndices.Clear();
            _paramIndices.Clear();
            _byRefParams.Clear();
            _byRefStoreScratch.Clear();
            _tempIndices.Clear();
            _tempNameIndices.Clear();
            _declaredIdentifiers.Clear();
            _localCounter = 0;
            _maxStack = 8;
            _currentStack = 0;

            // The same per-method reset GenerateMethod does. Without it a Try inside a class
            // member would find no exception-handling state prepared for it.
            _syntheticLocals.Clear();
            _foreachSlots.Clear();
            _foreachContinueLabels.Clear();
            _consumedBlocks.Clear();
            _regionBlocks = null;
            _regionIsFinally = false;
            _regionIsCatch = false;
            _regionLeaveTarget = null;
            _methodExitResultLocal = -1;
            _methodExitUsed = false;
            _methodExitLabel = $"eh_exit_{_labelCounter++}";

            _currentMethodIsInstance = isInstance;
            _currentClassFields.Clear();
            _currentClassProperties.Clear();
            _currentClassFieldOwner.Clear();
            _fieldStoreScratch.Clear();
            _currentClassToken = owner != null ? SanitizeName(owner.Name) : null;
            _currentClassOwner = owner;

            // The class's own members AND its bases', each recorded with the class that declares
            // it. Own first and first-wins, so a member shadows a base's of the same name.
            var seenClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var declaring = owner; declaring != null; )
            {
                if (!seenClasses.Add(declaring.Name)) break;

                foreach (var ownProp in declaring.Properties ?? new List<IRProperty>())
                {
                    if (ownProp?.Name == null || _currentClassProperties.ContainsKey(ownProp.Name)) continue;
                    _currentClassProperties[ownProp.Name] = ownProp;

                    // Same reason the field loop does it: this is what routes an assignment's
                    // destination through the store path instead of into a dropped temporary.
                    _declaredIdentifiers.Add(ownProp.Name);
                }

                if (isInstance)
                {
                    foreach (var field in declaring.Fields ?? new List<IRField>())
                    {
                        if (field == null || field.IsStatic || string.IsNullOrEmpty(field.Name)
                            || _currentClassFields.ContainsKey(field.Name)) continue;
                        _currentClassFields[field.Name] = field.Type;
                        _currentClassFieldOwner[field.Name] = SanitizeName(declaring.Name);

                        // Register the field as a name the body can resolve. This is what routes an
                        // assignment's destination through EmitStoreLocal rather than into a temp,
                        // where `N = N + 1` used to silently land and be dropped.
                        _declaredIdentifiers.Add(field.Name);
                    }
                }

                if (string.IsNullOrEmpty(declaring.BaseClass)) break;
                if (!TryFindClass(declaring.BaseClass, out declaring)) break;
            }

            // ⛔ `Me` occupies slot 0, so the first DECLARED parameter is slot 1. Numbering from 0
            // made every parameter read the argument before it — the first one reading the object
            // reference itself, which is an integer-shaped wrong answer, not a crash.
            var argumentSlot = isInstance ? 1 : 0;
            foreach (var param in function.Parameters)
            {
                _declaredIdentifiers.Add(param.Name);
                _paramIndices[param.Name] = argumentSlot++;
            }
            RegisterByRefParameters(function);

            foreach (var local in function.LocalVariables)
            {
                _declaredIdentifiers.Add(local.Name);
                _localIndices[local.Name] = _localIndices.Count;
            }

            RegisterStaticFields(owner);
            RegisterModuleGlobals();

            AllocateExceptionHandlingLocals(function);
            AllocateForEachLocals(function);
            AllocateFieldStoreScratch(function);
            AllocateByRefStoreScratch(function);
            AllocateTemporaries(function);
        }

        /// <summary>
        /// Reserves one scratch slot per field this method ASSIGNS to.
        ///
        /// <para><c>stfld</c> wants the object reference UNDER the value, but this backend emits a
        /// value and then asks for it to be stored — <see cref="EmitStoreLocal"/> is called with
        /// the value already on the stack and IL has no swap. The scratch slot is how the value
        /// gets out of the way while <c>ldarg.0</c> goes down: park, push <c>Me</c>, re-push,
        /// <c>stfld</c>.</para>
        ///
        /// <para>Only fields actually written get a slot, found by scanning for instructions whose
        /// RESULT is named after a field — that is the shape an assignment takes here, since
        /// <c>N = N + 1</c> lowers to a single binary op named <c>N</c>.</para>
        /// </summary>
        private void AllocateFieldStoreScratch(IRFunction function)
        {
            if (!_currentMethodIsInstance) return;
            if (_currentClassFields.Count == 0 && _currentClassProperties.Count == 0) return;

            foreach (var block in function.Blocks)
            {
                foreach (var instruction in block.Instructions)
                {
                    // THREE shapes reach EmitStoreLocal with a field's name, and neither of the
                    // last two is an IRValue — scanning only for named results missed both:
                    //   `N = N + 1`  a binary op whose RESULT is named N
                    //   `N = v`      an IRAssignment whose TARGET is the variable N
                    //   (and IRStore, whose ADDRESS is the variable, for the same reason)
                    var target = instruction switch
                    {
                        IRAssignment assignment => assignment.Target?.Name,
                        IRStore store when store.Address is IRVariable variable => variable.Name,
                        IRValue value => value.Name,
                        _ => null,
                    };

                    if (string.IsNullOrEmpty(target)) continue;

                    // ⚠ A PROPERTY needs the slot for the same reason a field does: its setter
                    // wants the receiver under the value, and IL has no swap. An instance property
                    // only — a Shared one takes the value alone.
                    TypeInfo fieldType;
                    if (_currentClassFields.TryGetValue(target, out var directType))
                    {
                        fieldType = directType;
                    }
                    else if (_currentClassProperties.TryGetValue(target, out var scratchProp)
                             && !scratchProp.IsStatic)
                    {
                        fieldType = scratchProp.Type;
                    }
                    else
                    {
                        continue;
                    }

                    if (_fieldStoreScratch.ContainsKey(target)) continue;

                    var index = _localIndices.Count;
                    var name = $"fld_scratch_{RawName(target)}";
                    _localIndices[name] = index;
                    _fieldStoreScratch[target] = index;
                    _syntheticLocals.Add((index, IlTypeSpec(fieldType), name));
                }
            }
        }

        /// <summary>
        /// Records which of <paramref name="function"/>'s parameters are ByRef, so the signature
        /// can carry <c>&amp;</c> and every read/write through one can be an indirection.
        ///
        /// <para>⚠ Called AFTER <see cref="_paramIndices"/> is filled and keyed the same way, so a
        /// name is either in both tables or in neither — a ByRef entry with no slot would emit an
        /// <c>ldind</c> with nothing under it.</para>
        /// </summary>
        private void RegisterByRefParameters(IRFunction function)
        {
            foreach (var param in function.Parameters ?? new List<IRVariable>())
            {
                if (param?.Name == null || !param.IsByRef) continue;
                _byRefParams[param.Name] = param.Type;
            }
        }

        /// <summary>
        /// Reserves one scratch slot per ByRef parameter this method WRITES THROUGH — the exact
        /// twin of <see cref="AllocateFieldStoreScratch"/>, scanning the same three instruction
        /// shapes for the same reason.
        ///
        /// <para>A ByRef parameter that is only READ gets no slot, so a program that never writes
        /// one emits not a byte more than before.</para>
        /// </summary>
        private void AllocateByRefStoreScratch(IRFunction function)
        {
            if (_byRefParams.Count == 0) return;

            foreach (var block in function.Blocks ?? new List<BasicBlock>())
            {
                foreach (var instruction in block.Instructions)
                {
                    // The same THREE shapes AllocateFieldStoreScratch names: a binary op whose
                    // RESULT is the name, an IRAssignment's TARGET, and an IRStore's ADDRESS.
                    var target = instruction switch
                    {
                        IRAssignment assignment => assignment.Target?.Name,
                        IRStore store when store.Address is IRVariable variable => variable.Name,
                        IRValue value => value.Name,
                        _ => null,
                    };

                    if (string.IsNullOrEmpty(target)) continue;
                    if (!_byRefParams.TryGetValue(target, out var pointee)) continue;
                    if (_byRefStoreScratch.ContainsKey(target)) continue;

                    var index = _localIndices.Count;
                    var name = $"byref_scratch_{RawName(target)}";
                    _localIndices[name] = index;
                    _byRefStoreScratch[target] = index;
                    _syntheticLocals.Add((index, IlTypeSpec(pointee), name));
                }
            }
        }

        /// <summary>
        /// One parameter as the signature spells it. The <c>&amp;</c> is the whole of ByRef in IL:
        /// the argument slot holds a managed pointer instead of a copy.
        ///
        /// <para>⛔ Every site that spells a signature must agree — the declaration, the call, and
        /// <see cref="DeclaredParamList"/>. ilasm does not resolve member references, so a call
        /// that spells <c>(int32)</c> against a method declared <c>(int32&amp;)</c> assembles
        /// cleanly and dies at RUN time with MissingMethodException, the same trap
        /// <see cref="DeclaredParamList"/> already documents for declared types.</para>
        /// </summary>
        private string ParamSpec(IRVariable parameter) =>
            IlTypeSpec(parameter?.Type) + (parameter != null && parameter.IsByRef ? "&" : "");

        private void GenerateHeader(IRModule module)
        {
            WriteLine("// MSIL Assembly generated by BasicLang Compiler");
            WriteLine($"// Module: {module.Name}");
            WriteLine();
            WriteLine(".assembly extern mscorlib");
            WriteLine("{");
            WriteLine("  .publickeytoken = (B7 7A 5C 56 19 34 E0 89)");
            WriteLine("  .ver 4:0:0:0");
            WriteLine("}");
            WriteLine();
            WriteLine($".assembly {SanitizeName(module.Name)}");
            WriteLine("{");
            WriteLine("  .ver 1:0:0:0");
            WriteLine("}");
            WriteLine();
            // ⚠ NOT quoted. The argument is a file name rather than an identifier, and the
            // module name is a compiler constant ("Combined" from the driver, "MsilProbe" from the
            // test harness) that no program can choose — so there is nothing here to collide.
            WriteLine($".module {RawName(module.Name)}.exe");
            WriteLine();
        }

        private void GenerateClass(IRModule module)
        {
            _moduleName = SanitizeName(module.Name);

            var needsInitializer = _moduleGlobals.Values.Any(NeedsStaticInitialization);

            // ⚠ `beforefieldinit` is DROPPED when there is a type initializer, exactly as the C#
            // compiler drops it for a class with a static constructor. With the flag set the CLR
            // may run .cctor at any point at or before the first static-field access; without it
            // the first access is the guaranteed trigger. The relaxed form is not wrong for the
            // code we emit, but the strict form is the one whose ordering is observable and
            // testable, and it costs nothing here.
            var flags = needsInitializer ? "" : " beforefieldinit";
            WriteLine($".class public auto ansi{flags} {_moduleName}");
            WriteLine("       extends [mscorlib]System.Object");
            WriteLine("{");

            GenerateGlobalFields();

            // Generate methods
            foreach (var function in module.Functions)
            {
                if (!function.IsExternal && !IsClassMember(function, module))
                {
                    GenerateMethod(function);
                    WriteLine();
                }
            }

            if (needsInitializer)
            {
                GenerateStaticConstructor();
                WriteLine();
            }

            // Generate default constructor
            GenerateDefaultConstructor();

            WriteLine("} // end of class " + _moduleName);
        }

        /// <summary>
        /// True when this <see cref="IRFunction"/> is some class's member body rather than a
        /// standalone module procedure.
        ///
        /// <para>⛔ <c>IRModule.Functions</c> holds EVERY function the builder made, class members
        /// included: <c>IRBuilder</c> does <c>member.Accept(this)</c> — which appends to
        /// <c>Functions</c> — and then stores that SAME object as <c>IRMethod.Implementation</c>.
        /// Without this filter the module class emitted a second, static copy of every method on
        /// every class. Mostly dead IL, with two live consequences: two classes declaring a
        /// same-named method flattened onto one class and ilasm refused the file outright
        /// ("Duplicate method declaration"), and an unqualified sibling call to a <c>Shared</c>
        /// method resolved to the DUPLICATE, so it printed the right answer for the wrong
        /// reason.</para>
        ///
        /// <para>Reference identity, not name matching, and the same predicate
        /// <c>CSharpBackend.IsClassMethod</c> uses — the two must not disagree about what a
        /// standalone function is.</para>
        /// </summary>
        private static bool IsClassMember(IRFunction function, IRModule module)
        {
            if (module?.Classes == null) return false;

            foreach (var irClass in module.Classes.Values)
            {
                if (irClass.Methods.Any(m => m.Implementation == function)) return true;
                if (irClass.Constructors.Any(c => c.Implementation == function)) return true;
                if (irClass.Properties.Any(p => p.Getter == function || p.Setter == function)) return true;
            }

            return false;
        }

        /// <summary>
        /// Records the module's global variables so every later emission — fields, the type
        /// initializer, and every read and write in every method body — reads one table.
        /// </summary>
        private void CollectModuleGlobals(IRModule module)
        {
            _moduleGlobals.Clear();
            if (module.GlobalVariables == null) return;

            foreach (var global in module.GlobalVariables.Values)
            {
                if (string.IsNullOrEmpty(global?.Name)) continue;

                // ⛔ This table is keyed by bare name, and a second global with the same name used
                // to OVERWRITE the first: one `.field`, and every module's code reading the
                // survivor — `A.GetA()` printed B's value, from a build that reported success.
                // Within one unit the IR builder now names such globals apart (Alpha_Scale /
                // Beta_Scale) so this never fires; two FILES each declaring the name still
                // arrive bare, and that is refused here rather than silently mis-bound.
                if (_moduleGlobals.TryGetValue(global.Name, out var earlier)
                    && !ReferenceEquals(earlier, global))
                {
                    throw new ForeignFeatureException(
                        $"MSIL: module-level variable '{global.Name}' is declared by more than one "
                        + $"module ('{earlier.ModuleName}' and '{global.ModuleName}') across files. "
                        + "This backend keeps one static field per bare name, so the second would "
                        + "silently replace the first; rename one of them.");
                }
                _moduleGlobals[global.Name] = global;
            }
        }

        /// <summary>
        /// One static field per module-level variable.
        ///
        /// <para>⛔ The access modifier is NOT a direct translation of the BasicLang one. A
        /// <c>Private</c> global becomes <c>assembly</c>, not <c>private</c>: IL's <c>private</c>
        /// means "the declaring type only", so a method on a user class reading a module-level
        /// variable — which BasicLang allows, they are in the same file — would be refused by the
        /// CLR with FieldAccessException at the first read. Everything this backend emits lands in
        /// one assembly, so <c>assembly</c> is the narrowest modifier that still admits every
        /// reference the language permits.</para>
        /// </summary>
        private void GenerateGlobalFields()
        {
            if (_moduleGlobals.Count == 0) return;

            WriteLine("  // Module-level variables");
            foreach (var global in _moduleGlobals.Values)
            {
                var access = global.Access == AccessModifier.Public ? "public" : "assembly";
                WriteLine($"  .field {access} static {IlTypeSpec(global.Type)} {SanitizeName(global.Name)}");
            }
            WriteLine();
        }

        /// <summary>
        /// True when a global needs the type initializer to run for it: it has a declared
        /// initializer, or it is a sized array whose storage nothing else creates.
        ///
        /// <para>A global with neither is already correct — the CLR zeroes every static field, and
        /// zero/null/false is exactly what an uninitialized BasicLang variable holds. Emitting a
        /// <c>.cctor</c> for those alone would be dead IL.</para>
        /// </summary>
        private bool NeedsStaticInitialization(IRVariable global) =>
            global.InitialValue != null || TryArrayAllocation(global.Type, out _, out _);

        /// <summary>
        /// The module class's type initializer: declared initializers and sized-array storage for
        /// module-level variables, run once before the first access to any of them.
        ///
        /// <para>⛔ This is where a module-level <c>Dim n As Integer = 7</c>'s initializer has to
        /// go, because it is NOWHERE in the function IR — <c>Main</c>'s instruction list for that
        /// declaration is just <c>t0 = call CStr(@n)</c>. Nothing in a method body ever assigns
        /// the 7. Leaving this out does not produce a build error, it produces a program that
        /// prints 0.</para>
        ///
        /// <para>A sized array global is the same case as a sized array local or field: left bare
        /// it is a null reference and the first <c>g(0) = …</c> throws. <see cref="TryArrayAllocation"/>
        /// is shared with those two sites so all three agree on the length and refuse rank &gt; 1
        /// identically.</para>
        /// </summary>
        private void GenerateStaticConstructor()
        {
            // The initializer is a method: it gets its own stack accounting, and no locals,
            // parameters or temporaries from whatever was generated before it.
            _localIndices.Clear();
            _paramIndices.Clear();
            _byRefParams.Clear();
            _byRefStoreScratch.Clear();
            _tempIndices.Clear();
            _tempNameIndices.Clear();
            _declaredIdentifiers.Clear();
            _maxStack = 8;
            _currentStack = 0;

            WriteLine("  .method private hidebysig specialname rtspecialname");
            WriteLine("          static void .cctor() cil managed");
            WriteLine("  {");
            WriteLine("    .maxstack 8");

            foreach (var global in _moduleGlobals.Values)
            {
                if (global.InitialValue != null)
                {
                    EmitInlineValue(global.InitialValue);
                    EmitNumericCoercion(global.InitialValue, global.Type);
                }
                else if (TryArrayAllocation(global.Type, out var elementToken, out var length))
                {
                    EmitLdcI4(length);
                    _currentStack++;
                    WriteLine($"    newarr {elementToken}");
                }
                else
                {
                    continue;
                }

                WriteLine($"    stsfld {IlTypeSpec(global.Type)} {_moduleName}::{SanitizeName(global.Name)}");
                _currentStack--;
            }

            WriteLine("    ret");
            WriteLine("  } // end of method .cctor");
        }

        /// <summary>
        /// True when a <c>Shared</c> field needs the class's type initializer to run for it.
        ///
        /// <para>Sized-array storage counts, for the same reason it does at module level and on an
        /// instance: left bare the field is a null reference and the first index throws. Rank &gt; 1
        /// is refused by the shared <see cref="TryArrayAllocation"/>, so a Shared rank-2 array is
        /// rejected exactly as a local or module-level one is.</para>
        /// </summary>
        private bool NeedsStaticFieldInitialization(IRField field) =>
            field.IsStatic
            && (field.Initializer != null || TryArrayAllocation(field.Type, out _, out _));

        /// <summary>
        /// A user class's type initializer, for <c>Shared</c> field initializers and sized-array
        /// storage.
        ///
        /// <para>⛔ Without it <c>Public Shared Total As Integer = 5</c> emitted the field and
        /// dropped the 5 — the same shape as the module-level case, and just as silent: the
        /// program runs and reads 0. <see cref="EmitInstanceFieldInitialization"/> deliberately skips
        /// static fields ("a static field is not this instance's to create"), so this is the only
        /// place a Shared array gets storage.</para>
        /// </summary>
        private void GenerateClassStaticConstructor(IRClass irClass, string className)
        {
            _localIndices.Clear();
            _paramIndices.Clear();
            _byRefParams.Clear();
            _byRefStoreScratch.Clear();
            _tempIndices.Clear();
            _tempNameIndices.Clear();
            _declaredIdentifiers.Clear();
            _currentMethodIsInstance = false;
            _currentClassFields.Clear();
            _maxStack = 8;
            _currentStack = 0;

            WriteLine("  .method private hidebysig specialname rtspecialname");
            WriteLine("          static void .cctor() cil managed");
            WriteLine("  {");
            WriteLine("    .maxstack 8");

            foreach (var field in irClass.Fields)
            {
                if (!field.IsStatic) continue;

                if (field.Initializer != null)
                {
                    EmitInlineValue(field.Initializer);
                    EmitNumericCoercion(field.Initializer, field.Type);
                }
                else if (TryArrayAllocation(field.Type, out var elementToken, out var length))
                {
                    EmitLdcI4(length);
                    _currentStack++;
                    WriteLine($"    newarr {elementToken}");
                }
                else
                {
                    continue;
                }

                WriteLine($"    stsfld {IlTypeSpec(field.Type)} {className}::{SanitizeName(field.Name)}");
                _currentStack--;
            }

            WriteLine("    ret");
            WriteLine("  } // end of method .cctor");
        }

        /// <summary>
        /// Widens an integer literal that is initializing a floating-point or 64-bit global.
        ///
        /// <para>⛔ <c>Dim d As Double = 7</c> carries an <c>IRConstant</c> holding a CLR
        /// <c>int</c>, so the initializer emits <c>ldc.i4.7</c> while the field is
        /// <c>float64</c>. IL does NOT coerce on <c>stsfld</c>, and — measured, do not assume
        /// otherwise — nothing rejects the mismatch: ilasm assembles it without a diagnostic and
        /// the JIT runs it. The int32's BIT PATTERN lands in the low half of the float64 slot, so
        /// <c>d</c> holds 3.5E-323 and <c>d + 1.5</c> prints <c>1.5</c>. A missing conversion here
        /// is a silent wrong answer with no crash and no warning comment to find it by, which is
        /// why it is emitted from the field's declared type rather than trusted to the verifier.
        /// Only the literal case is handled because only there is the stack type known for
        /// certain; a computed initializer already carries its operands' type.</para>
        /// </summary>
        private void EmitNumericCoercion(IRValue value, TypeInfo target)
        {
            if (!(value is IRConstant constant)) return;
            if (!(constant.Value is int || constant.Value is long)) return;

            switch (IlTypeSpec(target))
            {
                case "float64": WriteLine("    conv.r8"); break;
                case "float32": WriteLine("    conv.r4"); break;
                case "int64" when constant.Value is int: WriteLine("    conv.i8"); break;
            }
        }

        private void GenerateDefaultConstructor()
        {
            WriteLine("  .method public hidebysig specialname rtspecialname");
            WriteLine("          instance void .ctor() cil managed");
            WriteLine("  {");
            WriteLine("    .maxstack 8");
            WriteLine("    ldarg.0");
            WriteLine("    call instance void [mscorlib]System.Object::.ctor()");
            WriteLine("    ret");
            WriteLine("  } // end of method .ctor");
        }

        private void GenerateMethod(IRFunction function)
        {
            _currentFunction = function;
            _localIndices.Clear();
            _paramIndices.Clear();
            _byRefParams.Clear();
            _byRefStoreScratch.Clear();
            _tempIndices.Clear();
            _tempNameIndices.Clear();
            _declaredIdentifiers.Clear();
            _localCounter = 0;

            // ⛔ A MODULE-level function is not a class member, and the class-member context of
            // whatever class was emitted last must not survive into it: only the three CLASS
            // emission sites call InitializeMethodContext, so these tables were simply left
            // standing. A module function whose body names something the last class also
            // declares then took the bare-FIELD path — `ldarg.0` in a STATIC method, which is
            // not a program the CLR will load (InvalidProgramException). It needed a name
            // collision to show, which is why it survived: `Module G / Public Total` beside a
            // class with a `Total` of its own, measured, before this change and without any
            // inheritance involved.
            _currentMethodIsInstance = false;
            _currentClassFields.Clear();
            _currentClassProperties.Clear();
            _currentClassFieldOwner.Clear();
            _fieldStoreScratch.Clear();
            _currentClassToken = null;
            _currentClassOwner = null;
            _maxStack = 8; // Default, will be calculated
            _currentStack = 0;

            // Exception-handling state is per METHOD: a region, its synthetic locals and its exit
            // label never outlive the method they were emitted for. The same is true of the
            // For Each tables — a slot index means nothing in another method's frame.
            _syntheticLocals.Clear();
            _foreachSlots.Clear();
            _foreachContinueLabels.Clear();
            _consumedBlocks.Clear();
            _regionBlocks = null;
            _regionIsFinally = false;
            _regionIsCatch = false;
            _regionLeaveTarget = null;
            _methodExitResultLocal = -1;
            _methodExitUsed = false;
            _methodExitLabel = $"eh_exit_{_labelCounter++}";

            // Collect declared identifiers
            foreach (var param in function.Parameters)
            {
                _declaredIdentifiers.Add(param.Name);
                _paramIndices[param.Name] = _paramIndices.Count;
            }
            RegisterByRefParameters(function);

            foreach (var local in function.LocalVariables)
            {
                _declaredIdentifiers.Add(local.Name);
                _localIndices[local.Name] = _localIndices.Count;
            }

            RegisterModuleGlobals();

            // Slots for the things a Try needs that IRFunction.LocalVariables does not carry.
            // MUST run before AllocateTemporaries: temp indices continue from _localIndices.Count,
            // and .locals init is written from these tables before any body instruction exists.
            AllocateExceptionHandlingLocals(function);
            AllocateForEachLocals(function);
            AllocateByRefStoreScratch(function);

            // Allocate indices for temporaries
            AllocateTemporaries(function);

            // Generate method signature
            var returnType = IlTypeSpec(function.ReturnType);
            var methodName = SanitizeName(function.Name);
            // ⚠ Asked of the RAW name. `methodName` is quoted for ILAsm, so `'Main'` never equals
            // "Main" and the method that needs `.entrypoint` would not get it — ilasm then fails
            // the whole assembly with "No entry point declared for executable".
            var isMain = RawName(function.Name).Equals("Main", StringComparison.OrdinalIgnoreCase);

            // Method attributes
            WriteLine($"  .method public hidebysig static");

            // Parameters
            var paramList = string.Join(", ", function.Parameters.Select(p =>
                $"{ParamSpec(p)} {SanitizeName(p.Name)}"));

            WriteLine($"          {returnType} {methodName}({paramList}) cil managed");

            if (isMain)
            {
                WriteLine("  {");
                WriteLine("    .entrypoint");
            }
            else
            {
                WriteLine("  {");
            }

            // Calculate max stack (estimate)
            _maxStack = Math.Max(8, _localIndices.Count + _tempIndices.Count + 4);
            WriteLine($"    .maxstack {_maxStack}");

            // Declare locals
            if (_localIndices.Count > 0 || _tempIndices.Count > 0)
            {
                GenerateLocalsDeclaration(function);
            }

            WriteLine();

            // Storage for sized array locals, before any statement can index one.
            EmitArrayLocalAllocations(function);

            // Generate method body
            if (function.EntryBlock != null)
            {
                _visitedBlocks = new HashSet<BasicBlock>();
                GenerateBasicBlock(function.EntryBlock, _visitedBlocks, isEntry: true);
            }

            EmitLoweredReturnExit();

            // Ensure method ends with ret
            if (returnType == "void" && !EndsWithRet())
            {
                WriteLine("    ret");
            }

            WriteLine($"  }} // end of method {methodName}");
        }

        /// <summary>
        /// Reserves the locals a <c>Try</c> needs but <c>IRFunction.LocalVariables</c> never lists:
        /// each catch clause's exception variable, and — for a non-void function containing a Try
        /// — one result slot for a <c>Return</c> lowered out of a protected region.
        ///
        /// <para><b>Why here and not in IRBuilder.</b> IRBuilder pushes the catch variable onto its
        /// version stack but never adds it to <c>LocalVariables</c>, so no backend sees it as a
        /// declared local. Adding it there would move C#, C++, JavaScript and LLVM output too, for
        /// a defect that is MSIL's alone; this backend declares what IL requires and leaves the
        /// shared builder untouched.</para>
        ///
        /// <para><b>Why a pre-pass.</b> <c>.locals init</c> is written from these tables before the
        /// first body instruction is emitted. The old emitter allocated the catch variable during
        /// emission with <c>_localCounter++</c>, a counter unrelated to <c>_localIndices</c>, and
        /// produced <c>stloc 0</c> in a method with NO locals directive at all — the
        /// InvalidProgramException this fixes. Where a local did happen to exist at that index, the
        /// store landed on an unrelated variable of an unrelated type instead.</para>
        /// </summary>
        private void AllocateExceptionHandlingLocals(IRFunction function)
        {
            var tries = function.Blocks
                .SelectMany(b => b.Instructions)
                .OfType<IRTryCatch>()
                .ToList();
            if (tries.Count == 0) return;

            foreach (var clause in tries.SelectMany(t => t.CatchClauses))
            {
                if (string.IsNullOrEmpty(clause.VariableName)) continue;

                // ⛔ KEYED BY THE RAW NAME, printed quoted. `_localIndices` is looked up by
                // EmitLoadLocal/EmitStoreLocal with the name the IR uses, so a quoted key would
                // never be found and the catch variable would read as unknown storage.
                var name = clause.VariableName;
                if (_localIndices.ContainsKey(name)) continue;

                var index = _localIndices.Count;
                _localIndices[name] = index;
                _declaredIdentifiers.Add(clause.VariableName);
                _syntheticLocals.Add((index, IlTypeSpec(clause.ExceptionType ?? ExceptionTypeInfo), SanitizeName(name)));
            }

            // A Return inside a protected region cannot be a `ret`; it becomes a store plus a
            // `leave` to one exit that owns the real `ret`. Reserve the slot unconditionally for a
            // non-void function with a Try — an unused local costs a word and nothing else, while
            // discovering mid-emission that one is needed is exactly what cannot be fixed then.
            _methodExitResultLocal = -1;
            if (MapType(function.ReturnType) != "void")
            {
                _methodExitResultLocal = _localIndices.Count;
                var name = $"eh_result_{_methodExitResultLocal}";
                _localIndices[name] = _methodExitResultLocal;
                _syntheticLocals.Add((_methodExitResultLocal, IlTypeSpec(function.ReturnType), name));
            }
        }

        /// <summary>
        /// Reserves the two locals every <c>For Each</c> needs and <c>IRFunction.LocalVariables</c>
        /// never lists: the loop VARIABLE and the enumerator.
        ///
        /// <para><b>Why a pre-pass, for the third time in this file.</b> <c>.locals init</c> is
        /// written from these tables before the first body instruction is emitted, so a slot
        /// discovered mid-emission cannot be declared. The old emitter took both slots from
        /// <c>_localCounter++</c> — a counter that starts at 0 and is unrelated to
        /// <c>_localIndices</c> — so the enumerator's <c>stloc.s 0</c> landed on the COLLECTION's
        /// own slot and the loop variable's <c>stloc.s 1</c> on whatever happened to be next.
        /// Same shape as the catch-variable defect, same fix.</para>
        ///
        /// <para>⛔ <b>Keyed by the INSTRUCTION, not the variable name.</b> Two loops in one
        /// method may share a name and differ in element type
        /// (<c>For Each n In ints</c> … <c>For Each n In names</c>); one slot for both would
        /// declare <c>int32</c> and then store a string into it. The name is bound to the slot
        /// only while that loop's body is being emitted — see <see cref="EmitForEachBody"/>.</para>
        ///
        /// <para>The name goes into <c>_declaredIdentifiers</c> here and stays there, so
        /// <see cref="AllocateTemporaries"/> does not also hand the loop variable a temporary.</para>
        /// </summary>
        private void AllocateForEachLocals(IRFunction function)
        {
            var loops = function.Blocks
                .SelectMany(b => b.Instructions)
                .OfType<IRForEach>()
                .ToList();
            if (loops.Count == 0) return;

            // `.locals init` names every slot, and two slots may not share a name. A loop
            // variable that collides with a declared local — or with another loop's — is
            // suffixed rather than renamed away, so the IL still reads like the source.
            var printedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var local in function.LocalVariables) printedNames.Add(RawName(local.Name));

            foreach (var forEach in loops)
            {
                if (forEach?.VariableName == null || _foreachSlots.ContainsKey(forEach)) continue;

                var varIndex = _localIndices.Count;
                _localIndices[$"fe_var_{varIndex}"] = varIndex;
                var printed = RawName(forEach.VariableName);
                if (!printedNames.Add(printed)) printed = $"{printed}_fe{varIndex}";
                _syntheticLocals.Add((varIndex, IlTypeSpec(forEach.ElementType), SanitizeName(printed)));
                _declaredIdentifiers.Add(forEach.VariableName);

                var enumIndex = _localIndices.Count;
                var enumName = $"fe_enum_{enumIndex}";
                _localIndices[enumName] = enumIndex;
                _syntheticLocals.Add((enumIndex, EnumeratorSpec, enumName));

                _foreachSlots[forEach] = (varIndex, enumIndex);
            }
        }

        /// <summary>
        /// The one <c>ret</c> that a <c>Return</c> lowered out of a protected region leaves to.
        ///
        /// <para>Emitted only when such a Return was actually lowered, so a method without one is
        /// byte-identical to before. Shared by every path that emits a body — a method, a class
        /// member, a constructor, a property accessor — because a `leave` to a label nobody writes
        /// is an unresolved forward reference and ilasm refuses the method outright.</para>
        /// </summary>
        private void EmitLoweredReturnExit()
        {
            if (!_methodExitUsed) return;

            WriteLine($"  {_methodExitLabel}:");
            if (_methodExitResultLocal >= 0) EmitLdloc(_methodExitResultLocal);
            WriteLine("    ret");
        }

        /// <summary>The implicit <c>Catch</c> type when a clause names none.</summary>
        private static readonly TypeInfo ExceptionTypeInfo = new TypeInfo("Exception", TypeKind.Class);

        /// <summary>
        /// Allocates every sized array LOCAL at the top of the method.
        ///
        /// <para>⛔ <c>Dim a(2) As String</c> declared <c>[0] string[] a</c> and stopped there. A
        /// local of reference type starts null, so the very next <c>ldelema</c> dereferenced null
        /// and the program died with NullReferenceException — the array was never created at all.
        /// <c>.locals init</c> zeroes a slot; it does not construct anything.</para>
        ///
        /// <para>The element count comes from <see cref="TypeInfo.ArrayDimensionSizes"/>, the same
        /// carrier the C# and C++ backends read, so the three cannot drift on how big
        /// <c>Dim a(n)</c> is. Note that in BasicLang <c>n</c> is the element COUNT, not a VB-style
        /// upper bound: the analyzer validates it as a size ("Array size cannot be negative") and
        /// both other backends allocate exactly <c>n</c>. <c>a(n)</c> is therefore out of range,
        /// which is the language's decision and not an off-by-one here.</para>
        /// </summary>
        private void EmitArrayLocalAllocations(IRFunction function)
        {
            foreach (var local in function.LocalVariables)
            {
                if (!TryArrayAllocation(local.Type, out var elementToken, out var length)) continue;
                // ⛔ The RAW name. `_localIndices` is keyed by the IR's own name (see where the
                // declared locals are inserted), and the `continue` below is silent, so a key that
                // does not match does not fail the build — it skips the `newarr` and the program
                // dies later with a NullReferenceException on first use.
                if (!_localIndices.TryGetValue(local.Name, out var index)) continue;

                EmitLdcI4(length);
                _currentStack++;
                WriteLine($"    newarr {elementToken}");
                EmitStloc(index);
            }
        }

        /// <summary>
        /// The IL type of a temporary's slot.
        ///
        /// <para>⛔ An <see cref="IRGetElementPtr"/> temp holds an ADDRESS, not a value.
        /// <c>ldelema int32</c> pushes an <c>int32&amp;</c> (a managed pointer), and the old
        /// declaration typed that slot from the node's own type — <c>int32</c> — so the address
        /// was stored as if it were the element. The following <c>stind.i4</c> then treated that
        /// integer as a pointer and the program died with AccessViolationException.</para>
        ///
        /// <para>This was invisible until arrays started being allocated: every access previously
        /// dereferenced a null array and raised NullReferenceException first. The reference-typed
        /// case looked like it WORKED — a <c>string&amp;</c> in a <c>string</c> slot printed the
        /// right answer — which makes it the more dangerous half: unverifiable IL that the GC may
        /// see as an object reference, passing its test by luck.</para>
        /// </summary>
        private string TempSlotSpec(IRValue temp) =>
            temp is IRGetElementPtr ? IlTypeSpec(temp.Type) + "&" : IlTypeSpec(temp.Type);

        /// <summary>
        /// Gives every instance FIELD its starting value in a constructor — the declared
        /// initializer, or storage for a sized array — after the base call and before any
        /// constructor body can touch one.
        ///
        /// <para>⛔ The INITIALIZER half was missing entirely, and silently:
        /// <c>Public N As Integer = 5</c> emitted the field and dropped the 5, so the program ran
        /// and read <b>0</b>. A constructor that builds on the value inherited the same zero —
        /// <c>N = N + 3</c> over <c>= 5</c> answered 3 rather than 8 — and a String field came out
        /// null. Only the <c>Shared</c> case worked, because
        /// <see cref="GenerateClassStaticConstructor"/> already did this for the type initializer;
        /// this is the same loop on the instance side.</para>
        ///
        /// <para>⚠ AFTER the base call is not incidental: that is where VB runs field
        /// initializers, and it is what lets a base constructor observe its own fields already
        /// set while a derived one does not see the derived initializers until its turn.</para>
        ///
        /// <para>⚠ The initializer branch is written FIRST to match the static loop, and that
        /// precedence is UNREACHABLE rather than load-bearing — measured: the analyzer refuses an
        /// initializer on an array-typed field at all ("Cannot assign value of type 'Integer' to
        /// variable of type 'Integer[]'"), so no field can carry both. Swapping the two arms
        /// changes nothing any program can observe, and no test holds the order. Both arms ARE
        /// live, for different fields; only their relative order is arbitrary.</para>
        ///
        /// <para>Fields are the second site, and they are not optional. The C++ backend's own
        /// note records that its first version of this fix lived inline in the locals loop and
        /// left array fields unsized, which turned "does not build" into "builds and
        /// access-violates". One helper, both constructor paths — the explicit one and the
        /// generated default — because a class with a declared constructor never reaches the
        /// other.</para>
        /// </summary>
        private void EmitInstanceFieldInitialization(IRClass irClass)
        {
            if (irClass?.Fields == null) return;

            foreach (var field in irClass.Fields)
            {
                if (field.IsStatic) continue;   // a static field is not this instance's to create

                if (field.Initializer != null)
                {
                    WriteLine("    ldarg.0");
                    _currentStack++;
                    EmitInlineValue(field.Initializer);
                    EmitNumericCoercion(field.Initializer, field.Type);
                }
                else if (TryArrayAllocation(field.Type, out var elementToken, out var length))
                {
                    WriteLine("    ldarg.0");
                    _currentStack++;
                    EmitLdcI4(length);
                    _currentStack++;
                    WriteLine($"    newarr {elementToken}");
                }
                else
                {
                    continue;
                }

                WriteLine($"    stfld {IlTypeSpec(field.Type)} {SanitizeName(irClass.Name)}::{SanitizeName(field.Name)}");
                _currentStack -= 2;
            }
        }

        /// <summary>
        /// Decides whether a type needs array storage created for it, and with what
        /// <c>newarr</c> operand and length. False for anything that is not a sized array —
        /// including <c>Dim a() As String</c>, whose storage an assignment supplies later.
        ///
        /// <para>⛔ <b>Rank &gt; 1 is REFUSED, not allocated.</b> A multi-dimensional declaration
        /// currently collapses to a rank-1 IL type, and <c>Visit(IRGetElementPtr)</c> emits
        /// <c>ldelema</c> with <c>Indices[0]</c> alone — so <c>g(1, 2)</c> silently reads and
        /// writes <c>g[1]</c>, dropping the second index entirely. Allocating that array would
        /// turn a loud NullReferenceException into a quiet wrong answer, which is strictly worse.
        /// A real rank-2 lowering needs the rectangular form (<c>newobj int32[,]::.ctor</c> plus
        /// <c>Get</c>/<c>Set</c> calls) on BOTH the declaration and the indexing side.</para>
        ///
        /// <para>The operand goes through <see cref="IlTypeToken(string)"/>, so it reads
        /// <c>newarr [mscorlib]System.String</c>. ⚠ Unlike <c>box</c>, <c>newarr</c> ALSO accepts
        /// the IL keyword form — <c>newarr int32</c> and <c>newarr string</c> both assemble and
        /// run, measured directly against ilasm, and a mutation replacing the token with the
        /// keyword kills no test. The token form is kept for consistency with every other operand
        /// position in this file, not because the alternative is invalid; don't cite this line as
        /// evidence that it is.</para>
        /// </summary>
        private bool TryArrayAllocation(TypeInfo type, out string elementToken, out int length)
        {
            elementToken = null;
            length = 0;

            if (type?.Kind != TypeKind.Array || type.ElementType == null) return false;

            var sizes = type.ArrayDimensionSizes;
            if (sizes == null || sizes.Count == 0) return false;

            if (sizes.Count > 1)
            {
                throw new ForeignFeatureException(
                    $"MSIL: a {sizes.Count}-dimensional array has no IL lowering. The declaration "
                    + "collapses to a one-dimensional type and indexing emits ldelema with only "
                    + "the FIRST index, so g(i, j) silently reads and writes g(i) — allocating it "
                    + "would replace a NullReferenceException with a wrong answer. A rank-2 array "
                    + "needs the rectangular IL form (newobj T[,]::.ctor plus Get/Set) on the "
                    + "declaration and the indexing side together. Use nested rank-1 arrays, or "
                    + "target C#/C++ which lower this correctly.");
            }

            // An unsized dimension (`Dim a() As String`) is a declaration without storage: some
            // later assignment supplies the array. Nothing to create here.
            if (sizes[0] <= 0) return false;

            elementToken = IlTypeToken(type.ElementType);
            length = sizes[0];
            return true;
        }

        /// <summary>
        /// The <c>System.Exception</c> members this backend can name, and the accessor each one
        /// really is. All are <c>string</c>-valued properties declared on <c>Exception</c> itself,
        /// so a derived exception type inherits them and <c>callvirt</c> on the base is correct.
        ///
        /// <para>Deliberately narrow, on the same principle as <c>CollectionMembers</c>: a member
        /// with no recorded signature is REFUSED rather than guessed, because a guessed member
        /// reference assembles cleanly and fails at run time.</para>
        /// </summary>
        private static readonly Dictionary<string, string> ExceptionMembers =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["Message"] = "get_Message",
                ["StackTrace"] = "get_StackTrace",
                ["Source"] = "get_Source",
            };

        /// <summary>
        /// Resolves a member read on a recognized .NET exception to its property accessor.
        /// Returns false for any other receiver, so nothing outside exception types is affected;
        /// throws for an exception member outside <see cref="ExceptionMembers"/>.
        /// </summary>
        private bool TryExceptionMember(TypeInfo receiver, string member, out string token, out string accessor)
        {
            token = null;
            accessor = null;

            var name = receiver?.Name;
            if (name == null || !CppExceptionTypes.TryGetNetFullName(name, out var fullName)) return false;

            token = "[mscorlib]" + fullName;
            if (ExceptionMembers.TryGetValue(member ?? "", out accessor)) return true;

            throw new ForeignFeatureException(
                $"MSIL: '{name}.{member}' is outside the supported exception surface. MSIL carries "
                + "Message, StackTrace and Source, the string properties whose IL accessor names "
                + "are recorded. Emitting a guessed member produces a reference that assembles and "
                + "then fails with MissingFieldException or MissingMethodException at run time, so "
                + "it is refused here instead. Add a row to MSILCodeGenerator.ExceptionMembers plus "
                + "a round-trip test to widen the set.");
        }

        /// <summary>
        /// The <c>System.String</c> members that arrive at <see cref="Visit(IRFieldAccess)"/> —
        /// i.e. are written without parentheses — and the accessor each one really is.
        ///
        /// <para><b>One row, and that is not an oversight.</b> <c>System.String</c> has exactly one
        /// public instance property, <c>Length</c>; everything else on it is a method and arrives
        /// at <see cref="Visit(IRInstanceMethodCall)"/>, which already emits a real
        /// <c>callvirt</c>. The table exists rather than a hard-coded <c>if</c> so that widening it
        /// is the same one-row gesture as <see cref="CollectionMembers"/> and
        /// <see cref="ExceptionMembers"/>, and so the return type travels with the accessor name
        /// instead of being spelled at the emission site.</para>
        /// </summary>
        private static readonly Dictionary<string, CollectionMember> StringMembers =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["Length"] = new("get_Length", "int32", ""),
            };

        /// <summary>
        /// Resolves a parenthesis-free member read on a <c>String</c> receiver to its property
        /// accessor. Returns false for every other receiver, so nothing else is affected; throws
        /// for a String member outside <see cref="StringMembers"/>.
        ///
        /// <para>⛔ The refusal is stronger here than for collections or exceptions. Those types
        /// do have fields a future row might legitimately name; <c>System.String</c> has none, so
        /// any member reaching this point and not found here would go out as an <c>ldfld</c> that
        /// cannot exist. Refusing turns a run-time <c>MissingFieldException</c> from a build that
        /// reported success into a compile-time diagnostic.</para>
        /// </summary>
        private bool TryStringMember(TypeInfo receiver, string member, out CollectionMember accessor)
        {
            accessor = null;
            if (receiver == null || MapType(receiver) != "string") return false;
            if (StringMembers.TryGetValue(member ?? "", out accessor)) return true;

            throw new ForeignFeatureException(
                $"MSIL: 'String.{member}' is outside the supported String surface. Length is the "
                + "only member of System.String that is read without parentheses, and it is a "
                + "PROPERTY — it has to be emitted as get_Length(). System.String has no public "
                + "instance fields at all, so falling back to an ldfld here would emit a field "
                + "reference that assembles and then fails with MissingFieldException at run time. "
                + "Methods are unaffected: s.ToUpper() and s.Substring(i, n) go out as callvirt "
                + "through the instance-method path. Add a row to MSILCodeGenerator.StringMembers "
                + "plus a round-trip test to widen the set.");
        }

        private void AllocateTemporaries(IRFunction function)
        {
            foreach (var block in function.Blocks)
            {
                foreach (var instruction in block.Instructions)
                {
                    if (instruction is IRValue value &&
                        !(value is IRConstant) &&
                        !(value is IRVariable) &&
                        !string.IsNullOrEmpty(value.Name) &&
                        !_declaredIdentifiers.Contains(value.Name))
                    {
                        if (!_tempIndices.ContainsKey(value))
                        {
                            var newIdx = _localIndices.Count + _tempIndices.Count;
                            _tempIndices[value] = newIdx;

                            // Track by string representation for cross-reference lookup
                            var valueStr = value.ToString();
                            if (!string.IsNullOrEmpty(valueStr))
                                _tempNameIndices[valueStr] = newIdx;

                            // Also track by short name (e.g., "t0") for return value matching
                            if (!string.IsNullOrEmpty(value.Name) && !_tempNameIndices.ContainsKey(value.Name))
                                _tempNameIndices[value.Name] = newIdx;
                        }
                    }
                }
            }
        }

        private void GenerateLocalsDeclaration(IRFunction function)
        {
            // Collected with their indices and SORTED, not appended in source order: the three
            // sources interleave (a catch variable is allocated between the declared locals and
            // the temporaries), and ilasm reads `[n]` as the slot number, so an out-of-order list
            // silently declares the wrong type for a slot.
            var slots = new List<(int Index, string Text)>();

            // Declared local variables
            foreach (var local in function.LocalVariables)
            {
                var index = _localIndices[local.Name];
                slots.Add((index, $"      [{index}] {IlTypeSpec(local.Type)} {SanitizeName(local.Name)}"));
            }

            // Catch-clause exception variables and the lowered-return result slot
            foreach (var (index, spec, name) in _syntheticLocals)
            {
                slots.Add((index, $"      [{index}] {spec} {name}"));
            }

            // Temporary variables
            foreach (var (temp, index) in _tempIndices)
            {
                slots.Add((index, $"      [{index}] {TempSlotSpec(temp)} V_{index}"));
            }

            var locals = slots.OrderBy(s => s.Index).Select(s => s.Text).ToList();

            if (locals.Count > 0)
            {
                WriteLine("    .locals init (");
                for (int i = 0; i < locals.Count; i++)
                {
                    var comma = i < locals.Count - 1 ? "," : "";
                    WriteLine($"{locals[i]}{comma}");
                }
                WriteLine("    )");
            }
        }

        private bool EndsWithRet()
        {
            var lines = _output.ToString().Split('\n');
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("//")) continue;
                return line == "ret" || line.StartsWith("ret");
            }
            return false;
        }

        private void GenerateBasicBlock(BasicBlock block, HashSet<BasicBlock> visited, bool isEntry = false)
        {
            if (visited.Contains(block)) return;
            visited.Add(block);

            // Emit label (skip for entry block)
            if (!isEntry)
            {
                WriteLine($"  {SanitizeLabel(block.Name)}:");
            }

            // Process instructions
            foreach (var instruction in block.Instructions)
            {
                instruction.Accept(this);
            }

            // Process successor blocks
            foreach (var successor in block.Successors.Where(s => !visited.Contains(s)))
            {
                GenerateBasicBlock(successor, visited);
            }
        }

        /// <summary>
        /// Every user-chosen name, SINGLE-QUOTED for ILAsm.
        ///
        /// <para>⛔ Without this an ordinary variable name that happens to be an IL keyword does
        /// not assemble, with nothing said on the BasicLang side. Measured over 90 candidates as a
        /// plain <c>Dim … As Integer</c>: 8 are not BasicLang identifiers at all, and of the other
        /// 82 <b>64 make ilasm reject the program</b> — among them <c>value</c>, <c>add</c>,
        /// <c>call</c>, <c>method</c>, <c>field</c>, <c>filter</c>, <c>handler</c>,
        /// <c>custom</c>, <c>break</c>, <c>switch</c>, <c>box</c>, <c>literal</c>,
        /// <c>native</c> and <c>sealed</c>. The failure is
        /// <c>syntax error at token 'neg' in: [0] int32 neg</c>, and EVERY position is affected:
        /// local, parameter, method name, class name, field, module-level global and
        /// property.</para>
        ///
        /// <para>⛔ Quoting is PURELY LEXICAL, which is what makes quoting everything safe rather
        /// than a matching hazard: measured, a method DECLARED <c>'Twice'</c> and CALLED as
        /// <c>Twice</c> in the same file assembles and prints its answer. A quoted name and a bare
        /// one are the SAME identifier, so a site this misses still resolves and no reference has
        /// to be kept in step with its declaration.</para>
        ///
        /// <para>⚠ Everything is quoted rather than a keyword list, deliberately. ILAsm's keyword
        /// set is large and context-sensitive — <c>value</c> and <c>at</c> are in it, <c>file</c>
        /// and <c>hash</c> are not — and the compiler has no way to read it as data, so a list
        /// here is a list that drifts, and being wrong by one word costs a program that does not
        /// assemble. The IL reads more noisily; nothing else changes.</para>
        ///
        /// <para>⚠ The backend already quoted <c>'value'</c> BY HAND at the property and event
        /// setter parameter, which is the same fix applied to the one name that had already been
        /// hit. This generalizes it.</para>
        /// </summary>
        protected override string SanitizeName(string name) => IlName(RawName(name));

        /// <summary>
        /// The sanitized name WITHOUT the quotes.
        ///
        /// <para>⚠ Needed for two distinct reasons, and conflating them is how this breaks: the
        /// sites that COMPOSE an identifier out of a name (<c>get_X</c>, <c>add_X</c>,
        /// <c>fld_scratch_X</c>) would otherwise emit <c>get_'X'</c>; and <c>_localIndices</c> is
        /// keyed by the RAW IR name, because <see cref="EmitLoadLocal"/> and
        /// <see cref="EmitStoreLocal"/> look a local up by the name the IR uses, not by the name
        /// the IL prints.</para>
        /// </summary>
        private string RawName(string name) => base.SanitizeName(name);

        /// <summary>One complete identifier, quoted for ILAsm.</summary>
        private static string IlName(string identifier) => $"'{identifier}'";

        /// <summary>
        /// A branch label — NOT quoted, unlike every user-chosen name.
        ///
        /// <para>⚠ A block name cannot collide with an IL keyword: every one is built as
        /// <c>if{n}.then</c>, <c>switch{n}.default</c>, <c>for{n}.cond</c> and so on, so it always
        /// carries a digit, and <c>entry</c> is the only bare word. Quoting here would change
        /// every label in every method to buy a case that cannot arise, and it would cost the
        /// readability of the one thing a person reads generated IL for.</para>
        ///
        /// <para>⛔ What WOULD make it needed: a user-written label. <c>Visit(IRLabel)</c> exists
        /// but <c>new IRLabel</c> is never constructed anywhere and the lexer has no <c>GoTo</c>,
        /// so BasicLang has no way to name one today. Give the language labels and this has to
        /// quote.</para>
        /// </summary>
        private string SanitizeLabel(string name)
        {
            return RawName(name).Replace(".", "_");
        }

        private int GetLocalIndex(string name)
        {
            if (_localIndices.TryGetValue(name, out var idx))
                return idx;
            return -1;
        }

        private int GetParamIndex(string name)
        {
            if (_paramIndices.TryGetValue(name, out var idx))
                return idx;
            return -1;
        }

        private int GetTempIndex(IRValue value)
        {
            if (_tempIndices.TryGetValue(value, out var idx))
                return idx;

            // Try by name as well
            var valueName = value?.ToString() ?? "";
            if (!string.IsNullOrEmpty(valueName) && _tempNameIndices.TryGetValue(valueName, out idx))
                return idx;

            // Allocate new temp
            var newIdx = _localIndices.Count + _tempIndices.Count;
            _tempIndices[value] = newIdx;
            if (!string.IsNullOrEmpty(valueName))
                _tempNameIndices[valueName] = newIdx;
            return newIdx;
        }

        private void EmitLoadLocal(string name)
        {
            var idx = GetLocalIndex(name);
            if (idx >= 0)
            {
                EmitLdloc(idx);
                return;
            }

            idx = GetParamIndex(name);
            if (idx >= 0)
            {
                EmitLdarg(idx);

                // A ByRef parameter's slot holds a MANAGED POINTER, not the value. Reading the
                // name means dereferencing it — without this `n + 1` would add one to the ADDRESS.
                if (_byRefParams.TryGetValue(name, out var pointee))
                {
                    WriteLine($"    ldind.{GetIndirectSuffix(pointee)}");
                }
                return;
            }

            // `Me` is the receiver in argument slot 0. It used to resolve to nothing, so
            // `Me.Name` emitted a WARNING comment and then `ldfld` with an EMPTY stack.
            if (_currentMethodIsInstance && IsSelfReference(name))
            {
                EmitLdarg(0);
                return;
            }

            // A bare field name inside an instance method. This pushed NOTHING, so the very next
            // instruction ran an operand short — `Return "HI-" & Name` reached String::Concat with
            // one argument and the CLR rejected the whole method.
            if (_currentMethodIsInstance && _currentClassFields.TryGetValue(name, out var fieldType))
            {
                EmitLdarg(0);
                WriteLine($"    ldfld {IlTypeSpec(fieldType)} {FieldOwnerToken(name)}::{SanitizeName(name)}");
                return;
            }

            // The same shape for the class's own PROPERTY, which is a call rather than a load.
            if (_currentClassOwner != null
                && _currentClassProperties.TryGetValue(name, out var ownProperty))
            {
                if (!ownProperty.IsStatic)
                {
                    if (!_currentMethodIsInstance) return;
                    EmitLdarg(0);
                    _currentStack++;
                }

                EmitPropertyGet(_currentClassOwner, ownProperty);
                return;
            }

            // A `Shared` field of the enclosing class, by bare name. Nearer than a module-level
            // variable and further than an instance field, matching the language's own scoping.
            if (TryFindStaticField(_currentClass, name, out var ownStaticOwner, out var ownStatic))
            {
                WriteLine($"    ldsfld {IlTypeSpec(ownStatic.Type)} {SanitizeName(ownStaticOwner.Name)}::{SanitizeName(ownStatic.Name)}");
                _currentStack++;
                return;
            }

            // A module-level variable, LAST so that anything nearer in scope — a local, a
            // parameter, or a field of the enclosing instance — still shadows it by name.
            if (_moduleGlobals.TryGetValue(name, out var global))
            {
                WriteLine($"    ldsfld {IlTypeSpec(global.Type)} {_moduleName}::{SanitizeName(global.Name)}");
                _currentStack++;
                return;
            }

            WriteLine($"    // WARNING: Unknown local '{name}'");
        }

        // ================================================================================
        // ByRef ARGUMENTS — pushing an ADDRESS where every other argument pushes a value.
        // ================================================================================

        /// <summary>What kind of storage a ByRef argument names, and so which address opcode.</summary>
        private enum ByRefTargetKind
        {
            /// <summary><c>ldloca</c>.</summary>
            Local,
            /// <summary><c>ldarga</c> — the caller's own ByVal parameter IS a variable, and VB
            /// lets one be passed ByRef. The callee writes the caller's copy, which is what C#
            /// and C++ both do.</summary>
            Argument,
            /// <summary>A bare <c>ldarg</c>: the slot ALREADY holds the pointer, so taking its
            /// address would alias the argument slot instead of the caller's variable.</summary>
            ByRefArgument,
            /// <summary><c>ldarg.0</c> + <c>ldflda</c>.</summary>
            InstanceField,
            /// <summary><c>ldsflda</c> — a <c>Shared</c> field and a module global alike.</summary>
            StaticField,
        }

        private readonly struct ByRefTarget
        {
            internal ByRefTargetKind Kind { get; }
            internal int Slot { get; }
            internal TypeInfo Type { get; }
            internal string Token { get; }

            internal ByRefTarget(ByRefTargetKind kind, int slot, TypeInfo type, string token)
            {
                Kind = kind;
                Slot = slot;
                Type = type;
                Token = token;
            }
        }

        /// <summary>The declared type of the current method's local or parameter <paramref name="name"/>.</summary>
        private TypeInfo DeclaredStorageType(IEnumerable<IRVariable> candidates, string name) =>
            candidates?.FirstOrDefault(v => v?.Name != null
                && string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase))?.Type;

        /// <summary>
        /// Resolves <paramref name="name"/> to storage whose ADDRESS can be taken, walking exactly
        /// the ladder <see cref="EmitLoadLocal"/> walks and in the same order — a local, then a
        /// parameter, then a field of the enclosing instance, then a <c>Shared</c> field, then a
        /// module global. Resolving in any other order would hand the callee a pointer to
        /// different storage than the same name reads through in the same body.
        ///
        /// <para>⛔ A PROPERTY IS NOT STORAGE and is refused by the caller rather than resolved
        /// here: its bare name is an accessor CALL, so there is no address at all. VB's own
        /// semantics for that case are copy-in/copy-out through a temporary, which is not
        /// something this backend can synthesize without also knowing where the call ends — and a
        /// ByRef that writes a temporary nobody reads is precisely the silent wrong answer this
        /// refusal exists to prevent. C# refuses it too (CS0206).</para>
        /// </summary>
        private bool TryResolveByRefTarget(string name, out ByRefTarget target)
        {
            target = default;
            if (string.IsNullOrEmpty(name)) return false;

            var localIdx = GetLocalIndex(name);
            if (localIdx >= 0)
            {
                target = new ByRefTarget(ByRefTargetKind.Local, localIdx,
                    DeclaredStorageType(_currentFunction?.LocalVariables, name), null);
                return true;
            }

            var paramIdx = GetParamIndex(name);
            if (paramIdx >= 0)
            {
                var declared = DeclaredStorageType(_currentFunction?.Parameters, name);
                target = _byRefParams.ContainsKey(name)
                    ? new ByRefTarget(ByRefTargetKind.ByRefArgument, paramIdx, declared, null)
                    : new ByRefTarget(ByRefTargetKind.Argument, paramIdx, declared, null);
                return true;
            }

            if (_currentMethodIsInstance && _currentClassFields.TryGetValue(name, out var fieldType))
            {
                target = new ByRefTarget(ByRefTargetKind.InstanceField, 0, fieldType,
                    $"{FieldOwnerToken(name)}::{SanitizeName(name)}");
                return true;
            }

            // A property shadows anything further out, exactly as it does in EmitLoadLocal — so it
            // is checked HERE and reported as unaddressable rather than skipped, which would let a
            // module global of the same name be written instead.
            if (_currentClassOwner != null && _currentClassProperties.ContainsKey(name)) return false;

            if (TryFindStaticField(_currentClass, name, out var staticOwner, out var staticField))
            {
                target = new ByRefTarget(ByRefTargetKind.StaticField, 0, staticField.Type,
                    $"{SanitizeName(staticOwner.Name)}::{SanitizeName(staticField.Name)}");
                return true;
            }

            if (_moduleGlobals.TryGetValue(name, out var global))
            {
                target = new ByRefTarget(ByRefTargetKind.StaticField, 0, global.Type,
                    $"{_moduleName}::{SanitizeName(global.Name)}");
                return true;
            }

            return false;
        }

        /// <summary>Pushes the address <paramref name="target"/> describes.</summary>
        private void EmitByRefTarget(ByRefTarget target)
        {
            switch (target.Kind)
            {
                case ByRefTargetKind.Local:
                    if (target.Slot < 256) WriteLine($"    ldloca.s {target.Slot}");
                    else WriteLine($"    ldloca {target.Slot}");
                    break;
                case ByRefTargetKind.Argument:
                    if (target.Slot < 256) WriteLine($"    ldarga.s {target.Slot}");
                    else WriteLine($"    ldarga {target.Slot}");
                    break;
                case ByRefTargetKind.ByRefArgument:
                    // ⛔ NOT ldarga. The slot already holds the caller's pointer; `ldarga` would
                    // hand the callee a pointer to THIS frame's argument slot, so the write would
                    // land one level short and the original variable would never change.
                    EmitLdarg(target.Slot);
                    return;
                case ByRefTargetKind.InstanceField:
                    EmitLdarg(0);
                    WriteLine($"    ldflda {IlTypeSpec(target.Type)} {target.Token}");
                    return;
                case ByRefTargetKind.StaticField:
                    WriteLine($"    ldsflda {IlTypeSpec(target.Type)} {target.Token}");
                    break;
            }
            _currentStack++;
        }

        /// <summary>
        /// Pushes the ADDRESS of a ByRef argument, or refuses loudly.
        ///
        /// <para>⛔ REFUSING IS THE POINT. Every shape below that cannot be given an address has
        /// exactly one alternative — push a value, let the callee write it, and throw the write
        /// away — and that alternative ASSEMBLES AND RUNS. A ByRef that silently writes a
        /// temporary instead of the caller's variable is a worse failure than not compiling, so
        /// each of these is a hard refusal naming the shape. C# refuses the same three
        /// (CS1510 for a literal and an expression, CS0206 for a property) and C++ refuses the
        /// mismatched type, so this loses no program that another backend accepts.</para>
        /// </summary>
        private void EmitByRefArgument(IRValue argument, IRVariable declared, string calleeName, int position)
        {
            var where = $"argument {position + 1} of the ByRef call to '{calleeName}'";
            var wantedSpec = IlTypeSpec(declared?.Type);

            // `a(0)` lowers to a GEP — a managed pointer, already parked in a temp by
            // Visit(IRGetElementPtr) — followed by a LOAD of that pointer. The pointer is the
            // address; the loaded temp beside it is a copy, and passing the copy is the silent
            // wrong answer. Take the GEP.
            if (argument is IRLoad arrayLoad && arrayLoad.Address is IRGetElementPtr elementPtr)
            {
                RequireByRefSpec(wantedSpec, IlTypeSpec(arrayLoad.Type), where, "an array element");
                EmitLoadValue(elementPtr);
                return;
            }

            if (argument is IRVariable variable && TryResolveByRefTarget(variable.Name, out var target))
            {
                RequireByRefSpec(wantedSpec, IlTypeSpec(target.Type), where, $"'{variable.Name}'");
                EmitByRefTarget(target);
                return;
            }

            var what = argument switch
            {
                IRConstant => "a literal has no address",
                IRVariable named when _currentClassProperties.ContainsKey(named.Name ?? "")
                    => $"'{named.Name}' is a PROPERTY, and a property is an accessor call, not storage",
                IRVariable named => $"'{named.Name}' does not resolve to a local, a parameter, a "
                    + "field or a module-level variable",
                _ => "an expression's value lives in a temporary, not in the caller's storage",
            };

            throw new ForeignFeatureException(
                $"MSIL: {where} cannot be passed by reference — {what}. Passing it by VALUE "
                + "instead would assemble and run, and quietly drop the write-back the ByRef was "
                + "asked for; assign it to a variable first and pass that.");
        }

        /// <summary>
        /// A ByRef argument must name storage of EXACTLY the parameter's type: a pointer is not
        /// convertible, so there is no coercion to apply and nothing to widen through.
        ///
        /// <para>⚠ This is the shape <c>ArgumentCoercionTests</c> records as the discriminating
        /// one — <c>ByRef n As Double</c> given an Integer. C# rejects it (CS1503, "cannot convert
        /// from 'ref int' to 'ref double'") and C++ rejects it too; coercing it would create a
        /// temporary of the right type and write the increment into THAT.</para>
        /// </summary>
        private void RequireByRefSpec(string wanted, string actual, string where, string what)
        {
            if (string.Equals(wanted, actual, StringComparison.Ordinal)) return;

            throw new ForeignFeatureException(
                $"MSIL: {where} is {what}, of type {actual}, but the parameter is declared "
                + $"{wanted} ByRef. A managed pointer cannot be converted, so passing it would "
                + "mean writing the callee's change into a temporary of the parameter's type and "
                + "discarding it. C# rejects the same program (CS1503).");
        }

        /// <summary>
        /// Loads one call argument: its ADDRESS where the signature this call will SPELL carries
        /// <c>&amp;</c> at that position, its value everywhere else.
        ///
        /// <para>⛔ THE DECLARATION DECIDES, and it must be the SAME declaration
        /// <see cref="DeclaredParamList"/> spells the signature from — same list, same
        /// "is there one at all" test. Keying the argument on <c>IRCall.ByRefArguments</c> instead
        /// looks equivalent and is not: measured on <c>Util.Bump(v)</c> against a
        /// <c>Public Shared Sub Bump(ByRef n As Integer)</c>, the front end records NO by-ref
        /// marker for a <c>Type.SharedMethod</c> call (the same gap that makes the C# backend emit
        /// a raw CS1620 for it), so the signature came out <c>(int32&amp;)</c> from the declaration
        /// while the argument came out a value from the call site — an invalid program. One source
        /// for both is the only arrangement in which they cannot disagree.</para>
        ///
        /// <para>Shared by every call site — the module-procedure arm, the static user-class arm
        /// and the instance-method arm — because a ByRef that works through one spelling of a call
        /// and not another is the drift this file already carries three separate notes about.</para>
        /// </summary>
        private void EmitCallArguments(
            IReadOnlyList<IRValue> arguments,
            IReadOnlyList<IRVariable> declared,
            string calleeName)
        {
            // The SAME predicate DeclaredParamList uses to decide whether the declaration is what
            // gets spelled. Where it falls back to the argument types there is no `&` in the
            // signature, so there must be no address at the call either.
            var declarationDecides = declared != null && declared.Count > 0;

            for (var i = 0; i < arguments.Count; i++)
            {
                if (declarationDecides && i < declared.Count && declared[i] != null && declared[i].IsByRef)
                {
                    EmitByRefArgument(arguments[i], declared[i], calleeName, i);
                }
                else
                {
                    EmitLoadValue(arguments[i]);
                }
            }
        }

        /// <summary>
        /// Makes the owning class's <c>Shared</c> fields resolvable by bare name inside its own
        /// members — <c>Total = Total + 1</c> written inside <c>Counter</c>.
        ///
        /// <para>⛔ Registered for STATIC members too, which is why this is not folded into the
        /// instance-field loop above: that loop runs only when <c>isInstance</c>, and a
        /// <c>Shared Sub</c> is exactly where a bare <c>Shared</c> field name is most likely to
        /// appear. Without the name in <c>_declaredIdentifiers</c> the assignment lands in an
        /// anonymous temporary and the field never changes.</para>
        /// </summary>
        private void RegisterStaticFields(IRClass owner)
        {
            if (owner?.Fields == null) return;

            foreach (var field in owner.Fields)
            {
                if (!field.IsStatic || string.IsNullOrEmpty(field.Name)) continue;
                _declaredIdentifiers.Add(field.Name);
            }
        }

        /// <summary>
        /// Makes every module-level variable a name the current method body can resolve.
        ///
        /// <para>⛔ Registering the FIELD is not enough on its own. <c>_declaredIdentifiers</c> is
        /// what decides, at every assignment site, whether a computed value is stored to a NAME or
        /// dropped into an anonymous temporary. A global missing from this set means
        /// <c>Total = Total + 1</c> computes the sum, parks it in a temp nothing ever reads, and
        /// leaves the global untouched — a wrong answer with no warning comment to find it by.
        /// Called AFTER locals and parameters, but the set is unordered: shadowing is decided in
        /// <see cref="EmitLoadLocal"/>, which consults the slot tables first.</para>
        /// </summary>
        private void RegisterModuleGlobals()
        {
            foreach (var global in _moduleGlobals.Values)
                _declaredIdentifiers.Add(global.Name);
        }

        /// <summary>
        /// Resolves a call name to a <c>Shared</c> method on a user class, in either spelling the
        /// language allows: qualified (<c>MathUtil.Twice</c>) or unqualified from inside the
        /// declaring class (<c>Twice</c>).
        ///
        /// <para>⛔ The unqualified arm is NOT optional bonus coverage — it is what keeps
        /// <c>Return Half(84)</c> working once <see cref="IsClassMember"/> stops duplicating class
        /// methods onto the module class. That call used to bind to the duplicate and print the
        /// right answer; with the duplicate gone and nothing here, it would become a
        /// MissingMethodException. The two changes only make sense together.</para>
        ///
        /// <para>Matching is by NAME only, not by argument types: a user class reached this way
        /// has exactly the overloads this compilation declared, and picking between them is the
        /// front end's job, not this backend's. Contrast <see cref="TryResolveNetStaticCall"/>,
        /// which must choose among BCL overloads it did not declare.</para>
        /// </summary>
        private bool TryResolveUserStaticCall(string funcName, out string classToken, out IRMethod method)
        {
            classToken = null;
            method = null;
            if (string.IsNullOrEmpty(funcName) || _module?.Classes == null) return false;

            var dot = funcName.LastIndexOf('.');
            if (dot > 0)
            {
                var typeName = funcName.Substring(0, dot);
                var memberName = funcName.Substring(dot + 1);
                if (!TryFindClass(typeName, out var owner)) return false;
                if (!TryFindStaticMethod(owner, memberName, out var declaring, out method)) return false;

                classToken = SanitizeName(declaring.Name);
                return true;
            }

            // Unqualified, inside a class body. `_currentClass` is the only scope a bare name may
            // reach: a Shared member of some OTHER class always needs its type named.
            if (_currentClass == null) return false;
            if (!TryFindStaticMethod(_currentClass, funcName, out var ownDeclaring, out method)) return false;

            classToken = SanitizeName(ownDeclaring.Name);
            return true;
        }

        private bool TryFindClass(string name, out IRClass irClass)
        {
            irClass = null;
            if (string.IsNullOrEmpty(name) || _module?.Classes == null) return false;

            foreach (var candidate in _module.Classes.Values)
            {
                if (string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    irClass = candidate;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Finds a <c>Shared</c> method by name on <paramref name="irClass"/> or anywhere up its
        /// base chain, reporting the class that actually DECLARES it.
        ///
        /// <para>⛔ The declaring class is what the call must name. <c>Derived.Tag()</c> where
        /// <c>Tag</c> is Shared on <c>Base</c> is legal BasicLang — the C# backend emits it
        /// verbatim and C# resolves it — but emitting <c>call ... Derived::Tag()</c> here left the
        /// CLR looking for a method <c>Derived</c> does not define.</para>
        ///
        /// <para>The chain is walked with a visited set: <c>BaseClass</c> is an unvalidated name,
        /// and a cycle in it would otherwise hang the compiler rather than fail.</para>
        /// </summary>
        private bool TryFindStaticMethod(IRClass irClass, string name, out IRClass declaring, out IRMethod method)
        {
            declaring = null;
            method = null;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var current = irClass; current != null; )
            {
                if (!seen.Add(current.Name)) break;

                var found = current.Methods?.FirstOrDefault(m => m.IsStatic
                    && string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
                if (found != null)
                {
                    declaring = current;
                    method = found;
                    return true;
                }

                if (string.IsNullOrEmpty(current.BaseClass)) break;
                if (!TryFindClass(current.BaseClass, out current)) break;
            }

            return false;
        }

        private IRMethod FindStaticMethod(IRClass irClass, string name) =>
            TryFindStaticMethod(irClass, name, out _, out var method) ? method : null;

        /// <summary>
        /// Emits a resolved <c>Shared</c> call: arguments, then <c>call</c> with NO receiver.
        ///
        /// <para>⚠ The signature is spelled from the CALL's own argument and return types, matching
        /// every other call site in this file — the declaration those resolve against is emitted
        /// from the same <see cref="IlTypeSpec"/>, so the two agree. A call whose signature
        /// disagrees with the declaration binds to nothing and fails at run time, not at
        /// assembly.</para>
        /// </summary>
        private void EmitUserStaticCall(
            IRValue node, IReadOnlyList<IRValue> arguments, string classToken, IRMethod method, bool hasReturn)
        {
            EmitCallArguments(arguments, method?.Implementation?.Parameters, method?.Name);

            // ⛔ Spelled from the DECLARATION, not from the call site. GenerateClassMethod writes
            // the signature as MapType(method.ReturnType) and IlTypeSpec(parameter.Type); a call
            // that spells it any other way binds to nothing and fails at RUN time, since ilasm
            // does not resolve member references. `Derived.Tag()` reached here with a call type of
            // `object` where the method returns `string` — the call site is not a reliable source.
            var returnType = IlTypeSpec(method.ReturnType);
            var paramTypes = method.Implementation != null
                ? string.Join(", ", method.Implementation.Parameters.Select(ParamSpec))
                : string.Join(", ", arguments.Select(a => IlTypeSpec(a.Type)));
            WriteLine($"    call {returnType} {classToken}::{SanitizeName(method.Name)}({paramTypes})");

            _currentStack -= arguments.Count;
            if (hasReturn) _currentStack++;

            if (hasReturn && !string.IsNullOrEmpty(node.Name))
            {
                if (_declaredIdentifiers.Contains(node.Name)) EmitStoreLocal(node.Name);
                else EmitStloc(GetTempIndex(node));
            }
            else if (hasReturn)
            {
                WriteLine("    pop");
                _currentStack--;
            }
        }

        /// <summary>
        /// Resolves a field access to a <c>Shared</c> field on a user class, reporting whether the
        /// receiver was the TYPE (no value to load) or an INSTANCE (a value that must still be
        /// evaluated and discarded).
        ///
        /// <para>⚠ A name is only read as a type when nothing NEARER in scope answers to it. A
        /// local, parameter, enclosing-instance field or module-level global called <c>Counter</c>
        /// shadows a class of that name, exactly as it does in <see cref="EmitLoadLocal"/> — so
        /// this asks those tables first rather than letting a type name win by being checked
        /// earlier.</para>
        /// </summary>
        private bool TryResolveStaticField(
            IRValue receiver, string fieldName, out IRClass owner, out IRField field, out bool viaInstance)
        {
            owner = null;
            field = null;
            viaInstance = false;
            if (receiver == null || string.IsNullOrEmpty(fieldName)) return false;

            // The receiver names a type directly: `Counter.Total`.
            var name = receiver.Name;
            if (!string.IsNullOrEmpty(name) && !ResolvesAsValue(name) && TryFindClass(name, out var byName))
            {
                return TryFindStaticField(byName, fieldName, out owner, out field);
            }

            // The receiver is a value whose TYPE declares the Shared field: `b.Total`.
            if (TryFindClass(receiver.Type?.Name, out var byType)
                && TryFindStaticField(byType, fieldName, out owner, out field))
            {
                viaInstance = true;
                return true;
            }

            return false;
        }

        /// <summary>
        /// A property declared by <paramref name="irClass"/> or anything it inherits from.
        ///
        /// <para>The sibling of <see cref="TryFindStaticField"/> and
        /// <see cref="TryFindStaticMethod"/>, walked the same way and with the same visited set —
        /// <c>BaseClass</c> is an unvalidated name and a cycle in it would otherwise hang the
        /// compiler rather than fail.</para>
        /// </summary>
        private bool TryFindProperty(IRClass irClass, string name, out IRClass declaring, out IRProperty prop)
        {
            declaring = null;
            prop = null;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var current = irClass; current != null; )
            {
                if (!seen.Add(current.Name)) break;

                var found = current.Properties?.FirstOrDefault(p =>
                    string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
                if (found != null)
                {
                    declaring = current;
                    prop = found;
                    return true;
                }

                if (string.IsNullOrEmpty(current.BaseClass)) break;
                if (!TryFindClass(current.BaseClass, out current)) break;
            }

            return false;
        }

        /// <summary>
        /// The property a member access names, whether reached through a value (<c>b.Alpha</c>) or
        /// through the TYPE for a Shared one (<c>Box.Total</c>).
        ///
        /// <para>⛔ Deliberately shaped like <see cref="TryResolveStaticField"/>, because the same
        /// receiver ambiguity applies: a Shared property reached by type name has no receiver to
        /// load, while one reached through an instance has a receiver that must be evaluated and
        /// discarded.</para>
        /// </summary>
        private bool TryResolveProperty(
            IRValue receiver, string memberName, out IRClass owner, out IRProperty prop, out bool viaInstance)
        {
            owner = null;
            prop = null;
            viaInstance = false;
            if (receiver == null || string.IsNullOrEmpty(memberName)) return false;

            var name = receiver.Name;
            if (!string.IsNullOrEmpty(name) && !ResolvesAsValue(name) && TryFindClass(name, out var byName))
            {
                return TryFindProperty(byName, memberName, out owner, out prop);
            }

            if (TryFindClass(receiver.Type?.Name, out var byType)
                && TryFindProperty(byType, memberName, out owner, out prop))
            {
                viaInstance = true;
                return true;
            }

            return false;
        }

        /// <summary>
        /// A property READ as its accessor call. The receiver, if the property is an instance one,
        /// is already on the stack.
        ///
        /// <para>⛔ This is what a property access has to become. <c>c.Alpha</c> lowered to
        /// <c>ldfld int32 'Box'::'Alpha'</c> — a direct read of storage that does not exist, which
        /// ASSEMBLES (ilasm does not resolve member references) and dies at run time with
        /// <c>MissingFieldException: Field not found: 'Box.Alpha'</c>. For a property with a
        /// computed getter a field read cannot be right at all, whatever it names. The
        /// <c>ex.Message</c> and collection-<c>Count</c> arms above are the same fix for the BCL's
        /// properties.</para>
        /// </summary>
        private void EmitPropertyGet(IRClass owner, IRProperty prop)
        {
            var propType = IlTypeSpec(prop.Type);
            var token = SanitizeName(owner.Name);
            var getter = $"get_{RawName(prop.Name)}";

            if (prop.IsStatic)
            {
                WriteLine($"    call {propType} {token}::{getter}()");
                _currentStack++;
                return;
            }

            WriteLine($"    callvirt instance {propType} {token}::{getter}()");
            _currentStack--;   // the receiver
            _currentStack++;   // the value
        }

        /// <summary>
        /// A property WRITE as its accessor call. The receiver (for an instance property) and then
        /// the value are already on the stack, which is the order <c>callvirt</c> wants.
        /// </summary>
        private void EmitPropertySet(IRClass owner, IRProperty prop)
        {
            var propType = IlTypeSpec(prop.Type);
            var token = SanitizeName(owner.Name);
            var setter = $"set_{RawName(prop.Name)}";

            if (prop.IsStatic)
            {
                WriteLine($"    call void {token}::{setter}({propType})");
                _currentStack--;
                return;
            }

            WriteLine($"    callvirt instance void {token}::{setter}({propType})");
            _currentStack -= 2;
        }

        /// <summary>True when this name already denotes storage the current method can load.</summary>
        private bool ResolvesAsValue(string name) =>
            GetLocalIndex(name) >= 0
            || GetParamIndex(name) >= 0
            || (_currentMethodIsInstance && _currentClassFields.ContainsKey(name))
            || _moduleGlobals.ContainsKey(name);

        /// <summary>The field counterpart of <see cref="TryFindStaticMethod"/>, base chain and all.</summary>
        private bool TryFindStaticField(IRClass irClass, string name, out IRClass declaring, out IRField field)
        {
            declaring = null;
            field = null;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var current = irClass; current != null; )
            {
                if (!seen.Add(current.Name)) break;

                var found = current.Fields?.FirstOrDefault(f => f.IsStatic
                    && string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
                if (found != null)
                {
                    declaring = current;
                    field = found;
                    return true;
                }

                if (string.IsNullOrEmpty(current.BaseClass)) break;
                if (!TryFindClass(current.BaseClass, out current)) break;
            }

            return false;
        }

        private IRField FindStaticField(IRClass irClass, string name) =>
            TryFindStaticField(irClass, name, out _, out var field) ? field : null;

        /// <summary>The spellings of the receiver a BasicLang instance method can name.</summary>
        private static bool IsSelfReference(string name) =>
            string.Equals(name, "Me", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "this", StringComparison.OrdinalIgnoreCase);

        private void EmitStoreLocal(string name)
        {
            var idx = GetLocalIndex(name);
            if (idx >= 0)
            {
                EmitStloc(idx);
                return;
            }

            // ⛔ A PARAMETER — the arm this method simply did not have. EmitLoadLocal resolves a
            // parameter immediately after a local, and a store MUST walk the same ladder in the
            // same order: check the fields below first and `n = n + 1` inside a class whose field
            // is also called `n` would read the argument and write the field. Falling off the end
            // instead (`// WARNING: Cannot store to 'n'`) left the computed value on the stack and
            // the CLR rejected the whole method — the one defect behind BOTH
            // `Sub Bump(ByRef n As Integer)` and `For <parameter> = 1 To n`.
            var paramIdx = GetParamIndex(name);
            if (paramIdx >= 0)
            {
                if (_byRefParams.TryGetValue(name, out var pointee))
                {
                    // Write THROUGH the pointer. `stind` wants the address UNDER the value and the
                    // value is already on the stack, so park it, push the address, re-push it —
                    // the same park-and-re-push the `stfld` arm below does, and for the same
                    // reason: IL has no swap.
                    if (_byRefStoreScratch.TryGetValue(name, out var byRefScratch))
                    {
                        EmitStloc(byRefScratch);
                        EmitLdarg(paramIdx);
                        EmitLdloc(byRefScratch);
                        WriteLine($"    stind.{GetIndirectSuffix(pointee)}");
                        _currentStack -= 2;
                        return;
                    }

                    // The scratch pre-pass scans the same instruction shapes this store is reached
                    // from, so a missing slot means those two have drifted apart. Emitting a
                    // `starg` here would overwrite the POINTER with the value and the caller's
                    // variable would silently keep its old one.
                    throw new ForeignFeatureException(
                        $"MSIL: no scratch slot was reserved for the write to ByRef parameter "
                        + $"'{name}'. AllocateByRefStoreScratch and EmitStoreLocal disagree about "
                        + "which instruction shapes write a parameter; a store emitted without one "
                        + "would overwrite the pointer instead of the caller's variable.");
                }

                EmitStarg(paramIdx);
                return;
            }

            // Assigning a field of the enclosing instance. The value is ALREADY on the stack and
            // `stfld` needs the object under it, so park the value, push `Me`, and re-push — IL
            // has no swap. Without this the assignment landed in a temporary and was dropped:
            // `N = N + 1` computed the sum and threw it away.
            if (_currentMethodIsInstance
                && _currentClassFields.TryGetValue(name, out var fieldType)
                && _fieldStoreScratch.TryGetValue(name, out var scratch))
            {
                EmitStloc(scratch);
                EmitLdarg(0);
                EmitLdloc(scratch);
                WriteLine($"    stfld {IlTypeSpec(fieldType)} {FieldOwnerToken(name)}::{SanitizeName(name)}");
                _currentStack -= 2;
                return;
            }

            // The same for the class's own PROPERTY. The setter wants the receiver UNDER the
            // value, so this needs the same park-and-re-push the `stfld` arm above does — and it
            // borrows that arm's scratch slot, which AllocateFieldStoreScratch reserves for every
            // assignable member name.
            if (_currentClassOwner != null
                && _currentClassProperties.TryGetValue(name, out var ownSetProperty))
            {
                if (ownSetProperty.IsStatic)
                {
                    EmitPropertySet(_currentClassOwner, ownSetProperty);
                    return;
                }

                if (_currentMethodIsInstance && _fieldStoreScratch.TryGetValue(name, out var propScratch))
                {
                    EmitStloc(propScratch);
                    EmitLdarg(0);
                    _currentStack++;
                    EmitLdloc(propScratch);
                    _currentStack++;
                    EmitPropertySet(_currentClassOwner, ownSetProperty);
                    return;
                }
            }

            // A `Shared` field of the enclosing class. Like the module-level case below and
            // unlike the instance `stfld` above, `stsfld` needs no scratch slot.
            if (TryFindStaticField(_currentClass, name, out var ownStaticOwner, out var ownStatic))
            {
                WriteLine($"    stsfld {IlTypeSpec(ownStatic.Type)} {SanitizeName(ownStaticOwner.Name)}::{SanitizeName(ownStatic.Name)}");
                _currentStack--;
                return;
            }

            // A module-level variable. Unlike `stfld` this needs NO scratch slot: `stsfld` takes
            // the value alone, with no object reference under it.
            if (_moduleGlobals.TryGetValue(name, out var global))
            {
                WriteLine($"    stsfld {IlTypeSpec(global.Type)} {_moduleName}::{SanitizeName(global.Name)}");
                _currentStack--;
                return;
            }

            WriteLine($"    // WARNING: Cannot store to '{name}'");
        }

        private void EmitLdloc(int index)
        {
            switch (index)
            {
                case 0: WriteLine("    ldloc.0"); break;
                case 1: WriteLine("    ldloc.1"); break;
                case 2: WriteLine("    ldloc.2"); break;
                case 3: WriteLine("    ldloc.3"); break;
                default:
                    if (index < 256)
                        WriteLine($"    ldloc.s {index}");
                    else
                        WriteLine($"    ldloc {index}");
                    break;
            }
            _currentStack++;
        }

        private void EmitStloc(int index)
        {
            switch (index)
            {
                case 0: WriteLine("    stloc.0"); break;
                case 1: WriteLine("    stloc.1"); break;
                case 2: WriteLine("    stloc.2"); break;
                case 3: WriteLine("    stloc.3"); break;
                default:
                    if (index < 256)
                        WriteLine($"    stloc.s {index}");
                    else
                        WriteLine($"    stloc {index}");
                    break;
            }
            _currentStack--;
        }

        /// <summary>
        /// Store to argument slot <paramref name="index"/>.
        ///
        /// <para>⚠ There is no <c>starg.0</c>: unlike <c>ldarg</c>, <c>starg</c> has exactly two
        /// encodings — the short form <c>starg.s</c> and the long <c>starg</c>. Writing
        /// <c>starg.0</c> by analogy with <c>ldarg.0</c> is not an instruction and ilasm rejects it.</para>
        /// </summary>
        private void EmitStarg(int index)
        {
            if (index < 256)
                WriteLine($"    starg.s {index}");
            else
                WriteLine($"    starg {index}");
            _currentStack--;
        }

        private void EmitLdarg(int index)
        {
            switch (index)
            {
                case 0: WriteLine("    ldarg.0"); break;
                case 1: WriteLine("    ldarg.1"); break;
                case 2: WriteLine("    ldarg.2"); break;
                case 3: WriteLine("    ldarg.3"); break;
                default:
                    if (index < 256)
                        WriteLine($"    ldarg.s {index}");
                    else
                        WriteLine($"    ldarg {index}");
                    break;
            }
            _currentStack++;
        }

        private void EmitLoadValue(IRValue value)
        {
            if (value is IRConstant constant)
            {
                EmitLoadConstant(constant);
            }
            else if (value is IRVariable variable)
            {
                EmitLoadLocal(variable.Name);
            }
            else if (_tempIndices.TryGetValue(value, out var idx))
            {
                EmitLdloc(idx);
            }
            else if (!string.IsNullOrEmpty(value.Name) && _declaredIdentifiers.Contains(value.Name))
            {
                EmitLoadLocal(value.Name);
            }
            else
            {
                // Try by string representation
                var valueName = value?.ToString() ?? "";
                if (!string.IsNullOrEmpty(valueName) && _tempNameIndices.TryGetValue(valueName, out idx))
                {
                    EmitLdloc(idx);
                }
                // Try by short name (e.g., "t0")
                else if (!string.IsNullOrEmpty(value?.Name) && _tempNameIndices.TryGetValue(value.Name, out idx))
                {
                    EmitLdloc(idx);
                }
                else
                {
                    WriteLine($"    // WARNING: Cannot load value '{value}'");
                }
            }
        }

        private void EmitLoadConstant(IRConstant constant)
        {
            if (constant.Value == null)
            {
                WriteLine("    ldnull");
                _currentStack++;
                return;
            }

            switch (constant.Value)
            {
                case bool b:
                    WriteLine(b ? "    ldc.i4.1" : "    ldc.i4.0");
                    break;
                case int i:
                    EmitLdcI4(i);
                    break;
                case long l:
                    WriteLine($"    ldc.i8 {l}");
                    break;
                case float f:
                    WriteLine($"    ldc.r4 {f:G9}");
                    break;
                case double d:
                    WriteLine($"    ldc.r8 {d:G17}");
                    break;
                case string s:
                    WriteLine($"    ldstr \"{EscapeString(s)}\"");
                    break;
                case char c:
                    EmitLdcI4((int)c);
                    break;
                default:
                    WriteLine($"    // WARNING: Unknown constant type: {constant.Value.GetType()}");
                    WriteLine("    ldc.i4.0");
                    break;
            }
            _currentStack++;
        }

        private void EmitLdcI4(int value)
        {
            switch (value)
            {
                case -1: WriteLine("    ldc.i4.m1"); break;
                case 0: WriteLine("    ldc.i4.0"); break;
                case 1: WriteLine("    ldc.i4.1"); break;
                case 2: WriteLine("    ldc.i4.2"); break;
                case 3: WriteLine("    ldc.i4.3"); break;
                case 4: WriteLine("    ldc.i4.4"); break;
                case 5: WriteLine("    ldc.i4.5"); break;
                case 6: WriteLine("    ldc.i4.6"); break;
                case 7: WriteLine("    ldc.i4.7"); break;
                case 8: WriteLine("    ldc.i4.8"); break;
                default:
                    if (value >= -128 && value <= 127)
                        WriteLine($"    ldc.i4.s {value}");
                    else
                        WriteLine($"    ldc.i4 {value}");
                    break;
            }
        }

        private string EscapeString(string s)
        {
            var sb = new StringBuilder();
            foreach (var ch in s)
            {
                switch (ch)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (ch < 32 || ch > 126)
                            sb.Append($"\\u{(int)ch:X4}");
                        else
                            sb.Append(ch);
                        break;
                }
            }
            return sb.ToString();
        }

        #region Visitor Methods

        public override void Visit(IRFunction function) { }
        public override void Visit(BasicBlock block) { }
        public override void Visit(IRConstant constant) { }
        public override void Visit(IRVariable variable) { }

        public override void Visit(IRBinaryOp binaryOp)
        {
            // Handle string concatenation specially
            if (binaryOp.Operation == BinaryOpKind.Concat)
            {
                EmitLoadValue(binaryOp.Left);
                EmitLoadValue(binaryOp.Right);
                WriteLine("    call string [mscorlib]System.String::Concat(string, string)");
                _currentStack--; // Two pops, one push = net -1

                // Store result
                if (!string.IsNullOrEmpty(binaryOp.Name) && _declaredIdentifiers.Contains(binaryOp.Name))
                {
                    EmitStoreLocal(binaryOp.Name);
                }
                else
                {
                    var tempIdx = GetTempIndex(binaryOp);
                    EmitStloc(tempIdx);
                }
                return;
            }

            // Load operands onto stack, each converted to the type the operation computes in.
            var operandKind = BinaryOperandKind(binaryOp);
            EmitLoadValue(binaryOp.Left);
            EmitNumericCoercion(binaryOp.Left, operandKind);
            EmitLoadValue(binaryOp.Right);
            EmitNumericCoercion(binaryOp.Right, operandKind);

            // Emit operation
            var op = _typeMapper.MapBinaryOperator(binaryOp.Operation);
            WriteLine($"    {op}");
            _currentStack--; // Two pops, one push = net -1

            // Store result
            if (!string.IsNullOrEmpty(binaryOp.Name) && _declaredIdentifiers.Contains(binaryOp.Name))
            {
                // Store to declared variable
                EmitStoreLocal(binaryOp.Name);
            }
            else
            {
                // Store to temp
                var tempIdx = GetTempIndex(binaryOp);
                EmitStloc(tempIdx);
            }
        }

        public override void Visit(IRUnaryOp unaryOp)
        {
            EmitLoadValue(unaryOp.Operand);

            var op = _typeMapper.MapUnaryOperator(unaryOp.Operation);
            WriteLine($"    {op}");

            // Store result
            if (!string.IsNullOrEmpty(unaryOp.Name) && _declaredIdentifiers.Contains(unaryOp.Name))
            {
                EmitStoreLocal(unaryOp.Name);
            }
            else
            {
                var tempIdx = GetTempIndex(unaryOp);
                EmitStloc(tempIdx);
            }
        }

        public override void Visit(IRCompare compare)
        {
            var operandKind = WiderNumericKind(compare.Left, compare.Right);
            EmitLoadValue(compare.Left);
            EmitNumericCoercion(compare.Left, operandKind);
            EmitLoadValue(compare.Right);
            EmitNumericCoercion(compare.Right, operandKind);

            EmitCompareOpcodes(compare.Comparison);

            _currentStack--; // Net effect

            // Store result
            if (!string.IsNullOrEmpty(compare.Name) && _declaredIdentifiers.Contains(compare.Name))
            {
                EmitStoreLocal(compare.Name);
            }
            else
            {
                var tempIdx = GetTempIndex(compare);
                EmitStloc(tempIdx);
            }
        }

        public override void Visit(IRAssignment assignment)
        {
            EmitLoadValue(assignment.Value);
            EmitStoreLocal(assignment.Target.Name);
        }

        public override void Visit(IRLoad load)
        {
            if (load.Address is IRVariable variable)
            {
                EmitLoadLocal(variable.Name);
            }
            else
            {
                EmitLoadValue(load.Address);
                var elemType = MapType(load.Type);
                WriteLine($"    ldind.{GetIndirectSuffix(load.Type)}");
            }

            // Store to temp if needed
            if (_tempIndices.ContainsKey(load))
            {
                var tempIdx = GetTempIndex(load);
                EmitStloc(tempIdx);
            }
        }

        private string GetIndirectSuffix(TypeInfo type)
        {
            if (type == null) return "ref";
            var name = type.Name?.ToLower() ?? "";
            return name switch
            {
                "integer" or "int" => "i4",
                "long" => "i8",
                "single" or "float" => "r4",
                "double" => "r8",
                "boolean" or "bool" => "i1",
                "byte" => "u1",
                "short" => "i2",
                "char" => "u2",
                _ => "ref"
            };
        }

        public override void Visit(IRStore store)
        {
            if (store.Address is IRVariable variable)
            {
                EmitLoadValue(store.Value);
                EmitStoreLocal(variable.Name);
            }
            else if (store.Address is IRAlloca alloca)
            {
                // ⛔ An alloca is a LOCAL SLOT on this backend, not a pointer — Visit(IRAlloca)
                // emits nothing precisely because `.locals init` already reserved it. Falling
                // through to the indirect path below emitted `ldloc N; ldloc M; stind.ref`, which
                // stores THROUGH the slot's contents; the slot is null, so `Dim a() As Integer =
                // {10, 20, 30}` died with NullReferenceException after building the array
                // correctly. Store to the slot instead.
                EmitLoadValue(store.Value);
                EmitStloc(GetTempIndex(alloca));
            }
            else
            {
                // Indirect store
                EmitLoadValue(store.Address);
                EmitLoadValue(store.Value);
                var suffix = GetIndirectSuffix(store.Value.Type);
                WriteLine($"    stind.{suffix}");
                _currentStack -= 2;
            }
        }

        public override void Visit(IRCall call)
        {
            var funcName = call.FunctionName;
            var hasReturn = call.Type != null && !call.Type.Name.Equals("Void", StringComparison.OrdinalIgnoreCase);

            // Check if this is an extern function call
            if (_module != null && _module.IsExtern(funcName))
            {
                var externDecl = _module.GetExtern(funcName);
                if (externDecl != null && externDecl.HasImplementation("MSIL"))
                {
                    var impl = externDecl.GetImplementation("MSIL");

                    // Load arguments
                    foreach (var arg in call.Arguments)
                    {
                        EmitLoadValue(arg);
                    }

                    // Emit the IL call (implementation should be fully qualified IL call)
                    WriteLine($"    {impl}");
                    _currentStack -= call.Arguments.Count;
                    if (hasReturn) _currentStack++;

                    // Store result if needed
                    if (hasReturn && !string.IsNullOrEmpty(call.Name))
                    {
                        if (_declaredIdentifiers.Contains(call.Name))
                        {
                            EmitStoreLocal(call.Name);
                        }
                        else
                        {
                            var tempIdx = GetTempIndex(call);
                            EmitStloc(tempIdx);
                        }
                    }
                    else if (hasReturn)
                    {
                        WriteLine("    pop");
                        _currentStack--;
                    }
                    return;
                }
            }

            // Handle standard library calls
            if (TryEmitStdLibCall(funcName, call.Arguments.ToList(), hasReturn))
            {
                // Store result if needed — unless the arm emitted a statement, which leaves
                // nothing on the stack for a stloc to take.
                if (hasReturn && !string.IsNullOrEmpty(call.Name)
                    && !IsVoidStdLibArm(ResolveStdLibArm(funcName)))
                {
                    // The arm and the IR can disagree about what is on the stack — see
                    // _stdLibResultSpec. Box across the gap before the store, exactly as the
                    // .NET-static arm below does, so the slot holds what its declared type says.
                    if (_stdLibResultSpec != null
                        && NeedsBoxingInto(IlTypeSpec(call.Type), _stdLibResultSpec, out var stdBoxToken))
                    {
                        WriteLine($"    box {stdBoxToken}");
                    }

                    if (_declaredIdentifiers.Contains(call.Name))
                    {
                        EmitStoreLocal(call.Name);
                    }
                    else
                    {
                        var tempIdx = GetTempIndex(call);
                        EmitStloc(tempIdx);
                    }
                }
                return;
            }

            // A .NET static member with a recorded signature — `Math.Sqrt(x)` — emitted as a
            // direct call. Ahead of the fallback below, which sanitises the dot out of the name
            // and produces `call object Combined::MathSqrt(float64)`: a call on THIS module's
            // class, to a method nothing defines.
            if (TryResolveNetStaticCall(funcName, call.Arguments.ToList(), out var netToken, out var netOverload))
            {
                var conversions = _netStaticConversions;
                for (var i = 0; i < call.Arguments.Count; i++)
                {
                    EmitLoadValue(call.Arguments[i]);
                    if (i < conversions.Length && conversions[i] != null)
                    {
                        WriteLine($"    {conversions[i]}");
                    }
                }

                var netParams = string.Join(", ", netOverload.ParameterSpecs);
                WriteLine($"    call {netOverload.ReturnSpec} {netToken}::{SanitizeName(funcName.Substring(funcName.IndexOf('.') + 1))}({netParams})");
                _currentStack -= call.Arguments.Count;

                if (netOverload.ReturnSpec != "void")
                {
                    _currentStack++;

                    // ⛔ IRBuilder cannot know the .NET signature, so it types the call as
                    // returning Object and the slot is declared `object`. The call really returns
                    // a value type, and storing a raw float64 into an object slot is a type
                    // mismatch the CLR follows into a NullReferenceException at the first use.
                    // Box across that gap — the slot's declared type is what the rest of the
                    // method reads it back as.
                    if (NeedsBoxingInto(IlTypeSpec(call.Type), netOverload.ReturnSpec, out var boxToken))
                    {
                        WriteLine($"    box {boxToken}");
                    }

                    if (!string.IsNullOrEmpty(call.Name))
                    {
                        if (_declaredIdentifiers.Contains(call.Name)) EmitStoreLocal(call.Name);
                        else EmitStloc(GetTempIndex(call));
                    }
                }
                return;
            }

            // A `Shared` member of a user class, reached as `MathUtil.Twice(21)` or unqualified
            // from a sibling member. Must come before the fallback for the same reason the .NET
            // arm does: SanitizeName strips the dot, so `MathUtil.Twice` became a call to
            // `Combined::MathUtilTwice` — a method nothing defines, on the module's own class.
            if (TryResolveUserStaticCall(funcName, out var userClassToken, out var userMethod))
            {
                EmitUserStaticCall(call, call.Arguments, userClassToken, userMethod, hasReturn);
                return;
            }

            // ⛔ A SIBLING call inside an instance method — `Return Inner() + 1` — needs the
            // receiver pushed first and a `callvirt instance`. It used to fall through to the
            // module-class arm below and emit `call int32 Program::Inner()`: a static call, on a
            // class that does not exist, to a method that is not static. `_moduleName` is null
            // while a class body is being emitted, which is where the phantom `Program` came from.
            // ⛔ The walk, not just the class's own methods: a bare call to an INHERITED method
            // fell through to the module-class arm and emitted `call {ret} Program::Helper()` —
            // MissingMethodException from a build that reported success, measured. The receiver
            // token has to name the class that DECLARES the method, as the static sibling
            // TryFindStaticMethod already does.
            var selfCallOwner = _currentMethodIsInstance && _currentClassToken != null
                ? DeclaringClassOfInstanceMethod(_currentClass, funcName)
                : null;
            var isSelfCall = selfCallOwner != null;

            if (isSelfCall) EmitLdarg(0);

            var declaredParams =
                isSelfCall ? DeclaredMethodParams(selfCallOwner, funcName) : DeclaredFunctionParams(funcName);

            // Load arguments — an ADDRESS for each one the DECLARATION takes ByRef.
            EmitCallArguments(call.Arguments, declaredParams, funcName);

            // Generate call
            // Type SPECS: the declaration these resolve to spells its parameters the same way,
            // and a call whose signature disagrees with the declaration binds to nothing.
            var returnType = IlTypeSpec(call.Type);
            var paramTypes = DeclaredParamList(declaredParams, call.Arguments);
            var sanitizedName = SanitizeName(funcName);

            if (isSelfCall)
            {
                WriteLine($"    callvirt instance {returnType} {SanitizeName(selfCallOwner.Name)}::{sanitizedName}({paramTypes})");
                _currentStack--;   // the receiver
            }
            else
            {
                // Use module name for class reference
                var className = _moduleName ?? "Program";
                WriteLine($"    call {returnType} {className}::{sanitizedName}({paramTypes})");
            }

            _currentStack -= call.Arguments.Count;
            if (hasReturn) _currentStack++;

            // Store result if needed
            if (hasReturn && !string.IsNullOrEmpty(call.Name))
            {
                if (_declaredIdentifiers.Contains(call.Name))
                {
                    EmitStoreLocal(call.Name);
                }
                else
                {
                    var tempIdx = GetTempIndex(call);
                    EmitStloc(tempIdx);
                }
            }
            else if (hasReturn)
            {
                // Discard result if not used
                WriteLine("    pop");
                _currentStack--;
            }
        }

        /// <summary>
        /// The .NET <c>Console</c> spellings, mapped onto the stdlib arm that already emits
        /// them. <c>IRBuilder</c> names a static member call <c>Type.Member</c> — with the dot —
        /// so <c>Console.WriteLine("x")</c> arrives here as <c>"Console.WriteLine"</c>, matches
        /// no arm, and used to fall through to the emit-a-call-on-the-current-class default:
        /// <c>call object Combined::ConsoleWriteLine(string)</c>, a method nothing defines.
        /// ilasm accepts that (a MemberRef needs no definition) and it dies at RUN time with
        /// MissingMethodException — the phantom self-call.
        ///
        /// <para><b>An explicit table, not qualifier-stripping.</b> Dropping the <c>Type.</c>
        /// prefix and re-matching would route <c>Decimal.Round</c> onto the <c>Math.Round</c>
        /// arm, which is a silent mis-emission rather than a missing one. Only the spellings
        /// written here are aliased.</para>
        ///
        /// <para>The targets are deliberate and mirror <c>CppCodeGenerator.StdLibArm</c>, which
        /// carries the same two dotted arms: on that backend <c>PrintLine</c> and
        /// <c>Console.WriteLine</c> emit identical code on purpose, because "a split where
        /// PrintLine printed 'A' and Console.WriteLine printed 65 would be a fresh internal
        /// inconsistency". The <c>printline</c>/<c>print</c> arms already handle the ZERO-ARG
        /// case (<c>Console.WriteLine()</c> is a bare newline), so that behaviour comes along
        /// for free rather than needing a second implementation.</para>
        /// </summary>
        /// <summary>
        /// The stdlib ARM a call name resolves to, after dotted aliasing. Used by both the
        /// emitter and <see cref="IsVoidStdLibArm"/> so the two cannot disagree about which arm
        /// ran — a disagreement there is exactly the stack underflow described above.
        /// </summary>
        /// <summary>
        /// The IL evaluation-stack numeric kind of a BasicLang type — <c>i4</c>, <c>i8</c>, <c>r4</c>,
        /// <c>r8</c> — or null for anything this coercion does not handle (Decimal, unsigned,
        /// Char, String, Boolean, Object, a user type). Byte and Short live as int32 on the stack.
        /// </summary>
        private static string NumericKind(TypeInfo type) => type?.Name switch
        {
            "Byte" or "Short" or "Integer" => "i4",
            "Long" => "i8",
            "Single" => "r4",
            "Double" => "r8",
            _ => null,
        };

        private static int NumericRank(string kind) => kind switch
        {
            "i4" => 0,
            "i8" => 1,
            "r4" => 2,
            "r8" => 3,
            _ => -1,
        };

        /// <summary>The wider of two operands' numeric kinds, or null unless BOTH are numeric.</summary>
        private static string WiderNumericKind(IRValue left, IRValue right)
        {
            var l = NumericKind(left?.Type);
            var r = NumericKind(right?.Type);
            if (l == null || r == null) return null;
            return NumericRank(l) >= NumericRank(r) ? l : r;
        }

        /// <summary>
        /// The numeric kind a binary operation COMPUTES in, which both operands must be converted
        /// to before the opcode (ADR-0004 D4).
        ///
        /// <para>⛔ WITHOUT THIS, IL arithmetic ran on whatever the operands happened to be.
        /// <c>add</c>/<c>mul</c>/<c>rem</c> over an int32 and a float64 is not a conversion in IL —
        /// it is undefined, and .NET Core does not verify: <c>2 * &lt;Double call&gt;</c> printed
        /// <c>1E-323</c>, Integer + Double printed 4.4E-323, Single + Integer 32775, and several
        /// mixes were InvalidProgramException. The IR only inserts casts for <c>/</c>; every other
        /// mix reached this backend raw. The arithmetic ops convert to the IR's RESULT type — so
        /// <c>\</c>, whose result is integral, converts floating operands DOWN (rounding half to
        /// even, as the IRCast narrowing does) before an integer <c>div</c>. A comparison, whose
        /// result is Boolean, converts to the wider operand. Shifts are excluded (the count stays
        /// int32 whatever the shifted type is), and so are the logical ops and concatenation.</para>
        /// </summary>
        private static string BinaryOperandKind(IRBinaryOp binaryOp)
        {
            switch (binaryOp.Operation)
            {
                case BinaryOpKind.Add:
                case BinaryOpKind.Sub:
                case BinaryOpKind.Mul:
                case BinaryOpKind.Div:
                case BinaryOpKind.Mod:
                case BinaryOpKind.IntDiv:
                case BinaryOpKind.BitwiseAnd:
                case BinaryOpKind.BitwiseOr:
                case BinaryOpKind.Xor:
                    return NumericKind(binaryOp.Type);
                case BinaryOpKind.Eq:
                case BinaryOpKind.Ne:
                case BinaryOpKind.Lt:
                case BinaryOpKind.Le:
                case BinaryOpKind.Gt:
                case BinaryOpKind.Ge:
                    return WiderNumericKind(binaryOp.Left, binaryOp.Right);
                default:
                    return null;
            }
        }

        /// <summary>
        /// Converts the value on top of the stack — <paramref name="operand"/>, just loaded — to
        /// <paramref name="targetKind"/>. Nothing when either side is not a handled numeric or the
        /// kinds already agree, so an operand the IR already cast (e.g. the Double casts it puts
        /// around <c>/</c>) is never converted twice. Floating → integral rounds half to even
        /// first, exactly as <see cref="Visit(IRCast)"/> does, because <c>conv.i*</c> truncates.
        /// </summary>
        private void EmitNumericCoercion(IRValue operand, string targetKind)
        {
            var sourceKind = NumericKind(operand?.Type);
            if (targetKind == null || sourceKind == null || sourceKind == targetKind) return;

            var sourceIsFloating = sourceKind is "r4" or "r8";
            switch (targetKind)
            {
                case "r8":
                    WriteLine("    conv.r8");
                    break;
                case "r4":
                    WriteLine("    conv.r4");
                    break;
                case "i8":
                case "i4":
                    if (sourceIsFloating)
                    {
                        WriteLine("    conv.r8");
                        WriteLine("    call float64 [mscorlib]System.Math::Round(float64)");
                    }
                    WriteLine(targetKind == "i8" ? "    conv.i8" : "    conv.i4");
                    break;
            }
        }

        /// <summary>A floating source, i.e. one a narrowing has something to round from.</summary>
        private static bool IsFloatingType(TypeInfo type) => type?.Name switch
        {
            "Double" or "Single" => true,
            _ => false,
        };

        /// <summary>
        /// A narrowing target that rounds. Single/Double are absent because widening to them is
        /// not a narrowing, and Boolean because it is a zero test rather than a numeric conversion.
        /// </summary>
        private static bool IsRoundingTarget(string loweredTargetName) => loweredTargetName switch
        {
            "integer" or "long" or "byte" or "short" or "char" => true,
            _ => false,
        };

        /// <summary>
        /// True when a conversion intrinsic's argument is a floating type, i.e. when there is
        /// something to round. Single is widened to float64 by the caller before the call, because
        /// <c>Convert::ToInt32</c> is overloaded per CLR type and IL names one exact overload.
        /// </summary>
        private static bool IsFloatingArgument(IRValue value) => value?.Type?.Name switch
        {
            "Double" or "Single" => true,
            _ => false,
        };

        private static string ResolveStdLibArm(string funcName)
        {
            var lower = funcName?.ToLower() ?? "";
            return DottedStdLibAliases.TryGetValue(lower, out var aliased) ? aliased : lower;
        }

        private static readonly Dictionary<string, string> DottedStdLibAliases =
            new(StringComparer.Ordinal)
            {
                ["console.writeline"] = "printline",
                ["console.write"] = "print",
                ["console.readline"] = "readline",
            };

        /// <summary>One recorded overload: the IL parameter specs it takes and the spec it returns.</summary>
        private sealed record NetStaticOverload(string ReturnSpec, string[] ParameterSpecs);

        /// <summary>
        /// The IL primitives that are VALUE types, so a reference-typed slot needs them boxed.
        /// <c>string</c> and <c>object</c> are deliberately absent — they are IL keywords but
        /// reference types already, and boxing one is not a no-op to reason about.
        /// </summary>
        private static readonly HashSet<string> BoxableSpecs = new(StringComparer.Ordinal)
        {
            "int8", "uint8", "int16", "uint16", "int32", "uint32", "int64", "uint64",
            "float32", "float64", "bool", "char",
        };

        /// <summary>
        /// Per-argument conversion opcodes for the overload the last resolve selected; an entry is
        /// null where the argument already matched. Set by <see cref="TryResolveNetStaticCall"/>
        /// and consumed immediately by its caller.
        /// </summary>
        private string[] _netStaticConversions = System.Array.Empty<string>();

        /// <summary>
        /// The integer widenings that cannot lose a value, and the opcode each needs.
        ///
        /// <para>⛔ <c>int64</c> and <c>uint64</c> are deliberately absent. They widen to
        /// <c>float64</c> in IL, but above 2^53 the conversion ROUNDS — silently returning a
        /// different number than the caller passed. Everything here fits in a double exactly.</para>
        /// </summary>
        private static readonly Dictionary<(string From, string To), string> LosslessWidenings =
            new()
            {
                [("int8", "float64")] = "conv.r8",
                [("uint8", "float64")] = "conv.r8",
                [("int16", "float64")] = "conv.r8",
                [("uint16", "float64")] = "conv.r8",
                [("int32", "float64")] = "conv.r8",
                [("uint32", "float64")] = "conv.r8",
                [("int8", "int32")] = "conv.i4",
                [("uint8", "int32")] = "conv.i4",
                [("int16", "int32")] = "conv.i4",
                [("uint16", "int32")] = "conv.i4",
                [("int32", "int64")] = "conv.i8",
                [("uint32", "int64")] = "conv.i8",
            };

        private static bool TryLosslessWidening(string from, string to, out string opcode) =>
            LosslessWidenings.TryGetValue((from, to), out opcode);

        /// <summary>
        /// True when a value of <paramref name="valueSpec"/> has to be boxed to live in a slot
        /// declared <paramref name="slotSpec"/>, with the token <c>box</c> needs.
        /// </summary>
        private static bool NeedsBoxingInto(string slotSpec, string valueSpec, out string boxToken)
        {
            boxToken = null;
            if (!BoxableSpecs.Contains(valueSpec)) return false;
            if (BoxableSpecs.Contains(slotSpec)) return false;   // value into value: no bridge
            return PrimitiveTokens.TryGetValue(valueSpec, out boxToken);
        }

        /// <summary>
        /// The .NET STATIC members this backend can emit, and the exact IL signature of each.
        ///
        /// <para><b>MSIL emits these DIRECTLY</b> — <c>call float64 [mscorlib]System.Math::Sqrt(float64)</c>
        /// — rather than through the blnet .NET proxy that the C++ backend uses. That is not a
        /// preference between two workable routes. The proxy is a NATIVE C ABI bridge:
        /// <c>[UnmanagedCallersOnly]</c> exports on a Native AOT shim reached through a
        /// function-pointer table, which exists because native code has no other way into .NET.
        /// Managed code cannot call an <c>UnmanagedCallersOnly</c> method at all, so an MSIL
        /// program could only reach the proxy by P/Invoking the native export so it could call
        /// back into the CLR — for members the CLR already offers — and every emitted binary would
        /// then depend on the shim being built and deployed. (<c>ResolvedNetTarget</c>, the
        /// descriptor that drives proxy lowering, is also null on this path: the resolver is not
        /// engaged for a plain compilation.)</para>
        ///
        /// <para>⛔ <b>Keyed on the FULL dotted name, and that is load-bearing.</b> The obvious
        /// shortcut — strip the <c>Type.</c> prefix and match the member alone — routes
        /// <c>Decimal.Round</c> onto <c>Math.Round</c>: a silent mis-emission rather than a missing
        /// one. Here <c>Decimal.Round</c> is simply absent and is refused.</para>
        ///
        /// <para><b>Overloads are matched on argument IL specs, never on count alone.</b>
        /// <c>Math.Abs</c> has int32, int64 and float64 forms that differ only in signature;
        /// picking by arity would emit a call that binds to the wrong one or to nothing.</para>
        ///
        /// <para>Deliberately narrow, on the same principle as <c>CollectionMembers</c> and
        /// <c>ExceptionMembers</c>: a guessed signature assembles cleanly — ilasm does not resolve
        /// member references — and fails at run time with MissingMethodException.</para>
        /// </summary>
        private static readonly Dictionary<string, (string Token, NetStaticOverload[] Overloads)> NetStaticMembers =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["Math.Sqrt"] = ("[mscorlib]System.Math", new[] { new NetStaticOverload("float64", new[] { "float64" }) }),
                ["Math.Floor"] = ("[mscorlib]System.Math", new[] { new NetStaticOverload("float64", new[] { "float64" }) }),
                ["Math.Ceiling"] = ("[mscorlib]System.Math", new[] { new NetStaticOverload("float64", new[] { "float64" }) }),
                ["Math.Pow"] = ("[mscorlib]System.Math", new[] { new NetStaticOverload("float64", new[] { "float64", "float64" }) }),
                ["Math.Abs"] = ("[mscorlib]System.Math", new[]
                {
                    new NetStaticOverload("int32", new[] { "int32" }),
                    new NetStaticOverload("int64", new[] { "int64" }),
                    new NetStaticOverload("float64", new[] { "float64" }),
                }),
                ["Math.Min"] = ("[mscorlib]System.Math", new[]
                {
                    new NetStaticOverload("int32", new[] { "int32", "int32" }),
                    new NetStaticOverload("int64", new[] { "int64", "int64" }),
                    new NetStaticOverload("float64", new[] { "float64", "float64" }),
                }),
                ["Math.Max"] = ("[mscorlib]System.Math", new[]
                {
                    new NetStaticOverload("int32", new[] { "int32", "int32" }),
                    new NetStaticOverload("int64", new[] { "int64", "int64" }),
                    new NetStaticOverload("float64", new[] { "float64", "float64" }),
                }),
                ["Math.Round"] = ("[mscorlib]System.Math", new[] { new NetStaticOverload("float64", new[] { "float64" }) }),

                ["Convert.ToInt32"] = ("[mscorlib]System.Convert", new[]
                {
                    new NetStaticOverload("int32", new[] { "string" }),
                    new NetStaticOverload("int32", new[] { "float64" }),
                }),
                ["Convert.ToDouble"] = ("[mscorlib]System.Convert", new[]
                {
                    new NetStaticOverload("float64", new[] { "string" }),
                    new NetStaticOverload("float64", new[] { "int32" }),
                }),

                // ⛔ No String.* rows: `String` is a reserved type keyword and the parser rejects
                // it at the start of an expression, so `String.IsNullOrEmpty(s)` never reaches
                // this backend on ANY target. A row here would be untestable by the round-trip
                // contract the refusal message promises.
            };

        /// <summary>
        /// Resolves a dotted static call to its recorded IL signature, matching the overload on
        /// argument specs. Returns false when the receiver is not a .NET static type at all — that
        /// call belongs to some other arm — and throws when the type IS one but the member or the
        /// argument shape has no recorded signature.
        /// </summary>
        private bool TryResolveNetStaticCall(
            string funcName, List<IRValue> args, out string token, out NetStaticOverload overload)
        {
            token = null;
            overload = null;

            var dot = funcName?.IndexOf('.') ?? -1;
            if (dot <= 0) return false;

            var typeName = funcName.Substring(0, dot);
            if (!IRBuilder.IsKnownNetStaticTypeName(typeName)) return false;

            // A recognized .NET static type whose member routes elsewhere (the Console aliases)
            // is not this arm's business.
            if (DottedStdLibAliases.ContainsKey(funcName.ToLowerInvariant())) return false;

            if (!NetStaticMembers.TryGetValue(funcName, out var entry))
            {
                throw new ForeignFeatureException(
                    $"MSIL: '{funcName}' is outside the supported .NET static surface. The members "
                    + "whose IL signatures are recorded are emitted as direct calls; anything else "
                    + "would need a guessed signature, which assembles and then fails with "
                    + "MissingMethodException at run time. ⛔ Matching on the member name alone "
                    + "instead would be worse: it routes Decimal.Round onto Math.Round, a silent "
                    + "wrong answer. Add a row to MSILCodeGenerator.NetStaticMembers plus a "
                    + "round-trip test to widen the set.");
            }

            var argumentSpecs = args.Select(a => IlTypeSpec(a.Type)).ToArray();

            // Exact match first, so a widening is never preferred over a signature that already
            // fits — `Math.Abs(-7)` must pick the int32 form, not widen to the float64 one.
            overload = entry.Overloads.FirstOrDefault(
                o => o.ParameterSpecs.Length == argumentSpecs.Length
                     && o.ParameterSpecs.Zip(argumentSpecs, (p, a) => p == a).All(match => match));

            if (overload != null)
            {
                _netStaticConversions = new string[argumentSpecs.Length];
                token = entry.Token;
                return true;
            }

            // Then a match reachable by LOSSLESS widening. `Math.Sqrt(16)` is natural to write and
            // the C# backend accepts it (C# widens implicitly), so refusing it would be a
            // divergence between two backends over an integer literal. Only widenings that cannot
            // lose a value are allowed — int64/uint64 to float64 is NOT one of them above 2^53,
            // and silently rounding a caller's value is the class of defect this file is full of.
            foreach (var candidate in entry.Overloads)
            {
                if (candidate.ParameterSpecs.Length != argumentSpecs.Length) continue;

                var conversions = new string[argumentSpecs.Length];
                var usable = true;
                for (var i = 0; i < argumentSpecs.Length; i++)
                {
                    if (candidate.ParameterSpecs[i] == argumentSpecs[i]) continue;
                    if (TryLosslessWidening(argumentSpecs[i], candidate.ParameterSpecs[i], out conversions[i])) continue;
                    usable = false;
                    break;
                }

                if (!usable) continue;

                _netStaticConversions = conversions;
                overload = candidate;
                token = entry.Token;
                return true;
            }

            if (overload == null)
            {
                var got = argumentSpecs.Length == 0 ? "no arguments" : string.Join(", ", argumentSpecs);
                var offered = string.Join(" | ", entry.Overloads.Select(o => string.Join(", ", o.ParameterSpecs)));
                throw new ForeignFeatureException(
                    $"MSIL: '{funcName}' has no recorded overload taking ({got}). Recorded: "
                    + $"({offered}). Overloads are matched on argument types, never on count — "
                    + "Math.Abs has int32, int64 and float64 forms that differ only in signature, "
                    + "so picking by arity emits a call that binds to the wrong one or to nothing.");
            }

            token = entry.Token;
            return true;
        }

        /// <summary>
        /// Stdlib arms that emit a STATEMENT, not a value — their IL pushes nothing, so the
        /// caller must not store a result even when the IR types the call as value-returning.
        ///
        /// <para>⛔ This is the second half of the Console fix and is not optional. <c>IRBuilder</c>
        /// types <c>Console.WriteLine(x)</c> as returning <c>Object</c>, so routing it to the
        /// <c>printline</c> arm — which correctly emits <c>call void …Console::WriteLine(string)</c>
        /// — left the caller emitting a <c>stloc</c> for a value nothing pushed. That is a stack
        /// underflow: ilasm accepts it and the CLR rejects the method with
        /// InvalidProgramException. Fixing only the call name turned a MissingMethodException
        /// into an InvalidProgramException.</para>
        ///
        /// <para><c>CppCodeGenerator.IsVoidStdLibCall</c> is the same predicate for the same
        /// reason, and carries the same names. The two lists are separate because the backends
        /// share no emission code, so a name added to one arm set needs adding to both.</para>
        /// </summary>
        private static bool IsVoidStdLibArm(string loweredName) =>
            loweredName is "print" or "printline" or "randomize";

        /// <summary>
        /// The IL spec an arm of <see cref="TryEmitStdLibCall"/> really left on the stack, for the
        /// arms where the IR's type for the call does not say. Null means "the IR type is right",
        /// which is every arm but one. Set by the arm, consumed immediately by its caller.
        ///
        /// <para>⛔ <c>Asc</c> and <c>Chr</c> are the two string intrinsics
        /// <c>SemanticAnalyzer.RegisterStdLibFunctions</c> never registered, so the front end types
        /// them <c>Object</c> and the destination slot is declared <c>object</c>. <c>Chr</c> is
        /// unaffected — it yields a <c>string</c>, already a reference. <c>Asc</c> yields an
        /// <c>int32</c>, and storing a raw integer into an object slot hands the runtime a number
        /// as a reference: the same gap the .NET-static arm boxes across, and the same fix.</para>
        ///
        /// <para>⚠ Deliberately NOT fixed by registering the two names in the front end. That
        /// table is read by all five backends, and re-typing a call that currently comes out
        /// <c>Object</c> would change what C#, C++, JavaScript and LLVM emit for a shape that
        /// works on three of them today. Bridging it in the one backend that spells IL types keeps
        /// the blast radius here. Registering them is the better fix and belongs with whoever owns
        /// the cross-backend stdlib table.</para>
        /// </summary>
        private string _stdLibResultSpec;

        /// <summary>
        /// The IL specs <c>conv.u2</c> can narrow to a <c>char</c>: the numeric ones. <c>bool</c>
        /// is absent deliberately — <c>conv.u2</c> would happily turn True into U+0001, and the C#
        /// backend refuses the same program (<c>((char)True)</c> is CS0030). <c>string</c> and
        /// <c>object</c> are absent because they are references, and narrowing a reference is the
        /// silent-wrong-answer case <see cref="RequireChrArgument"/> exists to stop.
        /// </summary>
        private static readonly HashSet<string> ChrArgumentSpecs = new(StringComparer.Ordinal)
        {
            "int8", "uint8", "int16", "uint16", "int32", "uint32", "int64", "uint64",
            "float32", "float64", "char",
        };

        /// <summary>
        /// ⛔ <c>Chr</c> and <c>Asc</c> are the two string intrinsics
        /// <c>SemanticAnalyzer.RegisterStdLibFunctions</c> never registered, so — unlike Mid, Left,
        /// Right, UCase, LCase, Trim, Replace, InStr and Len — <b>the front end type-checks nothing
        /// about their arguments</b>. Every other arm can trust that arg 0 is a String because
        /// semantic analysis already said so; these two cannot.
        ///
        /// <para>Measured on the CLI, compiled, assembled and run, with the guards removed:</para>
        /// <list type="bullet">
        /// <item><c>Chr("x")</c> emitted <c>ldstr "x"; conv.u2; call Char::ToString(char)</c>,
        /// ran clean, and printed <b>Ԙ</b> — narrowing a string REFERENCE to a character. The C#
        /// backend refuses the same program.</item>
        /// <item><c>Chr(Asc("A"))</c> printed <b>鍀</b>: Asc's result is typed Object, so it
        /// arrives boxed and <c>conv.u2</c> narrows the box pointer.</item>
        /// <item><c>Asc(5)</c> emitted <c>ldc.i4.5; ldc.i4.0; callvirt String::get_Chars</c> and
        /// died with NullReferenceException — an integer used as a string reference.</item>
        /// </list>
        ///
        /// <para>⛔ <b>A clean run with a wrong answer is worse than the gap these arms close</b>,
        /// so a mistyped argument is refused here. Before these arms existed the same programs
        /// failed loudly with MissingMethodException; turning that into garbage output would be a
        /// regression dressed as a feature.</para>
        /// </summary>
        private void RequireChrArgument(IRValue argument)
        {
            var spec = IlTypeSpec(argument?.Type);
            if (ChrArgumentSpecs.Contains(spec)) return;

            throw new ForeignFeatureException(
                $"MSIL: 'Chr' needs a numeric argument; this one is '{spec}'. Chr is not registered "
                + "in SemanticAnalyzer.RegisterStdLibFunctions, so the front end does not check its "
                + "argument and nothing upstream rejects Chr(\"x\"). Emitting it anyway means "
                + "conv.u2 narrowing a REFERENCE to a character: measured, Chr(\"x\") ran clean and "
                + "printed a Cyrillic glyph, and Chr(Asc(\"A\")) — whose argument is boxed because "
                + "Asc is typed Object — printed a CJK one. The C# backend refuses the same program "
                + "(CS0030). Register Chr in the front end to fix this properly for all five "
                + "backends.");
        }

        /// <summary>
        /// The companion guard for <c>Asc</c>. <c>string</c> is the intended argument; <c>object</c>
        /// is allowed because it is how the IR types any unregistered intrinsic's result and the
        /// <c>callvirt</c> dispatches correctly when the object really is a string — measured,
        /// <c>Asc(Chr(66))</c> answers 66. A VALUE-typed argument can never be a string, so it is
        /// refused rather than emitted as the NullReferenceException it would become.
        /// </summary>
        private void RequireAscArgument(IRValue argument)
        {
            var spec = IlTypeSpec(argument?.Type);
            if (spec == "string" || spec == "object") return;

            throw new ForeignFeatureException(
                $"MSIL: 'Asc' needs a String argument; this one is '{spec}'. Asc is not registered "
                + "in SemanticAnalyzer.RegisterStdLibFunctions, so the front end does not check its "
                + "argument and nothing upstream rejects Asc(5). Emitting it anyway calls "
                + "String::get_Chars on a value type: measured, Asc(5) assembled and died with "
                + "NullReferenceException. Register Asc in the front end to fix this properly for "
                + "all five backends.");
        }

        private bool TryEmitStdLibCall(string funcName, List<IRValue> args, bool hasReturn)
        {
            var lower = ResolveStdLibArm(funcName);
            _stdLibResultSpec = null;

            switch (lower)
            {
                case "printline":
                    if (args.Count > 0)
                    {
                        EmitLoadValue(args[0]);
                        var argType = MapType(args[0].Type);

                        if (argType == "string")
                        {
                            WriteLine("    call void [mscorlib]System.Console::WriteLine(string)");
                        }
                        else if (argType == "int32")
                        {
                            WriteLine("    call void [mscorlib]System.Console::WriteLine(int32)");
                        }
                        else if (argType == "int64")
                        {
                            WriteLine("    call void [mscorlib]System.Console::WriteLine(int64)");
                        }
                        else if (argType == "float64" || argType == "float32")
                        {
                            WriteLine("    call void [mscorlib]System.Console::WriteLine(float64)");
                        }
                        else if (argType == "bool")
                        {
                            WriteLine("    call void [mscorlib]System.Console::WriteLine(bool)");
                        }
                        else
                        {
                            WriteLine("    box object");
                            WriteLine("    call void [mscorlib]System.Console::WriteLine(object)");
                        }
                        _currentStack--;
                    }
                    else
                    {
                        WriteLine("    call void [mscorlib]System.Console::WriteLine()");
                    }
                    return true;

                case "print":
                    if (args.Count > 0)
                    {
                        EmitLoadValue(args[0]);
                        var argType = MapType(args[0].Type);
                        WriteLine($"    call void [mscorlib]System.Console::Write({argType})");
                        _currentStack--;
                    }
                    return true;

                case "readline":
                    WriteLine("    call string [mscorlib]System.Console::ReadLine()");
                    _currentStack++;
                    return true;

                case "sqrt":
                    EmitLoadValue(args[0]);
                    WriteLine("    call float64 [mscorlib]System.Math::Sqrt(float64)");
                    return true;

                case "pow":
                    EmitLoadValue(args[0]);
                    EmitLoadValue(args[1]);
                    WriteLine("    call float64 [mscorlib]System.Math::Pow(float64, float64)");
                    _currentStack--;
                    return true;

                case "sin":
                    EmitLoadValue(args[0]);
                    WriteLine("    call float64 [mscorlib]System.Math::Sin(float64)");
                    return true;

                case "cos":
                    EmitLoadValue(args[0]);
                    WriteLine("    call float64 [mscorlib]System.Math::Cos(float64)");
                    return true;

                case "tan":
                    EmitLoadValue(args[0]);
                    WriteLine("    call float64 [mscorlib]System.Math::Tan(float64)");
                    return true;

                case "log":
                    EmitLoadValue(args[0]);
                    WriteLine("    call float64 [mscorlib]System.Math::Log(float64)");
                    return true;

                case "exp":
                    EmitLoadValue(args[0]);
                    WriteLine("    call float64 [mscorlib]System.Math::Exp(float64)");
                    return true;

                case "floor":
                    EmitLoadValue(args[0]);
                    WriteLine("    call float64 [mscorlib]System.Math::Floor(float64)");
                    return true;

                case "ceiling":
                    EmitLoadValue(args[0]);
                    WriteLine("    call float64 [mscorlib]System.Math::Ceiling(float64)");
                    return true;

                case "abs":
                    EmitLoadValue(args[0]);
                    WriteLine("    call float64 [mscorlib]System.Math::Abs(float64)");
                    return true;

                case "round":
                    EmitLoadValue(args[0]);
                    WriteLine("    call float64 [mscorlib]System.Math::Round(float64)");
                    return true;

                case "min":
                    EmitLoadValue(args[0]);
                    EmitLoadValue(args[1]);
                    WriteLine("    call float64 [mscorlib]System.Math::Min(float64, float64)");
                    _currentStack--;
                    return true;

                case "max":
                    EmitLoadValue(args[0]);
                    EmitLoadValue(args[1]);
                    WriteLine("    call float64 [mscorlib]System.Math::Max(float64, float64)");
                    _currentStack--;
                    return true;

                case "len":
                    EmitLoadValue(args[0]);
                    WriteLine("    callvirt instance int32 [mscorlib]System.String::get_Length()");
                    return true;

                // ============================================================================
                // The VB string intrinsics. ⛔ EVERY ONE OF THESE EMITS THE IL THAT THE C#
                // BACKEND'S OUTPUT COMPILES TO, INSTRUCTION FOR INSTRUCTION — see
                // CSharpStdLibProvider.EmitMid/EmitLeft/… — AND THAT IS THE WHOLE CONTRACT.
                //
                // ⚠ It is NOT the same thing as "VB semantics", and the difference is not
                // theoretical. Measured on this front end, all four backends, compiled and run:
                //
                //   Mid("abcdef", 5, 10)  C#: ArgumentOutOfRangeException   JS: "ef"
                //   Mid("abc", 0, 2)      C#: ArgumentOutOfRangeException   JS: "c"
                //   Left("abcdef", 10)    C#: ArgumentOutOfRangeException   JS: "abcdef"
                //   Right("abcdef", 10)   C#: ArgumentOutOfRangeException   JS: "abcdef"
                //   Asc("")               C#: IndexOutOfRangeException      JS: NaN
                //
                // Real VB clamps and returns the short string; BasicLang's C# backend does not,
                // and `BasicLang.Runtime.BasicLangRuntime.Mid` — which DOES clamp — is dead code
                // that no backend calls. So there is no single existing answer to copy. MSIL is
                // the OTHER .NET backend, and the precedent this file already set for a .NET/.NET
                // split is the `cint` arm below: when the two disagreed, MSIL was changed to match
                // C#. Matching it here keeps the two .NET targets byte-identical on every input,
                // including the throwing ones, and leaves the clamping question — which belongs to
                // all five backends at once — to be settled in one place rather than invented here.
                //
                // ⛔ Do NOT "fix" one of these into clamping on its own. A clamp on MSIL alone
                // turns an exception both .NET backends agree on into a silent different answer
                // on one of them, which is strictly worse than the gap.

                // C#: str.Substring(start - 1, length). `Mid` is 1-BASED and the `- 1` is the
                // whole reason this cannot be a bare Substring; dropping it is an off-by-one that
                // assembles, runs, and returns the wrong characters.
                // ⚠ Only the 3-argument form exists: SemanticAnalyzer registers Mid with exactly
                // three parameters, so `Mid(s, 3)` is rejected by the front end on every backend
                // ("Function 'Mid' expects 3 argument(s), got 2") and never reaches any emitter.
                case "mid":
                    EmitLoadValue(args[0]);
                    EmitLoadValue(args[1]);
                    WriteLine("    ldc.i4.1");
                    WriteLine("    sub");
                    EmitLoadValue(args[2]);
                    WriteLine("    callvirt instance string [mscorlib]System.String::Substring(int32, int32)");
                    _currentStack -= 2;
                    return true;

                // C#: str.Substring(0, length).
                case "left":
                    EmitLoadValue(args[0]);
                    WriteLine("    ldc.i4.0");
                    EmitLoadValue(args[1]);
                    WriteLine("    callvirt instance string [mscorlib]System.String::Substring(int32, int32)");
                    _currentStack -= 2;
                    return true;

                // C#: str.Substring(str.Length - length) — which needs the receiver TWICE.
                //
                // ⛔ `dup`, not a second EmitLoadValue, and this is a deliberate divergence from
                // the C# backend rather than an oversight. `EmitRight` interpolates `{str}` twice,
                // so the receiver EXPRESSION is evaluated twice: measured on
                // `Right(Tag(), 2)` where Tag prints, C# printed "tag" TWICE while JavaScript and
                // C++ printed it once. Two of the three agree, a side effect happening twice is a
                // defect by any reading, and `dup` is also the only spelling here that cannot
                // duplicate work. The C# backend's double evaluation is recorded as its own bug.
                case "right":
                    EmitLoadValue(args[0]);
                    WriteLine("    dup");
                    WriteLine("    callvirt instance int32 [mscorlib]System.String::get_Length()");
                    EmitLoadValue(args[1]);
                    WriteLine("    sub");
                    WriteLine("    callvirt instance string [mscorlib]System.String::Substring(int32)");
                    _currentStack--;
                    return true;

                case "ucase":
                    EmitLoadValue(args[0]);
                    WriteLine("    callvirt instance string [mscorlib]System.String::ToUpper()");
                    return true;

                case "lcase":
                    EmitLoadValue(args[0]);
                    WriteLine("    callvirt instance string [mscorlib]System.String::ToLower()");
                    return true;

                case "trim":
                    EmitLoadValue(args[0]);
                    WriteLine("    callvirt instance string [mscorlib]System.String::Trim()");
                    return true;

                // C#: str.Replace(find, replaceWith) — which replaces EVERY occurrence, the
                // answer JavaScript and C++ also give ("banana"/"a"/"o" → "bonono" on all three).
                case "replace":
                    EmitLoadValue(args[0]);
                    EmitLoadValue(args[1]);
                    EmitLoadValue(args[2]);
                    WriteLine("    callvirt instance string [mscorlib]System.String::Replace(string, string)");
                    _currentStack -= 2;
                    return true;

                // C#: (str.IndexOf(search) + 1). ⛔ The `+ 1` is what makes InStr 1-BASED and what
                // makes NOT FOUND come out as 0 rather than .NET's -1 — one addition carrying two
                // separate parts of the contract. Measured 3 / 1 / 0 on found-middle / found-first
                // / absent, identically on C# and JavaScript.
                case "instr":
                    EmitLoadValue(args[0]);
                    EmitLoadValue(args[1]);
                    WriteLine("    callvirt instance int32 [mscorlib]System.String::IndexOf(string)");
                    WriteLine("    ldc.i4.1");
                    WriteLine("    add");
                    _currentStack--;
                    return true;

                // C#: ((char)code).ToString(). `Char::ToString(char)` is the static one-argument
                // overload, so no box/callvirt pair is needed. `conv.u2` performs the (char) cast:
                // without it the int32 on the stack does not match the char parameter.
                case "chr":
                    RequireChrArgument(args[0]);
                    EmitLoadValue(args[0]);
                    WriteLine("    conv.u2");
                    WriteLine("    call string [mscorlib]System.Char::ToString(char)");
                    return true;

                // C#: (int)str[0], i.e. the Chars indexer at 0. A char on the evaluation stack IS
                // an int32, so the cast needs no opcode — but the IR types this call Object (Asc
                // and Chr are the two intrinsics SemanticAnalyzer never registered), so the value
                // has to be bridged into a reference slot. See _stdLibResultSpec.
                case "asc":
                    RequireAscArgument(args[0]);
                    EmitLoadValue(args[0]);
                    WriteLine("    ldc.i4.0");
                    WriteLine("    callvirt instance char [mscorlib]System.String::get_Chars(int32)");
                    _stdLibResultSpec = "int32";
                    return true;

                // ⛔ ROUNDS HALF-TO-EVEN, and `conv.i4` alone does NOT — it truncates, which is
                // not what CInt means. Measured across the backends on
                // CInt(7.5)/CInt(8.5)/CInt(7.9)/CInt(-7.5): C# emits Convert.ToInt32 and printed
                // 8,8,8,-8 (the VB answer), while MSIL, JavaScript and C++ all printed 7,8,7,-7.
                // One language, two answers. Convert.ToInt32 is exactly
                // Math.Round(x, MidpointRounding.ToEven) — verified on ten values including the
                // midpoints 8.5 -> 8 and 2.5 -> 2 that separate it from AwayFromZero.
                //
                // ⚠ Only for a FLOATING argument, and the reason differs per arm — measured,
                // because the tempting reason is wrong. Routing an integral through the float64
                // overload DOES verify (a `conv.r8` in front makes it legal IL), and for CInt it
                // is even equivalent: a 32-bit Integer is exact in a double, so a mutation that
                // rounds the integral case too passes every test. The guard stays on `cint` as an
                // early-out, not a correctness claim.
                //
                // ⛔ On `clng` it IS correctness. A Long above 2^53 does not survive a double
                // round trip: measured, CLng(9007199254740993) answers itself today and would
                // answer 9007199254740992 through float64.
                case "cint":
                    EmitLoadValue(args[0]);
                    if (IsFloatingArgument(args[0]))
                    {
                        WriteLine("    conv.r8");
                        WriteLine("    call int32 [mscorlib]System.Convert::ToInt32(float64)");
                    }
                    else
                    {
                        WriteLine("    conv.i4");
                    }
                    return true;

                case "clng":
                    EmitLoadValue(args[0]);
                    if (IsFloatingArgument(args[0]))
                    {
                        WriteLine("    conv.r8");
                        WriteLine("    call int64 [mscorlib]System.Convert::ToInt64(float64)");
                    }
                    else
                    {
                        WriteLine("    conv.i8");
                    }
                    return true;

                case "cdbl":
                    EmitLoadValue(args[0]);
                    WriteLine("    conv.r8");
                    return true;

                case "csng":
                    EmitLoadValue(args[0]);
                    WriteLine("    conv.r4");
                    return true;

                case "cstr":
                    EmitLoadValue(args[0]);
                    var srcType = MapType(args[0].Type);
                    if (srcType != "string")
                    {
                        // box takes a TYPE TOKEN. `box [int32]` named an assembly, not a type.
                        WriteLine($"    box {IlTypeToken(args[0].Type)}");
                        WriteLine("    callvirt instance string [mscorlib]System.Object::ToString()");
                    }
                    return true;

                case "rnd":
                    // Use System.Random - simplified version
                    WriteLine("    newobj instance void [mscorlib]System.Random::.ctor()");
                    WriteLine("    callvirt instance float64 [mscorlib]System.Random::NextDouble()");
                    _currentStack++;
                    return true;

                default:
                    return false;
            }
        }

        public override void Visit(IRReturn ret)
        {
            if (_regionBlocks == null)
            {
                if (ret.Value != null)
                {
                    EmitLoadValue(ret.Value);
                }
                WriteLine("    ret");
                return;
            }

            // `ret` is ILLEGAL inside a protected region — the CLR rejects the whole method, which
            // is what `Return` inside a Try used to produce. Lower it the way a real compiler does:
            // park the value in a slot and `leave` to the one exit that owns the `ret`. Going out
            // through `leave` is also what runs an enclosing `finally`, so this is not merely legal
            // but the only spelling with the right semantics.
            if (_regionIsFinally)
            {
                throw new ForeignFeatureException(
                    "MSIL: 'Return' inside a Finally block has no IL lowering. A finally handler "
                    + "may only be left by endfinally — it cannot return, and it cannot swallow the "
                    + "exception in flight by returning a value. Move the Return after the End Try.");
            }

            if (ret.Value != null)
            {
                if (_methodExitResultLocal < 0)
                {
                    throw new ForeignFeatureException(
                        "MSIL: a value-returning 'Return' inside a Try appeared in a method with no "
                        + "result slot reserved. This is an emitter invariant failure, not a source "
                        + "problem — AllocateExceptionHandlingLocals reserves the slot for every "
                        + "non-void function containing a Try.");
                }
                EmitLoadValue(ret.Value);
                EmitStloc(_methodExitResultLocal);
            }

            _methodExitUsed = true;
            WriteLine($"    leave {_methodExitLabel}");
        }

        public override void Visit(IRBranch branch)
        {
            // ⛔ <c>Exit For</c> and the end of an ordinary iteration are the SAME branch to the
            // SAME block in the IR — <c>IRBuilder</c> gives a loop's break and continue targets
            // one block — and <c>IRBranch.IsLoopExit</c> is the only thing that tells them apart.
            // C++ and JavaScript have both read it since task_4cc381f1; MSIL never did, so
            // <c>Exit For</c> inside a <c>For Each</c> ran as <c>Continue For</c>: measured, a loop
            // over 1,2,3,4 exiting at 3 totalled 7 instead of 3, from a program that ran clean.
            // ⛔ It cannot be recovered positionally — an <c>If</c> in the body produces a merge
            // block that branches to the same place and MUST stay an iteration.
            EmitRegionAwareBranch(branch.Target, branch.IsLoopExit);
        }

        public override void Visit(IRConditionalBranch condBranch)
        {
            EmitLoadValue(condBranch.Condition);
            _currentStack--;

            var trueTarget = condBranch.TrueTarget;
            var falseTarget = condBranch.FalseTarget;

            // An edge to a For Each's continuation, taken from inside its body, is the next
            // ITERATION — and the loop head is always inside whatever region encloses it, so the
            // region test below must not see this target at all. Without this arm an `If` inside
            // a loop body branches out of the loop on its first true test.
            if (IsIterationBranch(trueTarget, out var trueIterationHead))
            {
                WriteLine($"    brtrue {trueIterationHead}");
                EmitRegionAwareBranch(falseTarget);
                return;
            }

            if (!LeavesRegion(trueTarget))
            {
                WriteLine($"    brtrue {SanitizeLabel(trueTarget.Name)}");
                EmitRegionAwareBranch(falseTarget);
                return;
            }

            // `brtrue` cannot cross a protected region's edge — only `leave`/`endfinally` may. Bounce
            // the taken edge through a trampoline INSIDE the region, which then leaves properly.
            var trampoline = $"eh_edge_{_labelCounter++}";
            WriteLine($"    brtrue {trampoline}");
            EmitRegionAwareBranch(falseTarget);
            WriteLine($"  {trampoline}:");
            EmitRegionAwareBranch(trueTarget);
        }

        /// <summary>
        /// True when branching to <paramref name="target"/> would cross out of the region being
        /// emitted. False everywhere outside a region, which is why ordinary code is unaffected.
        /// </summary>
        private bool LeavesRegion(BasicBlock target) =>
            _regionBlocks != null && target != null && !_regionBlocks.Contains(target);

        /// <summary>
        /// True when a branch to <paramref name="target"/> is the end of a <c>For Each</c>
        /// ITERATION rather than the end of the loop, giving the loop head to branch to.
        ///
        /// <para>Only ever true while that loop's body is being emitted — the entry is pushed and
        /// popped around the body — so the identical branch AFTER the loop still goes to the
        /// continuation.</para>
        /// </summary>
        private bool IsIterationBranch(BasicBlock target, out string loopHead)
        {
            loopHead = null;
            return target != null && _foreachContinueLabels.TryGetValue(target, out loopHead);
        }

        /// <summary>
        /// An unconditional transfer to <paramref name="target"/>, spelled the way the CURRENT
        /// region allows: <c>br</c> within the region (or outside any), <c>leave</c> out of a
        /// try/catch, and <c>endfinally</c> out of a finally — where the target is implicit,
        /// because a finally resumes whatever unwinding or <c>leave</c> entered it and cannot
        /// choose its own destination.
        /// </summary>
        /// <param name="isLoopExit">
        /// True for the branch an <c>Exit For</c> emits, which targets the loop's continuation
        /// exactly as the end of an iteration does and must NOT be redirected back to the head.
        /// </param>
        private void EmitRegionAwareBranch(BasicBlock target, bool isLoopExit = false)
        {
            // Checked FIRST, and before the region test: the loop head is inside the region, so
            // classifying this edge as one that leaves would emit `leave` out of a Try for what
            // is only the next iteration of a loop inside it.
            if (!isLoopExit && IsIterationBranch(target, out var iterationHead))
            {
                WriteLine($"    br {iterationHead}");
                return;
            }

            if (!LeavesRegion(target))
            {
                WriteLine($"    br {SanitizeLabel(target.Name)}");
                return;
            }

            WriteLine(_regionIsFinally ? "    endfinally" : $"    leave {SanitizeLabel(target.Name)}");
        }

        /// <summary>
        /// Lowers a <c>Select Case</c> to an ordered chain of comparisons — one test per case in
        /// source order, first match wins, falling through to the Else/default target.
        ///
        /// <para>⛔ <b>Do not "restore" the IL <c>switch</c> instruction here.</b> It was what
        /// this method emitted, and it was wrong twice over. First, IL <c>switch</c> is
        /// <b>index</b>-based: it pops an unsigned int32 <i>i</i> and jumps to the <i>i</i>-th
        /// label, so even a fully populated table sends <c>Case 1, 2, 3</c> to the wrong arms and
        /// cannot express a string, a range, a comparison or a <c>When</c> guard at all. Second,
        /// the table it built came from <see cref="IRSwitch.Cases"/>, and the parser routes EVERY
        /// case value into <see cref="IRSwitch.PatternCases"/> — <c>Cases</c> stays empty (the
        /// same fact <c>ControlFlowGraph</c> and <c>CppCodeGenerator</c> both record). So the
        /// emitted table was <c>switch ()</c>, empty, followed by an UNCONDITIONAL branch to the
        /// default: every input took <c>Case Else</c>, in code that assembled and ran clean. The
        /// legacy <c>Cases</c> list is still walked below, ahead of the patterns, so a future
        /// front end that does populate it keeps working.</para>
        ///
        /// <para>This mirrors <c>CppCodeGenerator.Visit(IRSwitch)</c>, which lowers the same IR to
        /// an if/else-if chain of gotos for the same reason. The pattern VALUES (range bounds,
        /// comparison operands) were already emitted into this block by <c>IRBuilder</c> before
        /// the switch instruction, so re-loading them here cannot re-run a side effect.</para>
        /// </summary>
        public override void Visit(IRSwitch switchInst)
        {
            var subject = switchInst.Value;

            foreach (var (caseValue, target) in switchInst.Cases)
            {
                var noMatch = NextCaseLabel();
                EmitCaseEqualityTest(subject, caseValue, noMatch);
                WriteLine($"    br {SanitizeLabel(target.Name)}");
                WriteLine($"  {noMatch}:");
            }

            foreach (var patternCase in switchInst.PatternCases)
            {
                var noMatch = NextCaseLabel();
                EmitPatternTest(subject, patternCase, noMatch);
                WriteLine($"    br {SanitizeLabel(patternCase.Target.Name)}");
                WriteLine($"  {noMatch}:");
            }

            WriteLine($"    br {SanitizeLabel(switchInst.DefaultTarget.Name)}");
        }

        /// <summary>A fresh IL label for one link of a Select Case comparison chain.</summary>
        private string NextCaseLabel() => $"case_test_{_labelCounter++}";

        /// <summary>
        /// Emits the full test for one pattern case: the value test AND its optional <c>When</c>
        /// guard. Falls through when the case matches; branches to <paramref name="noMatch"/>
        /// when it does not. Leaves the evaluation stack balanced on both paths.
        /// </summary>
        private void EmitPatternTest(IRValue subject, IRPatternCase patternCase, string noMatch)
        {
            EmitPatternValueTest(subject, patternCase, noMatch);

            if (patternCase.WhenGuard != null)
            {
                EmitInlineValue(patternCase.WhenGuard);
                WriteLine($"    brfalse {noMatch}");
                _currentStack--;
            }
        }

        /// <summary>
        /// The value/range/comparison half of a pattern test, without the <c>When</c> guard.
        /// Recursion target for <c>Or</c> alternatives — which is why it is split out, and why
        /// an alternative's own guard is not evaluated here (<c>CppCodeGenerator</c> makes the
        /// same split for the same reason; the parser attaches <c>When</c> to the case, not to
        /// an alternative).
        /// </summary>
        private void EmitPatternValueTest(IRValue subject, IRPatternCase patternCase, string noMatch)
        {
            switch (patternCase)
            {
                case IRConstantPatternCase constantCase:
                    EmitCaseEqualityTest(subject, constantCase.Value, noMatch);
                    break;

                case IRRangePatternCase rangeCase:
                    EmitCaseRelationalTest(subject, rangeCase.LowerBound, ">=", noMatch);
                    EmitCaseRelationalTest(subject, rangeCase.UpperBound, "<=", noMatch);
                    break;

                case IRComparisonPatternCase comparisonCase:
                    EmitCaseRelationalTest(subject, comparisonCase.CompareValue, comparisonCase.Operator, noMatch);
                    break;

                case IRNothingPatternCase:
                    EmitNothingTest(subject, noMatch);
                    break;

                case IROrPatternCase orCase:
                    if (orCase.Alternatives.Count == 0)
                    {
                        WriteLine($"    br {noMatch}");
                        break;
                    }

                    var matched = NextCaseLabel();
                    foreach (var alternative in orCase.Alternatives)
                    {
                        var nextAlternative = NextCaseLabel();
                        EmitPatternValueTest(subject, alternative, nextAlternative);
                        WriteLine($"    br {matched}");
                        WriteLine($"  {nextAlternative}:");
                    }
                    WriteLine($"    br {noMatch}");
                    WriteLine($"  {matched}:");
                    break;

                default:
                    throw new ForeignFeatureException(
                        $"MSIL: the Select Case pattern '{patternCase.GetType().Name}' has no IL "
                        + "lowering. MSIL carries constant, range, comparison, Nothing and Or "
                        + "patterns plus When guards; a type/tuple/binding pattern needs "
                        + "isinst/deconstruction support this backend does not have. Emitting "
                        + "nothing for it would silently route the case to Case Else, so it is "
                        + "refused here instead.");
            }
        }

        /// <summary>
        /// Compares <paramref name="subject"/> with <paramref name="value"/> and branches to
        /// <paramref name="branchTo"/>. By default that is the NO-MATCH edge of a <c>Case x</c>,
        /// so the branch is taken when the two are UNEQUAL; <paramref name="branchWhenEqual"/>
        /// flips it for <c>Case Is &lt;&gt; x</c>, whose no-match edge is equality.
        ///
        /// <para>A string operand goes through <c>String::Equals</c>, not <c>beq</c>/<c>bne.un</c>:
        /// those compare the REFERENCE, so <c>Select Case s</c> over string cases would match only
        /// when the two strings happened to be the same interned object — true for literals the
        /// runtime interned together, false for a string that was built or read at run time. That
        /// is a wrong answer that passes every test written with literals. Numeric and reference
        /// cases use <c>beq</c>/<c>bne.un</c>, bit-exact for integers and NaN-correct for floats.</para>
        /// </summary>
        private void EmitCaseEqualityTest(IRValue subject, IRValue value, string branchTo, bool branchWhenEqual = false)
        {
            EmitLoadValue(subject);
            EmitLoadValue(value);

            if (IsStringOperand(subject) || IsStringOperand(value))
            {
                WriteLine("    call bool [mscorlib]System.String::Equals(string, string)");
                _currentStack--;
                WriteLine($"    {(branchWhenEqual ? "brtrue" : "brfalse")} {branchTo}");
                _currentStack--;
                return;
            }

            WriteLine($"    {(branchWhenEqual ? "beq" : "bne.un")} {branchTo}");
            _currentStack -= 2;
        }

        /// <summary>
        /// <c>subject op value</c> for a <c>Case Is &gt; x</c> / range bound, branching to
        /// <paramref name="noMatch"/> when the relation is false.
        ///
        /// <para>Built from <c>clt</c>/<c>cgt</c>/<c>ceq</c> + a <c>brtrue</c>/<c>brfalse</c>
        /// rather than the <c>blt</c>/<c>bgt</c> branch forms, because the NEGATION of an
        /// ordered comparison is not one opcode: <c>!(a &gt; b)</c> is <c>ble</c> for signed
        /// integers but <c>ble.un</c> for floats (NaN must fail the test), and picking one
        /// spelling silently mis-handles the other type. Computing the POSITIVE relation with
        /// <c>clt</c>/<c>cgt</c> — signed for integers, ordered for floats — and then branching
        /// on the boolean is correct for both without inspecting the operand type. The one case
        /// it does not cover is an unsigned 32/64-bit subject, which needs the <c>.un</c>
        /// compare forms; that is selected explicitly.</para>
        /// </summary>
        private void EmitCaseRelationalTest(IRValue subject, IRValue value, string op, string noMatch)
        {
            // '=' and '<>' are equality, not ordering — route them through the equality path so a
            // string `Case Is = "x"` still gets String::Equals rather than a reference compare.
            if (op == "=")
            {
                EmitCaseEqualityTest(subject, value, noMatch);
                return;
            }

            if (op == "<>")
            {
                EmitCaseEqualityTest(subject, value, noMatch, branchWhenEqual: true);
                return;
            }

            if (IsStringOperand(subject) || IsStringOperand(value))
            {
                throw new ForeignFeatureException(
                    $"MSIL: 'Case Is {op}' on a String has no IL lowering. IL's ordering opcodes "
                    + "(clt/cgt) are defined for numeric and native-int operands only — applied "
                    + "to two object references they produce an unverifiable method that throws "
                    + "InvalidProgramException at run time, so this is refused here instead. "
                    + "String equality (Case \"x\", Case Is = \"x\", Case Is <> \"x\") is "
                    + "supported and routes through String::Equals.");
            }

            var unsigned = IsUnsignedOperand(subject);

            EmitLoadValue(subject);
            EmitLoadValue(value);

            switch (op)
            {
                case ">":
                    WriteLine(unsigned ? "    cgt.un" : "    cgt");
                    WriteLine($"    brfalse {noMatch}");
                    break;
                case "<":
                    WriteLine(unsigned ? "    clt.un" : "    clt");
                    WriteLine($"    brfalse {noMatch}");
                    break;
                case ">=":
                    WriteLine(unsigned ? "    clt.un" : "    clt");
                    WriteLine($"    brtrue {noMatch}");
                    break;
                case "<=":
                    WriteLine(unsigned ? "    cgt.un" : "    cgt");
                    WriteLine($"    brtrue {noMatch}");
                    break;
                default:
                    throw new ForeignFeatureException(
                        $"MSIL: unknown Select Case comparison operator '{op}'. The parser emits "
                        + "=, <>, >, <, >= and <=; anything else would fall through untested and "
                        + "silently match, so it is refused here instead.");
            }

            _currentStack -= 2;
        }

        /// <summary>
        /// <c>Case Nothing</c>. <c>brtrue</c> is right for both shapes VB gives this: a null
        /// reference and a zero integer both fall through to the match, anything else branches
        /// away. Floating-point subjects are refused — <c>brtrue</c> is not defined for an F
        /// operand and produces an unverifiable method.
        /// </summary>
        private void EmitNothingTest(IRValue subject, string noMatch)
        {
            var spec = subject?.Type != null ? IlTypeSpec(subject.Type) : "object";
            if (spec == "float32" || spec == "float64")
            {
                throw new ForeignFeatureException(
                    "MSIL: 'Case Nothing' on a floating-point value has no IL lowering — brtrue "
                    + "is undefined for an F operand and yields an unverifiable method, so it is "
                    + "refused here rather than emitted. Compare against 0 explicitly "
                    + "(Case 0 / Case Is = 0).");
            }

            EmitLoadValue(subject);
            WriteLine($"    brtrue {noMatch}");
            _currentStack--;
        }

        /// <summary>True when the value is typed as an IL <c>string</c>.</summary>
        private bool IsStringOperand(IRValue value) =>
            value?.Type != null && IlTypeSpec(value.Type) == "string";

        /// <summary>
        /// True for the IL primitives whose ordering needs the <c>.un</c> compare forms.
        /// <c>uint8</c>/<c>uint16</c>/<c>char</c> are deliberately absent: they are zero-extended
        /// to int32 on the evaluation stack, so the SIGNED compare already gives the right answer
        /// for them, and <c>.un</c> would be a no-op at best.
        /// </summary>
        private bool IsUnsignedOperand(IRValue value)
        {
            if (value?.Type == null) return false;
            var spec = IlTypeSpec(value.Type);
            return spec == "uint32" || spec == "uint64";
        }

        /// <summary>
        /// Emits an un-emitted expression tree — a suppressed <c>When</c> guard — leaving its
        /// value on the stack.
        ///
        /// <para><c>IRBuilder</c> builds a guard with <c>_suppressEmit</c> set so optimization
        /// passes cannot rewrite it, which means the guard's operand instructions never entered a
        /// block and were never given a local slot. <see cref="EmitLoadValue"/> alone would find
        /// no slot for them, write a <c>// WARNING</c> comment, and push NOTHING — unbalancing
        /// the stack and turning a wrong answer into an InvalidProgramException. So recurse over
        /// the guard's shape instead, and refuse loudly at any node this cannot rebuild rather
        /// than emitting a comment where a value belongs. (<c>CppCodeGenerator.RenderInline</c>
        /// is the same function for the same reason, over C++ expression text.)</para>
        /// </summary>
        private void EmitInlineValue(IRValue value)
        {
            switch (value)
            {
                case IRConstant constant:
                    EmitLoadConstant(constant);
                    return;

                case IRVariable variable:
                    EmitLoadLocal(variable.Name);
                    return;

                case IRBinaryOp binaryOp:
                {
                    var operandKind = BinaryOperandKind(binaryOp);
                    EmitInlineValue(binaryOp.Left);
                    EmitNumericCoercion(binaryOp.Left, operandKind);
                    EmitInlineValue(binaryOp.Right);
                    EmitNumericCoercion(binaryOp.Right, operandKind);
                    WriteLine($"    {_typeMapper.MapBinaryOperator(binaryOp.Operation)}");
                    _currentStack--;
                    return;
                }

                case IRCompare compare:
                {
                    var compareKind = WiderNumericKind(compare.Left, compare.Right);
                    EmitInlineValue(compare.Left);
                    EmitNumericCoercion(compare.Left, compareKind);
                    EmitInlineValue(compare.Right);
                    EmitNumericCoercion(compare.Right, compareKind);
                    EmitCompareOpcodes(compare.Comparison);
                    _currentStack--;
                    return;
                }

                case IRUnaryOp unaryOp:
                    EmitInlineValue(unaryOp.Operand);
                    WriteLine($"    {_typeMapper.MapUnaryOperator(unaryOp.Operation)}");
                    return;
            }

            // A leaf that IS already in a slot (a value computed before the Select Case and
            // merely referenced by the guard) is loadable the ordinary way.
            if (value != null
                && (_tempIndices.ContainsKey(value)
                    || (!string.IsNullOrEmpty(value.Name)
                        && (_declaredIdentifiers.Contains(value.Name) || _tempNameIndices.ContainsKey(value.Name)))))
            {
                EmitLoadValue(value);
                return;
            }

            throw new ForeignFeatureException(
                $"MSIL: the 'When' guard node '{value?.GetType().Name ?? "null"}' has no IL "
                + "lowering. A guard is built with instruction emission suppressed, so only "
                + "shapes this backend can rebuild in place are supported: constants, variables, "
                + "binary/compare/unary operators over them, and values already computed before "
                + "the Select Case. Anything else would push no value and corrupt the evaluation "
                + "stack, so it is refused here instead.");
        }

        /// <summary>
        /// The IL opcode(s) that turn two loaded operands into the boolean for
        /// <paramref name="comparison"/>. Shared by <see cref="Visit(IRCompare)"/> and the
        /// <c>When</c>-guard renderer so an ordinary comparison and a guard comparison cannot
        /// disagree. IL has only <c>ceq</c>/<c>clt</c>/<c>cgt</c>, so the other three are built
        /// by negating with <c>ldc.i4.0; ceq</c>.
        /// </summary>
        private void EmitCompareOpcodes(CompareKind comparison)
        {
            switch (comparison)
            {
                case CompareKind.Eq:
                    WriteLine("    ceq");
                    break;
                case CompareKind.Ne:
                    WriteLine("    ceq");
                    WriteLine("    ldc.i4.0");
                    WriteLine("    ceq"); // Not equal = !(a == b)
                    break;
                case CompareKind.Lt:
                    WriteLine("    clt");
                    break;
                case CompareKind.Le:
                    WriteLine("    cgt");
                    WriteLine("    ldc.i4.0");
                    WriteLine("    ceq"); // <= is !(a > b)
                    break;
                case CompareKind.Gt:
                    WriteLine("    cgt");
                    break;
                case CompareKind.Ge:
                    WriteLine("    clt");
                    WriteLine("    ldc.i4.0");
                    WriteLine("    ceq"); // >= is !(a < b)
                    break;
            }
        }

        public override void Visit(IRPhi phi)
        {
            // Phi nodes are handled during SSA deconstruction
            // For now, emit a comment
            WriteLine($"    // phi: {phi.Name}");
        }

        public override void Visit(IRAlloca alloca)
        {
            // In MSIL, locals are already allocated via .locals init
            // Nothing to emit here
        }

        public override void Visit(IRGetElementPtr gep)
        {
            // Array element access
            EmitLoadValue(gep.BasePointer);

            if (gep.Indices.Count > 0)
            {
                EmitLoadValue(gep.Indices[0]);
                var elemType = gep.BasePointer.Type?.ElementType;
                var ilType = elemType != null ? MapType(elemType) : "object";
                WriteLine($"    ldelema {ilType}");
                _currentStack--; // array + index -> address
            }

            // Store to temp
            if (_tempIndices.ContainsKey(gep))
            {
                var tempIdx = GetTempIndex(gep);
                EmitStloc(tempIdx);
            }
        }

        public override void Visit(IRCast cast)
        {
            EmitLoadValue(cast.Value);

            var targetType = cast.Type?.Name?.ToLower() ?? "";

            // ⛔ A FLOATING -> INTEGRAL narrowing ROUNDS HALF-TO-EVEN before the conv, because
            // `conv.i4` alone TRUNCATES. `Dim i As Integer = 7.5` answered 7 on all four backends
            // while `CInt(7.5)` answers 8 — one language, two answers depending on which syntax
            // reached the same narrowing. VB rounds both. Math::Round(float64) is ToEven by
            // default, and the conv that follows then has nothing left to truncate.
            if (IsFloatingType(cast.SourceType) && IsRoundingTarget(targetType))
            {
                WriteLine("    conv.r8");
                WriteLine("    call float64 [mscorlib]System.Math::Round(float64)");
            }

            switch (targetType)
            {
                case "integer":
                    WriteLine("    conv.i4");
                    break;
                case "long":
                    WriteLine("    conv.i8");
                    break;
                case "single":
                    WriteLine("    conv.r4");
                    break;
                case "double":
                    WriteLine("    conv.r8");
                    break;
                case "byte":
                    WriteLine("    conv.u1");
                    break;
                case "short":
                    WriteLine("    conv.i2");
                    break;
                case "char":
                    WriteLine("    conv.u2");
                    break;
                case "boolean":
                    // Convert to bool (0 or 1)
                    WriteLine("    ldc.i4.0");
                    WriteLine("    cgt.un");
                    break;
                default:
                    WriteLine($"    // WARNING: Unknown cast to {targetType}");
                    break;
            }

            // Store to temp
            if (!string.IsNullOrEmpty(cast.Name) && _declaredIdentifiers.Contains(cast.Name))
            {
                EmitStoreLocal(cast.Name);
            }
            else if (_tempIndices.ContainsKey(cast))
            {
                var tempIdx = GetTempIndex(cast);
                EmitStloc(tempIdx);
            }
        }

        public override void Visit(IRLabel label)
        {
            WriteLine($"  {SanitizeLabel(label.Name)}:");
        }

        public override void Visit(IRComment comment)
        {
            if (_options.GenerateComments)
            {
                WriteLine($"    // {comment.Text}");
            }
        }

        /// <summary>
        /// The array behind an array LITERAL (<c>Dim a() As Integer = {10, 20, 30}</c>).
        ///
        /// <para>⛔ This emitted <c>stloc {arrayAlloc.Name}</c> — the IR value's NAME where IL
        /// wants a slot index, so the file carried <c>stloc t0</c> and ilasm refused it with
        /// "Undeclared identifier t0". <c>newarr</c> also took <c>MapType</c>, the IL keyword
        /// form; an operand position needs the type TOKEN.</para>
        /// </summary>
        public override void Visit(IRArrayAlloc arrayAlloc)
        {
            EmitLdcI4(arrayAlloc.Size);
            _currentStack++;
            WriteLine($"    newarr {IlTypeToken(arrayAlloc.ElementType)}");
            EmitStloc(GetTempIndex(arrayAlloc));
        }

        /// <inheritdoc cref="Visit(IRArrayAlloc)"/>
        public override void Visit(IRArrayStore arrayStore)
        {
            var elementType = arrayStore.Array.Type?.ElementType ?? new TypeInfo("Object", TypeKind.Class);

            EmitLoadValue(arrayStore.Array);
            EmitLoadValue(arrayStore.Index);
            EmitLoadValue(arrayStore.Value);
            WriteLine($"    stelem {IlTypeToken(elementType)}");
            _currentStack -= 3;
        }

        public override void Visit(IRAwait awaitInst)
        {
            // MSIL async/await requires complex state machine generation
            WriteLine($"    // WARNING: await not fully implemented in MSIL backend - expression will be evaluated synchronously");
        }

        public override void Visit(IRYield yieldInst)
        {
            // MSIL yield requires iterator state machine generation
            if (yieldInst.IsBreak)
                WriteLine("    // WARNING: yield break not fully implemented in MSIL backend - iterator will not function correctly");
            else
                WriteLine($"    // WARNING: yield return not fully implemented in MSIL backend - iterator will not function correctly");
        }

        /// <summary>
        /// ⛔ <b><c>Throw</c> used to emit NOTHING on this backend.</b>
        /// <see cref="ICodeGenerator"/> declares <c>Visit(IRThrow)</c> as an empty virtual so a
        /// backend can ignore nodes it does not handle, and MSIL never overrode it. The exception
        /// object was therefore constructed, stored, and dropped, and execution continued straight
        /// past the <c>Throw</c> into the statements below it. Every <c>Try</c>/<c>Catch</c> on
        /// MSIL was consequently untestable: nothing could ever reach a handler.
        /// </summary>
        public override void Visit(IRThrow throwInst)
        {
            if (throwInst.Exception == null)
            {
                // A bare `Throw` re-raises the exception the handler is running for, preserving
                // its original stack trace. `rethrow` is only defined inside a catch handler.
                if (!_regionIsCatch)
                {
                    throw new ForeignFeatureException(
                        "MSIL: a bare 'Throw' (rethrow) outside a Catch block has no IL lowering — "
                        + "the rethrow opcode is only valid inside a catch handler, and emitting it "
                        + "elsewhere produces a method the CLR rejects. Throw a specific exception "
                        + "instead.");
                }

                WriteLine("    rethrow");
                return;
            }

            EmitLoadValue(throwInst.Exception);
            WriteLine("    throw");
            _currentStack--;
        }

        public override void Visit(IRNewObject newObj)
        {
            // Generate newobj instruction. A collection construction needs its BCL generic
            // token — `newobj instance void List::.ctor()` names nothing. Everything else goes
            // through IlTypeToken rather than SanitizeName so a BCL type keeps its assembly:
            // `Throw New Exception(m)` emitted `newobj instance void Exception::.ctor(string)`,
            // and ilasm refused the file with "Reference to undefined class 'Exception'". A user
            // class still resolves to its bare sanitized name, as before.
            var className = TryCollectionToken(newObj.Type, out var collectionToken)
                ? "class " + collectionToken
                : IlTypeToken(newObj.ClassName);

            // Load arguments first
            foreach (var arg in newObj.Arguments)
            {
                EmitLoadValue(arg);
            }

            // Build constructor signature with parameter types
            var paramTypes = DeclaredParamList(
                DeclaredCtorParams(newObj.ClassName, newObj.Arguments.Count), newObj.Arguments);

            // Emit newobj with proper constructor signature
            WriteLine($"    newobj instance void {className}::.ctor({paramTypes})");
            _currentStack -= newObj.Arguments.Count; // Arguments consumed
            _currentStack++; // Object reference pushed

            // Store result if needed
            if (!string.IsNullOrEmpty(newObj.Name))
            {
                if (_declaredIdentifiers.Contains(newObj.Name))
                {
                    EmitStoreLocal(newObj.Name);
                }
                else
                {
                    var tempIdx = GetTempIndex(newObj);
                    EmitStloc(tempIdx);
                }
            }
        }

        public override void Visit(IRInstanceMethodCall methodCall)
        {
            var hasReturn = methodCall.Type != null && !methodCall.Type.Name.Equals("Void", StringComparison.OrdinalIgnoreCase);

            // ⛔ BasicLang lets a `Shared` member be reached through an INSTANCE — `b.Tag()` where
            // Tag is Shared. IL does not: the method has no `this` parameter, so the `callvirt
            // instance` below named `Box::Tag()` with a receiver and the CLR could not find it
            // (MissingMethodException). The receiver expression is still evaluated and then
            // discarded, because it may have side effects — `MakeBox().Tag()` must still run
            // MakeBox.
            if (TryFindClass(methodCall.Object?.Type?.Name, out var staticOwner)
                && TryFindStaticMethod(staticOwner, methodCall.MethodName, out var staticDeclaring, out var staticMethod))
            {
                EmitLoadValue(methodCall.Object);
                WriteLine("    pop");
                _currentStack--;

                EmitUserStaticCall(
                    methodCall, methodCall.Arguments,
                    SanitizeName(staticDeclaring.Name), staticMethod, hasReturn);
                return;
            }

            // Load 'this' reference (the object on which the method is called)
            EmitLoadValue(methodCall.Object);

            // Load arguments — an ADDRESS for each one the DECLARATION takes ByRef. An interface
            // receiver has no IRVariable parameter list to read IsByRef from, so a ByRef through
            // an interface stays by-value here and is caught by the signature check below.
            EmitCallArguments(
                methodCall.Arguments,
                DeclaredMethodParams(
                    TryFindClass(methodCall.Object?.Type?.Name, out var declaringForArgs) ? declaringForArgs : null,
                    methodCall.MethodName),
                methodCall.MethodName);

            // Build method signature
            string returnType, paramTypes, className, methodName;
            string boxInterfaceReturn = null;
            if (TryCollectionMember(
                    methodCall.Object?.Type, methodCall.MethodName, out var collToken, out var collSig))
            {
                // Generic definition signature, per the table — not the substituted types.
                className = "class " + collToken;
                methodName = collSig.Il;
                returnType = collSig.Ret;
                paramTypes = collSig.Params;
            }
            else
            {
                // The DECLARATION decides the signature — an interface first, because an
                // interface receiver is not a class and every class-side lookup misses it.
                var ifaceMethod = DeclaredInterfaceMethod(methodCall.Object?.Type?.Name, methodCall.MethodName);
                if (ifaceMethod != null)
                {
                    returnType = IlTypeSpec(ifaceMethod.ReturnType);
                    paramTypes = string.Join(", ",
                        (ifaceMethod.Parameters ?? new List<IRParameter>()).Select(pp => IlTypeSpec(pp.Type)));

                    // ⛔ THE IR AND THE IL NOW DISAGREE, AND THE STACK FOLLOWS THE IL. The front end
                    // types an interface-method call Object, so its destination temp is an object
                    // slot; taking the return type from the interface declaration (as we must) means
                    // a Function returning Integer really does leave an int32 on the stack. Storing
                    // that into an object local UNBOXED hands the runtime a raw integer as a
                    // reference — measured: NullReferenceException inside Console.WriteLine, with a
                    // stack trace pointing at the print rather than at the call. Box to put the
                    // stack back in step with what the IR believes it is holding.
                    if (IsIlValueType(returnType) && !IsIlValueType(MapType(methodCall.Type)))
                    {
                        boxInterfaceReturn = IlTypeToken(ifaceMethod.ReturnType);
                    }

                    // ⛔ THE SAME DISAGREEMENT IN THE OTHER DIRECTION — a Sub. The front end types
                    // an interface call Object whatever the member returns, so hasReturn came out
                    // TRUE and a store was emitted; but the declaration says void, so the call
                    // pushes NOTHING and that store underflows the stack. Measured on
                    // `Interface ISpeaker : Sub Speak()`: `callvirt instance void
                    // 'ISpeaker'::'Speak'()` followed by `stloc.2` — InvalidProgramException, and
                    // the CLR names no line. The declaration decides this too. Leaving the store
                    // out leaves the IR's destination local at the null `.locals init` already
                    // gave it, which is the same value the store would have written.
                    if (returnType == "void") hasReturn = false;
                }
                else
                {
                    returnType = IlTypeSpec(methodCall.Type);
                    paramTypes = DeclaredParamList(
                        DeclaredMethodParams(TryFindClass(methodCall.Object?.Type?.Name, out var declaringForCall)
                            ? declaringForCall : null, methodCall.MethodName), methodCall.Arguments);
                }
                className = IlReceiverToken(methodCall.Object?.Type);
                methodName = SanitizeName(methodCall.MethodName);
            }
            // Use callvirt for virtual dispatch (polymorphic behavior)
            // For non-virtual calls, the backend should use 'call instance' instead, but callvirt is safer as default
            var callInstruction = methodCall.IsVirtual || !methodCall.IsVirtual ? "callvirt" : "call";
            WriteLine($"    {callInstruction} instance {returnType} {className}::{methodName}({paramTypes})");
            if (boxInterfaceReturn != null) WriteLine($"    box {boxInterfaceReturn}");

            // Update stack: pop 'this' + args, push return value if any
            _currentStack -= (1 + methodCall.Arguments.Count);
            if (hasReturn) _currentStack++;

            // Store result if needed
            if (hasReturn && !string.IsNullOrEmpty(methodCall.Name))
            {
                if (_declaredIdentifiers.Contains(methodCall.Name))
                {
                    EmitStoreLocal(methodCall.Name);
                }
                else
                {
                    var tempIdx = GetTempIndex(methodCall);
                    EmitStloc(tempIdx);
                }
            }
            else if (hasReturn)
            {
                // Discard unused return value
                WriteLine("    pop");
                _currentStack--;
            }
        }

        public override void Visit(IRBaseMethodCall baseCall)
        {
            var hasReturn = baseCall.Type != null && !baseCall.Type.Name.Equals("Void", StringComparison.OrdinalIgnoreCase);

            // Load 'this' (ldarg.0 for instance methods)
            WriteLine("    ldarg.0");
            _currentStack++;

            // Load arguments
            foreach (var arg in baseCall.Arguments)
            {
                EmitLoadValue(arg);
            }

            // Build method signature
            var returnType = IlTypeSpec(baseCall.Type);
            var paramTypes = DeclaredParamList(null, baseCall.Arguments);
            var methodName = SanitizeName(baseCall.MethodName);

            // For base calls, we need to know the base class name from the current class context
            // Use 'call instance' instead of 'callvirt' to call base class method non-virtually
            var baseClassName = _currentClass != null && !string.IsNullOrEmpty(_currentClass.BaseClass)
                ? SanitizeName(_currentClass.BaseClass)
                : "[mscorlib]System.Object";
            WriteLine($"    call instance {returnType} {baseClassName}::{methodName}({paramTypes})");

            // Update stack: pop 'this' + args, push return value if any
            _currentStack -= (1 + baseCall.Arguments.Count);
            if (hasReturn) _currentStack++;

            // Store result if needed
            if (hasReturn && !string.IsNullOrEmpty(baseCall.Name))
            {
                if (_declaredIdentifiers.Contains(baseCall.Name))
                {
                    EmitStoreLocal(baseCall.Name);
                }
                else
                {
                    var tempIdx = GetTempIndex(baseCall);
                    EmitStloc(tempIdx);
                }
            }
            else if (hasReturn)
            {
                // Discard unused return value
                WriteLine("    pop");
                _currentStack--;
            }
        }

        public override void Visit(IRFieldAccess fieldAccess)
        {
            // ⛔ A `Shared` field reached as `Counter.Total`. The receiver is a TYPE NAME, not a
            // value: loading it emitted `// WARNING: Unknown local 'Counter'` and pushed nothing,
            // and the `ldfld` that followed read a field off an empty stack — InvalidProgramException.
            // `ldsfld` takes no receiver at all. Reached through an INSTANCE (`b.Total`, which
            // BasicLang allows) the receiver IS a value, so it is evaluated and discarded.
            if (TryResolveStaticField(fieldAccess.Object, fieldAccess.FieldName, out var readOwner,
                    out var readField, out var readViaInstance))
            {
                if (readViaInstance)
                {
                    EmitLoadValue(fieldAccess.Object);
                    WriteLine("    pop");
                    _currentStack--;
                }

                WriteLine($"    ldsfld {IlTypeSpec(readField.Type)} {SanitizeName(readOwner.Name)}::{SanitizeName(readField.Name)}");
                _currentStack++;
                EmitFieldAccessResult(fieldAccess);
                return;
            }

            // ⛔ A user class's PROPERTY reached as `c.Alpha`. Checked BEFORE the receiver is
            // pushed, because a Shared property reached by TYPE NAME (`Box.Total`) has no
            // receiver to push at all — the same split TryResolveStaticField makes.
            if (TryResolveProperty(fieldAccess.Object, fieldAccess.FieldName,
                    out var readPropOwner, out var readProp, out var readPropViaInstance))
            {
                if (readProp.IsStatic)
                {
                    if (readPropViaInstance)
                    {
                        EmitLoadValue(fieldAccess.Object);
                        WriteLine("    pop");
                        _currentStack--;
                    }
                }
                else
                {
                    EmitLoadValue(fieldAccess.Object);
                }

                EmitPropertyGet(readPropOwner, readProp);
                EmitFieldAccessResult(fieldAccess);
                return;
            }

            // Load object reference
            EmitLoadValue(fieldAccess.Object);

            // ⛔ A collection's Count reaches here as a FIELD access, and it is not a field —
            // `ldfld int32 List::Count` names a bare class and a member that does not exist.
            // It is a property, so it has to become its accessor call.
            if (TryCollectionMember(
                    fieldAccess.Object?.Type, fieldAccess.FieldName, out var collToken, out var collSig))
            {
                WriteLine($"    callvirt instance {collSig.Ret} class {collToken}::{collSig.Il}({collSig.Params})");
                _currentStack--;
                _currentStack++;
                EmitFieldAccessResult(fieldAccess);
                return;
            }

            // ⛔ An ARRAY's Length is neither a field nor a property — IL has a dedicated opcode
            // for it. `a.Length` emitted `ldfld int32 Integer[]::Length`, which names a class that
            // does not exist and fails to assemble. `For i = 0 To a.Length - 1` is the ordinary
            // way to walk an array, so this is needed for the allocation fix to be usable.
            if (fieldAccess.Object?.Type?.Kind == TypeKind.Array
                && string.Equals(fieldAccess.FieldName, "Length", StringComparison.OrdinalIgnoreCase))
            {
                WriteLine("    ldlen");
                WriteLine("    conv.i4");   // ldlen yields a native uint; BasicLang's Length is Integer
                EmitFieldAccessResult(fieldAccess);
                return;
            }

            // ⛔ THE SAME SHAPE ONCE MORE, on the type it matters most for. `s.Length` emitted
            // `ldfld int32 [mscorlib]System.String::'Length'`, which ASSEMBLES — ilasm does not
            // resolve member references — and dies at run time with
            // `MissingFieldException: Field not found: 'System.String.Length'`. Length is a
            // PROPERTY, so it has to become its accessor call.
            //
            // ⚠ This is the PROPERTY half only, and that is what made it hard to see: the METHOD
            // half beside it already worked. `s.ToUpper()` and `s.Substring(1, 3)` both run today,
            // because Visit(IRInstanceMethodCall) renders the receiver through IlReceiverToken and
            // emits a real `callvirt`. Only a member reaching Visit(IRFieldAccess) was broken, and
            // String's only property is the one everybody uses.
            //
            // ⛔ An unrecorded String member is REFUSED, not passed to the ldfld below, and unlike
            // the collection and exception tables that is not merely a convention here:
            // System.String has NO public instance fields at all, so a field load on a string
            // receiver cannot be right whatever it names. Falling through would re-create exactly
            // the run-time failure this arm exists to remove.
            if (TryStringMember(fieldAccess.Object?.Type, fieldAccess.FieldName, out var strAccessor))
            {
                WriteLine($"    callvirt instance {strAccessor.Ret} [mscorlib]System.String::{strAccessor.Il}()");
                _currentStack--;
                _currentStack++;
                EmitFieldAccessResult(fieldAccess);
                return;
            }

            // ⛔ Same shape as the collection arm above: an EXCEPTION's members reach here as field
            // accesses and none of them are fields. `ex.Message` emitted
            // `ldfld string [mscorlib]System.Exception::Message`, which assembles — ilasm does not
            // resolve member references — and dies at run time with MissingFieldException. Reading
            // the exception is the main thing a Catch block does, so the accessor call is required
            // for Try/Catch to be usable at all.
            if (TryExceptionMember(fieldAccess.Object?.Type, fieldAccess.FieldName, out var exToken, out var exAccessor))
            {
                WriteLine($"    callvirt instance string {exToken}::{exAccessor}()");
                _currentStack--;
                _currentStack++;
                EmitFieldAccessResult(fieldAccess);
                return;
            }

            // Load field value from object
            var fieldType = IlTypeSpec(fieldAccess.Type);
            var className = DeclaringFieldToken(fieldAccess.Object?.Type, fieldAccess.FieldName);
            var fieldName = SanitizeName(fieldAccess.FieldName);

            WriteLine($"    ldfld {fieldType} {className}::{fieldName}");
            _currentStack--; // Pop object reference
            _currentStack++; // Push field value

            EmitFieldAccessResult(fieldAccess);
        }

        /// <summary>
        /// Parks the value a field access (or the property call that stands in for one) left on
        /// the stack. Shared so the collection-property arm cannot drift from the field arm —
        /// both push exactly one value and both have to store it the same way.
        /// </summary>
        private void EmitFieldAccessResult(IRFieldAccess fieldAccess)
        {
            if (string.IsNullOrEmpty(fieldAccess.Name)) return;

            if (_declaredIdentifiers.Contains(fieldAccess.Name))
                EmitStoreLocal(fieldAccess.Name);
            else
                EmitStloc(GetTempIndex(fieldAccess));
        }

        public override void Visit(IRFieldStore fieldStore)
        {
            // The write half of the `Shared` field case — see Visit(IRFieldAccess). `stsfld` takes
            // the value alone, with no object reference under it, so unlike the instance `stfld`
            // path this needs no scratch slot to get the operands in the right order.
            if (TryResolveStaticField(fieldStore.Object, fieldStore.FieldName, out var storeOwner,
                    out var storeField, out var storeViaInstance))
            {
                if (storeViaInstance)
                {
                    EmitLoadValue(fieldStore.Object);
                    WriteLine("    pop");
                    _currentStack--;
                }

                EmitLoadValue(fieldStore.Value);
                WriteLine($"    stsfld {IlTypeSpec(storeField.Type)} {SanitizeName(storeOwner.Name)}::{SanitizeName(storeField.Name)}");
                _currentStack--;
                return;
            }

            // The write half of the property case — see Visit(IRFieldAccess).
            if (TryResolveProperty(fieldStore.Object, fieldStore.FieldName,
                    out var storePropOwner, out var storeProp, out var storePropViaInstance))
            {
                if (storeProp.IsStatic)
                {
                    if (storePropViaInstance)
                    {
                        EmitLoadValue(fieldStore.Object);
                        WriteLine("    pop");
                        _currentStack--;
                    }
                }
                else
                {
                    EmitLoadValue(fieldStore.Object);
                }

                EmitLoadValue(fieldStore.Value);
                EmitPropertySet(storePropOwner, storeProp);
                return;
            }

            // Load object reference
            EmitLoadValue(fieldStore.Object);

            // Load value to store
            EmitLoadValue(fieldStore.Value);

            // Store value to field
            var fieldType = IlTypeSpec(
                DeclaredFieldType(fieldStore.Object?.Type, fieldStore.FieldName) ?? fieldStore.Value?.Type);
            var className = fieldStore.Object?.Type?.Name != null
                ? DeclaringFieldToken(fieldStore.Object.Type, fieldStore.FieldName)
                : "object";
            var fieldName = SanitizeName(fieldStore.FieldName);

            WriteLine($"    stfld {fieldType} {className}::{fieldName}");

            // Update stack: pop object + value
            _currentStack -= 2;
        }

        public override void Visit(IRTupleElement tupleElement)
        {
            // Load tuple value onto stack
            EmitLoadValue(tupleElement.Tuple);

            // Call the Item property getter (Item1, Item2, etc.)
            var tupleType = MapType(tupleElement.Tuple?.Type) ?? "object";
            var elemType = MapType(tupleElement.Type);
            var itemIndex = tupleElement.Index + 1; // 1-based

            WriteLine($"    ldfld {elemType} {tupleType}::Item{itemIndex}");

            // Store to local variable
            var varName = SanitizeName(tupleElement.Name);
            if (!_localIndices.ContainsKey(varName))
            {
                var localIndex = _localCounter++;
                _localIndices[varName] = localIndex;
            }
            WriteLine($"    stloc {_localIndices[varName]}");
        }

        /// <summary>
        /// Emits a <c>Try</c> as real IL exception-handling regions.
        ///
        /// <para>⛔ <b>The previous emitter looked right and protected nothing.</b> It inlined the
        /// try block's non-branch instructions into <c>.try { }</c> and hard-coded
        /// <c>leave EndTry</c>. But a Try body is a graph of BLOCKS, not one block's instruction
        /// list, so only the first block's straight-line instructions ever landed inside the
        /// region — and those same blocks were then emitted AGAIN by
        /// <see cref="GenerateBasicBlock"/> as ordinary labelled blocks, because nothing marked
        /// them handled. The real work therefore ran OUTSIDE the protected region, after a
        /// truncated copy of it had already run inside. A Try around an If printed the right
        /// answer while protecting nothing, which is why it looked like it worked.</para>
        ///
        /// <para>Three further faults are fixed here rather than separately, because they are the
        /// same mistake at different points: <c>FinallyBlock</c> was ignored entirely (leaving
        /// <c>.try { }</c> with NO handler, which ilasm rejects outright); the <c>EndTry</c> label
        /// was a fixed string, so two Try statements in one method emitted it twice; and a catch
        /// type was spelled <c>[mscorlib]System.</c> + the clause's name, which turns a
        /// user-defined exception class into a reference to a BCL type that does not exist.</para>
        ///
        /// <para><b>Shape.</b> IL does not allow <c>catch</c> and <c>finally</c> on one region, so
        /// a Try with both nests: the inner region carries the catches, the outer carries the
        /// finally. That is also what gives the right semantics — a <c>leave</c> out of the inner
        /// region runs the outer finally on its way past.</para>
        /// </summary>
        public override void Visit(IRTryCatch tryCatch)
        {
            if (tryCatch.TryBlock == null || tryCatch.EndBlock == null)
            {
                throw new ForeignFeatureException(
                    "MSIL: a Try with no body block or no continuation block cannot be lowered. "
                    + "IRBuilder always creates both, so this is an emitter invariant failure.");
            }

            var endLabel = SanitizeLabel(tryCatch.EndBlock.Name);

            // A region stops at the blocks that belong to the OTHER arms of the same Try. Without
            // these bounds the walk from the try body would wander into the catch and finally
            // blocks and emit them inside the protected region.
            var boundaries = new HashSet<BasicBlock> { tryCatch.EndBlock };
            foreach (var clause in tryCatch.CatchClauses)
            {
                if (clause.Block != null) boundaries.Add(clause.Block);
            }
            if (tryCatch.FinallyBlock != null) boundaries.Add(tryCatch.FinallyBlock);

            var hasCatch = tryCatch.CatchClauses.Count > 0;
            var hasFinally = tryCatch.FinallyBlock != null;

            if (!hasCatch && !hasFinally)
            {
                // A Try with neither arm protects nothing; emit the body as ordinary blocks rather
                // than an empty region, which would not assemble.
                EmitRegionBody(tryCatch.TryBlock, boundaries, endLabel, inRegion: false, isFinally: false);
                return;
            }

            if (hasFinally && hasCatch)
            {
                WriteLine("    .try");
                WriteLine("    {");
                EmitTryAndCatches(tryCatch, boundaries, endLabel);
                // Unreachable in practice — every arm above ends in `leave` — but a protected
                // block may not fall out of its own end, so give it an explicit exit.
                WriteLine($"    leave {endLabel}");
                WriteLine("    }");
                EmitFinallyHandler(tryCatch, boundaries, endLabel);
                return;
            }

            if (hasFinally)
            {
                WriteLine("    .try");
                WriteLine("    {");
                EmitRegionBody(tryCatch.TryBlock, boundaries, endLabel, inRegion: true, isFinally: false);
                WriteLine("    }");
                EmitFinallyHandler(tryCatch, boundaries, endLabel);
                return;
            }

            EmitTryAndCatches(tryCatch, boundaries, endLabel);
        }

        /// <summary>The <c>.try { } catch { }</c> pair, used alone or nested inside a finally's try.</summary>
        private void EmitTryAndCatches(IRTryCatch tryCatch, HashSet<BasicBlock> boundaries, string endLabel)
        {
            WriteLine("    .try");
            WriteLine("    {");
            EmitRegionBody(tryCatch.TryBlock, boundaries, endLabel, inRegion: true, isFinally: false);
            WriteLine("    }");

            foreach (var clause in tryCatch.CatchClauses)
            {
                WriteLine($"    catch {CatchTypeToken(clause)}");
                WriteLine("    {");
                EmitCatchBinding(clause);
                EmitRegionBody(clause.Block, boundaries, endLabel, inRegion: true, isFinally: false, isCatch: true);
                WriteLine("    }");
            }
        }

        private void EmitFinallyHandler(IRTryCatch tryCatch, HashSet<BasicBlock> boundaries, string endLabel)
        {
            WriteLine("    finally");
            WriteLine("    {");
            EmitRegionBody(tryCatch.FinallyBlock, boundaries, endLabel, inRegion: true, isFinally: true);
            WriteLine("    }");
        }

        /// <summary>
        /// A catch handler is entered with the exception already on the stack. Bind it to the
        /// clause's variable, or drop it — leaving it would unbalance every instruction after.
        /// </summary>
        private void EmitCatchBinding(IRCatchClause clause)
        {
            if (string.IsNullOrEmpty(clause.VariableName))
            {
                WriteLine("    pop");
                return;
            }

            // ⚠ The RAW name, matching the key AllocateExceptionHandlingLocals reserves under.
            // `_localIndices` is a lookup table for the IR's names; the quoted form is only ever
            // printed.
            var name = clause.VariableName;
            if (!_localIndices.TryGetValue(name, out var index))
            {
                throw new ForeignFeatureException(
                    $"MSIL: the catch variable '{clause.VariableName}' has no local slot. "
                    + "AllocateExceptionHandlingLocals reserves one for every catch clause before "
                    + ".locals init is written; emitting a store to an unreserved slot is what "
                    + "produced InvalidProgramException here before.");
            }

            _currentStack++;   // the runtime pushed the exception
            EmitStloc(index);
        }

        /// <summary>
        /// Emits every block of one region, in walk order, marking them handled so
        /// <see cref="GenerateBasicBlock"/> does not write them a second time.
        ///
        /// <para>The region's own branches are rewritten by the control-flow visitors, which read
        /// the state set here — see <see cref="EmitRegionAwareBranch"/>. State is saved and
        /// restored rather than assigned, so a Try nested inside a Try classifies its edges
        /// against the INNER region while it is being emitted and the outer one afterwards.</para>
        /// </summary>
        private void EmitRegionBody(
            BasicBlock entry, HashSet<BasicBlock> boundaries, string leaveTarget, bool inRegion,
            bool isFinally, bool isCatch = false)
        {
            var blocks = CollectRegionBlocks(entry, boundaries);

            var previousBlocks = _regionBlocks;
            var previousIsFinally = _regionIsFinally;
            var previousIsCatch = _regionIsCatch;
            var previousLeaveTarget = _regionLeaveTarget;

            _regionBlocks = inRegion ? new HashSet<BasicBlock>(blocks) : null;
            _regionIsFinally = isFinally;
            _regionIsCatch = isCatch;
            _regionLeaveTarget = leaveTarget;

            foreach (var block in blocks)
            {
                // A For Each inside this region writes its own body blocks when its instruction
                // is visited. They are collected here as ordinary CFG successors, so without this
                // they would be written a second time — "Duplicate label", the same failure a
                // nested Try's arms produce. Marked as each block is taken, and the walk order is
                // depth-first from the entry, so the loop's own block is always seen first.
                if (!_consumedBlocks.Add(block)) continue;
                _visitedBlocks?.Add(block);
                WriteLine($"  {SanitizeLabel(block.Name)}:");

                foreach (var instruction in block.Instructions)
                {
                    instruction.Accept(this);
                }

                // An IR block with no terminator falls through to the next block in source order.
                // Inside a region that is not expressible, so make the exit explicit.
                if (!block.IsTerminated())
                {
                    if (isFinally) WriteLine("    endfinally");
                    else if (inRegion) WriteLine($"    leave {leaveTarget}");
                    else WriteLine($"    br {leaveTarget}");
                }
            }

            _regionBlocks = previousBlocks;
            _regionIsFinally = previousIsFinally;
            _regionIsCatch = previousIsCatch;
            _regionLeaveTarget = previousLeaveTarget;
        }

        /// <summary>
        /// The blocks reachable from <paramref name="entry"/> without passing through a boundary —
        /// that is, one arm of a Try. Depth-first from the entry so the emitted order matches the
        /// order <see cref="GenerateBasicBlock"/> would have used.
        /// </summary>
        private List<BasicBlock> CollectRegionBlocks(BasicBlock entry, HashSet<BasicBlock> boundaries)
        {
            var ordered = new List<BasicBlock>();
            var seen = new HashSet<BasicBlock>();

            // The ENTRY is exempt from the boundary test. Every arm's own entry block is in the
            // boundary set — that is what stops the try body walking into the catch — so testing
            // it would make each handler collect zero blocks and emit an empty region, leaving its
            // real body to be written outside the handler by GenerateBasicBlock.
            void Walk(BasicBlock block, bool isEntry)
            {
                if (block == null) return;
                if (!isEntry && boundaries.Contains(block)) return;
                if (!seen.Add(block)) return;
                ordered.Add(block);

                // A NESTED Try owns its own arms: its Visit(IRTryCatch) emits them inside its own
                // region when this block's instructions are walked. They are reachable from here
                // as ordinary CFG successors, so without this they would also be collected into
                // the enclosing region and written a second time — ilasm rejects the file with
                // "Duplicate label". Its continuation block is NOT skipped: that is where the
                // enclosing region resumes.
                var nestedArms = new HashSet<BasicBlock>();
                foreach (var nested in block.Instructions.OfType<IRTryCatch>())
                {
                    if (nested.TryBlock != null) nestedArms.Add(nested.TryBlock);
                    foreach (var clause in nested.CatchClauses)
                    {
                        if (clause.Block != null) nestedArms.Add(clause.Block);
                    }
                    if (nested.FinallyBlock != null) nestedArms.Add(nested.FinallyBlock);
                }

                foreach (var successor in block.Successors)
                {
                    if (nestedArms.Contains(successor)) continue;
                    Walk(successor, isEntry: false);
                }
            }

            Walk(entry, isEntry: true);
            return ordered;
        }

        /// <summary>
        /// The IL type token naming what a <c>Catch</c> clause catches.
        ///
        /// <para>⛔ Not <c>"[mscorlib]System." + name</c>, which is what this used to be. That is
        /// right only by coincidence for the BCL names and turns <c>Catch e As MyError</c> into a
        /// reference to <c>[mscorlib]System.MyError</c> — a type that does not exist, so the
        /// assembly fails to build or fails at load. The recognized .NET names come from
        /// <see cref="CppExceptionTypes"/>, the single place this repo records them, so MSIL and
        /// C++ cannot disagree about which names are .NET exceptions.</para>
        /// </summary>
        private string CatchTypeToken(IRCatchClause clause)
        {
            var name = clause.ExceptionType?.Name;
            if (string.IsNullOrEmpty(name)) return "[mscorlib]System.Exception";

            // The analyzer resolved a .NET exception outside the known set (e.g. System.IO.*).
            if (!string.IsNullOrEmpty(clause.NetExceptionFullName))
                return "[mscorlib]" + clause.NetExceptionFullName;

            if (CppExceptionTypes.TryGetNetFullName(name, out var fullName))
                return "[mscorlib]" + fullName;

            // A user-defined exception class compiled into this same assembly.
            if (_module?.Classes != null && _module.Classes.ContainsKey(name))
                return SanitizeName(name);

            throw new ForeignFeatureException(
                $"MSIL: the Catch type '{name}' is neither one of the recognized .NET exception "
                + "names nor a class defined in this compilation, so there is no type token to "
                + "emit for it. Emitting a guessed '[mscorlib]System." + name + "' produces a "
                + "reference to a type that does not exist, which fails at assembly or load time "
                + "rather than saying so here.");
        }

        public override void Visit(IRInlineCode inlineCode)
        {
            if (inlineCode.Language.ToLower() == "msil")
            {
                // Emit the MSIL code directly
                WriteLine("        // Inline MSIL code");
                foreach (var line in inlineCode.Code.Split('\n'))
                {
                    WriteLine($"        {line.TrimEnd()}");
                }
            }
            else
            {
                // For non-MSIL inline code, emit a comment indicating it's not supported
                WriteLine($"        // WARNING: Inline {inlineCode.Language} code not supported in MSIL backend");
                WriteLine($"        // Original code ({inlineCode.Code.Length} chars) was skipped");
            }
        }

        /// <summary>
        /// A <c>For Each</c> in IL: an enumerator parked in a slot, <c>MoveNext</c> at the head,
        /// <c>get_Current</c> into the loop variable's slot, and the body emitted ONCE with its
        /// end-of-iteration branch pointed back at the head.
        ///
        /// <para>⛔ <b>Measured before, on <c>For Each n In l</c> over a
        /// <c>List(Of Integer)</c></b> — InvalidProgramException, from THREE defects at once:
        /// the enumerator's <c>stloc.s 0</c> overwrote the list's own local; the loop variable had
        /// no slot anywhere, so <c>total = total + n</c> emitted <c>ldloc.1</c>,
        /// <c>// WARNING: Unknown local 'n'</c>, <c>add</c> — an <c>add</c> with ONE operand; and
        /// the whole body was then emitted a SECOND time as the labelled block
        /// <c>foreach0body:</c>, because <c>ControlFlowGraph.Build</c> wires the body in as a CFG
        /// successor and <see cref="GenerateBasicBlock"/> walks successors.</para>
        ///
        /// <para><b>The non-generic enumerator, deliberately.</b>
        /// <c>IEnumerable::GetEnumerator</c> / <c>IEnumerator::MoveNext</c> /
        /// <c>IEnumerator::get_Current</c> is the ONE spelling that serves every receiver this
        /// backend can name — <c>List`1</c>, <c>Dictionary`2</c>, a vector array and
        /// <c>String</c> all implement it — where <c>IEnumerable`1&lt;T&gt;</c> would need the
        /// element type to be recoverable from the receiver, which it is not for an array or a
        /// string. The cost is one <c>unbox.any</c> per iteration, which is also what makes the
        /// element type explicit in the IL instead of inferred.</para>
        ///
        /// <para>⚠ <b>The enumerator is NOT disposed.</b> A C# <c>foreach</c> wraps the loop in
        /// <c>try/finally</c> and calls <c>IDisposable::Dispose</c>; this does not. No collection
        /// this backend can name has a disposal-sensitive enumerator, and adding a protected
        /// region around every loop would put every <c>Return</c> inside one — the lowering
        /// <c>Visit(IRReturn)</c> documents as the expensive path. Revisit when an iterator
        /// method (<c>Yield</c>) becomes reachable here, where it would matter.</para>
        ///
        /// <para>⚠ <b><c>Exit For</c> and <c>Continue For</c> are indistinguishable in the IR</b>
        /// and both arrive as a branch to the continuation block, so both lower to "next
        /// iteration". That is an IRBuilder fact, not an MSIL one — <c>LoopContext(endBlock,
        /// endBlock)</c> gives break and continue the same target — and the other backends divide
        /// the same way: C++ and JavaScript also run <c>Exit For</c> as a continue, and C# runs it
        /// as nothing at all. Fixing it means giving the node a break target, which every backend
        /// reads.</para>
        /// </summary>
        public override void Visit(IRForEach forEach)
        {
            if (forEach.BodyBlock == null || forEach.EndBlock == null)
            {
                throw new ForeignFeatureException(
                    "MSIL: a For Each with no body block or no continuation block cannot be "
                    + "lowered. IRBuilder always creates both, so this is an emitter invariant "
                    + "failure.");
            }

            if (!_foreachSlots.TryGetValue(forEach, out var slots))
            {
                throw new ForeignFeatureException(
                    "MSIL: this For Each has no reserved local slots. AllocateForEachLocals "
                    + "reserves the loop variable and the enumerator for every loop in the "
                    + "function before .locals init is written; taking a slot during emission is "
                    + "what made the enumerator overwrite the collection's own local here before.");
            }

            var loopHead = $"foreach_next_{_labelCounter}";
            var loopExit = $"foreach_done_{_labelCounter}";
            _labelCounter++;

            WriteLine($"    // ForEach loop: {SanitizeName(forEach.VariableName)} in {GetValueName(forEach.Collection)}");

            EmitLoadValue(forEach.Collection);
            WriteLine("    callvirt instance class [mscorlib]System.Collections.IEnumerator "
                      + "[mscorlib]System.Collections.IEnumerable::GetEnumerator()");
            EmitStloc(slots.EnumIndex);
            _currentStack--;

            WriteLine($"  {loopHead}:");
            EmitLdloc(slots.EnumIndex);
            _currentStack++;
            WriteLine("    callvirt instance bool [mscorlib]System.Collections.IEnumerator::MoveNext()");
            WriteLine($"    brfalse {loopExit}");
            _currentStack--;

            EmitLdloc(slots.EnumIndex);
            _currentStack++;
            WriteLine("    callvirt instance object [mscorlib]System.Collections.IEnumerator::get_Current()");

            // ⛔ IlTypeToken, not MapType: `unbox.any` takes a TOKEN, so `[mscorlib]System.Int32`
            // and never `class`-prefixed. One instruction covers both halves — ECMA-335 III.4.33
            // makes `unbox.any` on a reference type behave exactly as `castclass` — so the
            // element type does not have to be classified here to be handled correctly.
            var elementToken = IlTypeToken(forEach.ElementType);
            if (elementToken != "[mscorlib]System.Object")
            {
                WriteLine($"    unbox.any {elementToken}");
            }

            EmitStloc(slots.VarIndex);
            _currentStack--;

            // ⚠ No `br {loopHead}` here. EmitForEachBody gives EVERY block it writes a transfer —
            // its own terminator, or an explicit branch to the head when it has none — so a
            // branch appended after the body is unreachable in every shape. The first version
            // emitted one and the generated IL showed it: `br foreach_next_1` twice in a row.
            EmitForEachBody(forEach, slots.VarIndex, loopHead);

            WriteLine($"  {loopExit}:");

            // The continuation, spelled the way the enclosing region allows. EmitForEachBody has
            // already dropped this loop's redirect, so this is the real exit rather than another
            // iteration — which is exactly why the branch is emitted here and not inside it.
            EmitRegionAwareBranch(forEach.EndBlock);
        }

        /// <summary>
        /// Emits every block of one <c>For Each</c> body, ONCE, with the loop variable's name
        /// bound to its slot and the end-of-iteration edge redirected to the loop head.
        ///
        /// <para>Both bindings are saved and restored rather than assigned, which is what makes
        /// nesting work: the inner loop's variable and its continuation are in scope only while
        /// the inner body is being written, and the outer loop's come back afterwards. It is also
        /// what SCOPES the loop variable — after <c>Next</c> the name resolves to whatever it
        /// meant before, as it does in every other backend's emitted loop header.</para>
        ///
        /// <para>The body region stops at the continuation block, and a nested loop's body blocks
        /// are reached as ordinary CFG successors from here — they are collected into this list
        /// and then skipped, because the nested <c>Visit(IRForEach)</c> has already written them
        /// by the time the list reaches them.</para>
        /// </summary>
        private void EmitForEachBody(IRForEach forEach, int varIndex, string loopHead)
        {
            var blocks = CollectRegionBlocks(
                forEach.BodyBlock, new HashSet<BasicBlock> { forEach.EndBlock });

            var hadName = _localIndices.TryGetValue(forEach.VariableName, out var previousIndex);
            _localIndices[forEach.VariableName] = varIndex;

            var hadRedirect = _foreachContinueLabels.TryGetValue(forEach.EndBlock, out var previousHead);
            _foreachContinueLabels[forEach.EndBlock] = loopHead;

            foreach (var block in blocks)
            {
                if (!_consumedBlocks.Add(block)) continue;
                _visitedBlocks?.Add(block);
                WriteLine($"  {SanitizeLabel(block.Name)}:");

                foreach (var instruction in block.Instructions)
                {
                    instruction.Accept(this);
                }

                // An IR block with no terminator falls through to the next block in source order.
                // Inside a loop that is not expressible — the block emitted after it in this list
                // is not necessarily its successor — so make the exit explicit.
                //
                // ⚠ MEASURED DEAD TODAY, AND KEPT ANYWAY. `IsTerminated` is false only for a block
                // whose last instruction is a STRUCTURED one (a nested For Each, or a Try), because
                // IRBuilder terminates every other block it makes. In both of those cases the
                // structured visitor has already emitted an unconditional transfer, so what this
                // writes is unreachable — visible in the generated IL as
                // `br foreach1end` / `br foreach_next_1` back to back for a nested loop, and as a
                // `br` sitting between a closing `catch { }` and the continuation's label for a Try.
                // The mutant that deletes this line therefore SURVIVES.
                //
                // ⛔ It is kept because the deadness is a property of the OTHER visitors, not of
                // this one: nothing here can check that the last instruction emitted a transfer.
                // If that ever stops holding, control falls into this loop's own `loopExit:` label
                // and the loop ends after one iteration — a clean run with a wrong answer, which is
                // the failure class this whole family exists to close. An unreachable `br` costs two
                // bytes; the alternative costs correctness silently.
                if (!block.IsTerminated())
                {
                    WriteLine($"    br {loopHead}");
                }
            }

            if (hadRedirect) _foreachContinueLabels[forEach.EndBlock] = previousHead;
            else _foreachContinueLabels.Remove(forEach.EndBlock);

            if (hadName) _localIndices[forEach.VariableName] = previousIndex;
            else _localIndices.Remove(forEach.VariableName);
        }

        public override void Visit(IRIndexerAccess indexer)
        {
            // MSIL indexer access - load collection, index, then call get_Item
            var resultType = MapType(indexer.Type);

            // Load collection
            EmitLoadValue(indexer.Collection);

            // Load indices
            foreach (var index in indexer.Indices)
            {
                EmitLoadValue(index);
            }

            // Call the indexer on the RECEIVER's own type. This used to be hard-coded to
            // IList`1<resultType>, which is wrong for a Dictionary in both positions: it named
            // the VALUE type as the list's element and passed the KEY as an integer index.
            if (TryCollectionMember(indexer.Collection?.Type, "get_Item", out var collToken, out var collSig))
            {
                WriteLine($"    callvirt instance {collSig.Ret} class {collToken}::get_Item({collSig.Params})");
            }
            else
            {
                var indexTypes = string.Join(", ", indexer.Indices.Select(i => IlTypeSpec(i.Type)));
                WriteLine($"    callvirt instance {resultType} class [mscorlib]System.Collections.Generic.IList`1<{resultType}>::get_Item({indexTypes})");
            }

            // Update stack
            _currentStack -= indexer.Indices.Count; // Pop indices
            // Collection was already on stack, now replaced with result

            // ⛔ Store the result the way EVERY other visit does. This used to hand itself a
            // fresh slot with `_localCounter++` and register it in _localIndices — allocating a
            // local that GenerateLocalsDeclaration had already handed to something else. The
            // result was a silent miscompile: `PrintLine(l(1))` stored the element over the
            // LIST's own slot and then printed a different, uninitialized local, so the program
            // ran, printed garbage, and corrupted the collection for every later use.
            if (!string.IsNullOrEmpty(indexer.Name))
            {
                if (_declaredIdentifiers.Contains(indexer.Name))
                    EmitStoreLocal(indexer.Name);
                else
                    EmitStloc(GetTempIndex(indexer));
                _currentStack--;
            }
        }

        /// <summary>
        /// The WRITE half of <see cref="Visit(IRIndexerAccess)"/> — <c>l(i) = v</c>,
        /// <c>d(k) = v</c>, and the explicit <c>l.Item(i) = v</c> spelling that lowers to the
        /// same node.
        ///
        /// <para>⛔ <b>THIS OVERRIDE DID NOT EXIST.</b>
        /// <see cref="CodeGeneratorBase.Visit(IRIndexerStore)"/> is a <c>virtual { }</c>, so
        /// every indexed write on a collection emitted NOTHING AT ALL — not the call, not the
        /// indices, not even the evaluation of the value — and the program RAN CLEAN and
        /// printed the OLD element. Measured: <c>l(0) = 42</c> on a <c>List(Of Integer)</c>
        /// produced IL containing no <c>set_Item</c> and no <c>ldc.i4 42</c>, and printed
        /// <c>1</c>. On a <c>Dictionary</c> the dropped write turned into a
        /// <c>KeyNotFoundException</c> at the next read of that key.</para>
        ///
        /// <para>⚠ This is the THIRD time a base no-op has silently eaten an instruction on this
        /// backend — <c>IRThrow</c> was the same shape, and
        /// <c>JavaScriptCodeGenerator</c>'s class comment names the hazard by name. The two are
        /// the ONLY <c>virtual</c> visitors on <see cref="CodeGeneratorBase"/>; every other
        /// <c>Visit</c> is <c>abstract</c>, so no third instruction can be lost this way without
        /// someone first adding another <c>virtual { }</c>. An array element write was never
        /// affected: <c>a(i) = v</c> is <c>IRArrayStore</c>, which IS abstract.</para>
        ///
        /// <para><b>Operand order is the whole of the lowering.</b> <c>set_Item</c> is an
        /// ordinary instance call, so IL wants the receiver, then every index, then the value,
        /// and the signature comes from the RECEIVER's own type — exactly as on the read side
        /// and through the same table. <c>List`1&lt;T&gt;::set_Item(int32, !0)</c> indexes by an
        /// integer and takes a generic element; <c>Dictionary`2&lt;K,V&gt;::set_Item(!0, !1)</c>
        /// takes both from the instantiation. Spelling either as the other is a call that
        /// assembles and then dies at run time, which is why neither is inferred here.</para>
        ///
        /// <para><b>Insert-or-update falls out, it is not special-cased.</b>
        /// <c>Dictionary::set_Item</c> ADDS a key that is not present — that is what .NET means
        /// by <c>d(k) = v</c> and what C#, C++ and JavaScript all do — whereas <c>Add</c> would
        /// throw. Calling the accessor the receiver actually declares gets this right with no
        /// per-collection arm.</para>
        ///
        /// <para>A collection whose <c>set_Item</c> is outside the table — a <c>HashSet</c>, a
        /// <c>Queue</c>, a <c>Stack</c> — is REFUSED by <see cref="TryCollectionMember"/> with
        /// the ordinary BasicLang diagnostic rather than guessed at, per
        /// <see cref="CollectionMembers"/>.</para>
        /// </summary>
        public override void Visit(IRIndexerStore indexerStore)
        {
            // Receiver, then indices, then value — the argument order of the accessor.
            EmitLoadValue(indexerStore.Collection);

            foreach (var index in indexerStore.Indices)
            {
                EmitLoadValue(index);
            }

            EmitLoadValue(indexerStore.Value);

            if (TryCollectionMember(indexerStore.Collection?.Type, "set_Item", out var collToken, out var collSig))
            {
                // ⚠ The IL NAME comes from the table too, not from the string that was looked
                // up. Re-spelling it here made `CollectionMember.Il` dead on this path: a
                // mutation that changed Dictionary's row to call `Add` — which throws on an
                // existing key instead of updating it — SURVIVED the whole fixture, because
                // nothing read the field. The row is the single authority for all three parts
                // of the signature or for none of them. (The read path above still spells
                // `get_Item` literally; both rows happen to agree, so it emits the same text,
                // but it carries the same latent hazard.)
                WriteLine($"    callvirt instance {collSig.Ret} class {collToken}::{collSig.Il}({collSig.Params})");
            }
            else
            {
                // The receiver is not one of the collections this backend can NAME. The read
                // side has always fallen back to IList`1 here; the write mirrors it rather than
                // inventing a second guess, so the two halves of one indexer cannot disagree
                // about the type they are calling on.
                var elementSpec = IlTypeSpec(indexerStore.Value?.Type);
                var indexTypes = string.Join(", ", indexerStore.Indices.Select(i => IlTypeSpec(i.Type)));
                WriteLine($"    callvirt instance void class [mscorlib]System.Collections.Generic.IList`1<{elementSpec}>::set_Item({indexTypes}, {elementSpec})");
            }

            // Receiver + every index + the value all consumed; set_Item returns void.
            _currentStack -= 2 + indexerStore.Indices.Count;
        }

        #endregion

        private void WriteLine(string text = "")
        {
            _output.AppendLine(text);
        }
    }

    public class MSILCodeGenOptions
    {
        public bool GenerateComments { get; set; } = true;
        public string AssemblyName { get; set; } = "GeneratedAssembly";
    }
}
