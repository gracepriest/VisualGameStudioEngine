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

        /// <summary>The IL name of the type whose fields <see cref="_currentClassFields"/> holds.</summary>
        private string _currentClassToken;

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
            var returnType = MapType(irDelegate.ReturnType);
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
                var returnType = MapType(method.ReturnType);
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
                var propType = MapType(prop.Type);
                var propName = SanitizeName(prop.Name);

                WriteLine($"  .property instance {propType} {propName}()");
                WriteLine("  {");
                if (prop.HasGetter)
                    WriteLine($"    .get instance {propType} {interfaceName}::get_{propName}()");
                if (prop.HasSetter)
                    WriteLine($"    .set instance void {interfaceName}::set_{propName}({propType})");
                WriteLine("  }");
                WriteLine();

                // Getter method
                if (prop.HasGetter)
                {
                    WriteLine("  .method public hidebysig newslot specialname abstract virtual");
                    WriteLine($"          instance {propType} get_{propName}() cil managed");
                    WriteLine("  {");
                    WriteLine("  }");
                }

                // Setter method
                if (prop.HasSetter)
                {
                    WriteLine("  .method public hidebysig newslot specialname abstract virtual");
                    WriteLine($"          instance void set_{propName}({propType} 'value') cil managed");
                    WriteLine("  {");
                    WriteLine("  }");
                }
            }

            WriteLine($"}} // end of interface {interfaceName}");
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

            WriteLine($".class public auto ansi beforefieldinit {className}");
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
            var eventName = SanitizeName(evt.Name);
            var staticMod = evt.IsStatic ? "static " : "";

            // Backing field
            WriteLine($"  .field private {staticMod}class {delegateType} {eventName}");

            // Event declaration
            WriteLine($"  .event class {delegateType} {eventName}");
            WriteLine("  {");
            WriteLine($"    .addon instance void {SanitizeName(irClass.Name)}::add_{eventName}(class {delegateType})");
            WriteLine($"    .removeon instance void {SanitizeName(irClass.Name)}::remove_{eventName}(class {delegateType})");
            WriteLine("  }");
            WriteLine();

            // Add method
            WriteLine($"  .method public hidebysig specialname {staticMod}instance void");
            WriteLine($"          add_{eventName}(class {delegateType} 'value') cil managed");
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
            WriteLine($"          remove_{eventName}(class {delegateType} 'value') cil managed");
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

        private void GenerateProperty(IRClass irClass, IRProperty prop)
        {
            var propType = MapType(prop.Type);
            var propName = SanitizeName(prop.Name);
            var className = SanitizeName(irClass.Name);
            var staticMod = prop.IsStatic ? "static " : "";
            var instanceMod = prop.IsStatic ? "" : "instance ";

            // Property declaration
            WriteLine($"  .property {instanceMod}{propType} {propName}()");
            WriteLine("  {");
            if (!prop.IsWriteOnly)
                WriteLine($"    .get {instanceMod}{propType} {className}::get_{propName}()");
            if (!prop.IsReadOnly)
                WriteLine($"    .set {instanceMod}void {className}::set_{propName}({propType})");
            WriteLine("  }");
            WriteLine();

            // Getter
            if (prop.Getter != null && !prop.IsWriteOnly)
            {
                var virtualMod = "";  // Could add virtual if needed
                WriteLine($"  .method public hidebysig specialname {virtualMod}{staticMod}");
                WriteLine($"          {instanceMod}{propType} get_{propName}() cil managed");
                WriteLine("  {");
                WriteLine("    .maxstack 8");

                if (prop.Getter.EntryBlock != null)
                {
                    _currentFunction = prop.Getter;
                    InitializeMethodContext(prop.Getter, isInstance: !prop.IsStatic, owner: irClass);
                    // _visitedBlocks, not a local set: Visit(IRTryCatch) marks a region's blocks THERE so
                    // GenerateBasicBlock will not write them a second time. A local set left the two
                    // disagreeing, and a Try inside a class member emitted every region block twice
                    // ("Duplicate label").
                    _visitedBlocks = new HashSet<BasicBlock>();
                    var visited = _visitedBlocks;
                    GenerateBasicBlock(prop.Getter.EntryBlock, visited, isEntry: true);
                    EmitLoweredReturnExit();
                    _currentFunction = null;
                }
                else
                {
                    // Default: return default value
                    WriteLine($"    ldnull");
                    WriteLine("    ret");
                }

                WriteLine($"  }} // end of method get_{propName}");
                WriteLine();
            }

            // Setter
            if (prop.Setter != null && !prop.IsReadOnly)
            {
                var virtualMod = "";
                WriteLine($"  .method public hidebysig specialname {virtualMod}{staticMod}");
                WriteLine($"          {instanceMod}void set_{propName}({propType} 'value') cil managed");
                WriteLine("  {");
                WriteLine("    .maxstack 8");

                if (prop.Setter.EntryBlock != null)
                {
                    _currentFunction = prop.Setter;
                    InitializeMethodContext(prop.Setter, isInstance: !prop.IsStatic, owner: irClass);
                    // _visitedBlocks, not a local set: Visit(IRTryCatch) marks a region's blocks THERE so
                    // GenerateBasicBlock will not write them a second time. A local set left the two
                    // disagreeing, and a Try inside a class member emitted every region block twice
                    // ("Duplicate label").
                    _visitedBlocks = new HashSet<BasicBlock>();
                    var visited = _visitedBlocks;
                    GenerateBasicBlock(prop.Setter.EntryBlock, visited, isEntry: true);
                    EmitLoweredReturnExit();
                    _currentFunction = null;
                }

                if (!EndsWithRet())
                    WriteLine("    ret");

                WriteLine($"  }} // end of method set_{propName}");
                WriteLine();
            }
        }

        private void GenerateConstructor(IRClass irClass, IRConstructor ctor)
        {
            var className = SanitizeName(irClass.Name);
            var paramTypes = "";

            if (ctor.Implementation != null)
            {
                paramTypes = string.Join(", ", ctor.Implementation.Parameters.Select(p =>
                    $"{IlTypeSpec(p.Type)} {SanitizeName(p.Name)}"));
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
            WriteLine($"    call instance void {baseClass}::.ctor()");

            EmitArrayFieldAllocations(irClass);

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

        private void GenerateDefaultCtorForClass(IRClass irClass)
        {
            var baseClass = string.IsNullOrEmpty(irClass.BaseClass) ? "[mscorlib]System.Object" : IlTypeToken(irClass.BaseClass);

            WriteLine("  .method public hidebysig specialname rtspecialname");
            WriteLine("          instance void .ctor() cil managed");
            WriteLine("  {");
            WriteLine("    .maxstack 8");
            WriteLine("    ldarg.0");
            WriteLine($"    call instance void {baseClass}::.ctor()");
            EmitArrayFieldAllocations(irClass);
            WriteLine("    ret");
            WriteLine("  } // end of method .ctor");
            WriteLine();
        }

        private void GenerateClassMethod(IRClass irClass, IRMethod method)
        {
            var className = SanitizeName(irClass.Name);
            var methodName = SanitizeName(method.Name);
            var returnType = MapType(method.ReturnType);
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

            var paramTypes = "";
            if (method.Implementation != null)
            {
                paramTypes = string.Join(", ", method.Implementation.Parameters.Select(p =>
                    $"{IlTypeSpec(p.Type)} {SanitizeName(p.Name)}"));
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
            _tempIndices.Clear();
            _tempNameIndices.Clear();
            _declaredIdentifiers.Clear();
            _localCounter = 0;
            _maxStack = 8;
            _currentStack = 0;

            // The same per-method reset GenerateMethod does. Without it a Try inside a class
            // member would find no exception-handling state prepared for it.
            _syntheticLocals.Clear();
            _regionBlocks = null;
            _regionIsFinally = false;
            _regionIsCatch = false;
            _regionLeaveTarget = null;
            _methodExitResultLocal = -1;
            _methodExitUsed = false;
            _methodExitLabel = $"eh_exit_{_labelCounter++}";

            _currentMethodIsInstance = isInstance;
            _currentClassFields.Clear();
            _fieldStoreScratch.Clear();
            _currentClassToken = owner != null ? SanitizeName(owner.Name) : null;

            if (isInstance && owner?.Fields != null)
            {
                foreach (var field in owner.Fields)
                {
                    if (field.IsStatic || string.IsNullOrEmpty(field.Name)) continue;
                    _currentClassFields[field.Name] = field.Type;

                    // Register the field as a name the body can resolve. This is what routes an
                    // assignment's destination through EmitStoreLocal rather than into a temp,
                    // where `N = N + 1` used to silently land and be dropped.
                    _declaredIdentifiers.Add(field.Name);
                }
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

            foreach (var local in function.LocalVariables)
            {
                _declaredIdentifiers.Add(local.Name);
                _localIndices[local.Name] = _localIndices.Count;
            }

            AllocateExceptionHandlingLocals(function);
            AllocateFieldStoreScratch(function);
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
            if (!_currentMethodIsInstance || _currentClassFields.Count == 0) return;

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
                    if (!_currentClassFields.TryGetValue(target, out var fieldType)) continue;
                    if (_fieldStoreScratch.ContainsKey(target)) continue;

                    var index = _localIndices.Count;
                    var name = $"fld_scratch_{SanitizeName(target)}";
                    _localIndices[name] = index;
                    _fieldStoreScratch[target] = index;
                    _syntheticLocals.Add((index, IlTypeSpec(fieldType), name));
                }
            }
        }

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
            WriteLine($".module {SanitizeName(module.Name)}.exe");
            WriteLine();
        }

        private void GenerateClass(IRModule module)
        {
            _moduleName = SanitizeName(module.Name);

            WriteLine($".class public auto ansi beforefieldinit {_moduleName}");
            WriteLine("       extends [mscorlib]System.Object");
            WriteLine("{");

            // Generate methods
            foreach (var function in module.Functions)
            {
                if (!function.IsExternal)
                {
                    GenerateMethod(function);
                    WriteLine();
                }
            }

            // Generate default constructor
            GenerateDefaultConstructor();

            WriteLine("} // end of class " + _moduleName);
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
            _tempIndices.Clear();
            _tempNameIndices.Clear();
            _declaredIdentifiers.Clear();
            _localCounter = 0;
            _maxStack = 8; // Default, will be calculated
            _currentStack = 0;

            // Exception-handling state is per METHOD: a region, its synthetic locals and its exit
            // label never outlive the method they were emitted for.
            _syntheticLocals.Clear();
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

            foreach (var local in function.LocalVariables)
            {
                _declaredIdentifiers.Add(local.Name);
                _localIndices[local.Name] = _localIndices.Count;
            }

            // Slots for the things a Try needs that IRFunction.LocalVariables does not carry.
            // MUST run before AllocateTemporaries: temp indices continue from _localIndices.Count,
            // and .locals init is written from these tables before any body instruction exists.
            AllocateExceptionHandlingLocals(function);

            // Allocate indices for temporaries
            AllocateTemporaries(function);

            // Generate method signature
            var returnType = MapType(function.ReturnType);
            var methodName = SanitizeName(function.Name);
            var isMain = methodName.Equals("Main", StringComparison.OrdinalIgnoreCase);

            // Method attributes
            WriteLine($"  .method public hidebysig static");

            // Parameters
            var paramList = string.Join(", ", function.Parameters.Select(p =>
                $"{IlTypeSpec(p.Type)} {SanitizeName(p.Name)}"));

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

                var name = SanitizeName(clause.VariableName);
                if (_localIndices.ContainsKey(name)) continue;

                var index = _localIndices.Count;
                _localIndices[name] = index;
                _declaredIdentifiers.Add(clause.VariableName);
                _syntheticLocals.Add((index, IlTypeSpec(clause.ExceptionType ?? ExceptionTypeInfo), name));
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
                if (!_localIndices.TryGetValue(SanitizeName(local.Name), out var index)) continue;

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
        /// Allocates every sized array FIELD in a constructor, after the base call and before any
        /// constructor body can touch one.
        ///
        /// <para>Fields are the second site, and they are not optional. The C++ backend's own
        /// note records that its first version of this fix lived inline in the locals loop and
        /// left array fields unsized, which turned "does not build" into "builds and
        /// access-violates". One helper, both constructor paths — the explicit one and the
        /// generated default — because a class with a declared constructor never reaches the
        /// other.</para>
        /// </summary>
        private void EmitArrayFieldAllocations(IRClass irClass)
        {
            if (irClass?.Fields == null) return;

            foreach (var field in irClass.Fields)
            {
                if (field.IsStatic) continue;   // a static field is not this instance's to create
                if (!TryArrayAllocation(field.Type, out var elementToken, out var length)) continue;

                WriteLine("    ldarg.0");
                EmitLdcI4(length);
                WriteLine($"    newarr {elementToken}");
                WriteLine($"    stfld {IlTypeSpec(field.Type)} {SanitizeName(irClass.Name)}::{SanitizeName(field.Name)}");
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

        private string SanitizeLabel(string name)
        {
            return SanitizeName(name).Replace(".", "_");
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
                WriteLine($"    ldfld {IlTypeSpec(fieldType)} {_currentClassToken}::{SanitizeName(name)}");
                return;
            }

            WriteLine($"    // WARNING: Unknown local '{name}'");
        }

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
                WriteLine($"    stfld {IlTypeSpec(fieldType)} {_currentClassToken}::{SanitizeName(name)}");
                _currentStack -= 2;
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

            // Load operands onto stack
            EmitLoadValue(binaryOp.Left);
            EmitLoadValue(binaryOp.Right);

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
            EmitLoadValue(compare.Left);
            EmitLoadValue(compare.Right);

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

            // ⛔ A SIBLING call inside an instance method — `Return Inner() + 1` — needs the
            // receiver pushed first and a `callvirt instance`. It used to fall through to the
            // module-class arm below and emit `call int32 Program::Inner()`: a static call, on a
            // class that does not exist, to a method that is not static. `_moduleName` is null
            // while a class body is being emitted, which is where the phantom `Program` came from.
            var isSelfCall = _currentMethodIsInstance
                && _currentClassToken != null
                && _currentClass?.Methods != null
                && _currentClass.Methods.Any(m => !m.IsStatic
                    && string.Equals(m.Name, funcName, StringComparison.OrdinalIgnoreCase));

            if (isSelfCall) EmitLdarg(0);

            // Load arguments
            foreach (var arg in call.Arguments)
            {
                EmitLoadValue(arg);
            }

            // Generate call
            // Type SPECS: the declaration these resolve to spells its parameters the same way,
            // and a call whose signature disagrees with the declaration binds to nothing.
            var returnType = IlTypeSpec(call.Type);
            var paramTypes = string.Join(", ", call.Arguments.Select(a => IlTypeSpec(a.Type)));
            var sanitizedName = SanitizeName(funcName);

            if (isSelfCall)
            {
                WriteLine($"    callvirt instance {returnType} {_currentClassToken}::{sanitizedName}({paramTypes})");
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

        private bool TryEmitStdLibCall(string funcName, List<IRValue> args, bool hasReturn)
        {
            var lower = ResolveStdLibArm(funcName);

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

                case "cint":
                    EmitLoadValue(args[0]);
                    WriteLine("    conv.i4");
                    return true;

                case "clng":
                    EmitLoadValue(args[0]);
                    WriteLine("    conv.i8");
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
            EmitRegionAwareBranch(branch.Target);
        }

        public override void Visit(IRConditionalBranch condBranch)
        {
            EmitLoadValue(condBranch.Condition);
            _currentStack--;

            var trueTarget = condBranch.TrueTarget;
            var falseTarget = condBranch.FalseTarget;

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
        /// An unconditional transfer to <paramref name="target"/>, spelled the way the CURRENT
        /// region allows: <c>br</c> within the region (or outside any), <c>leave</c> out of a
        /// try/catch, and <c>endfinally</c> out of a finally — where the target is implicit,
        /// because a finally resumes whatever unwinding or <c>leave</c> entered it and cannot
        /// choose its own destination.
        /// </summary>
        private void EmitRegionAwareBranch(BasicBlock target)
        {
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
                    EmitInlineValue(binaryOp.Left);
                    EmitInlineValue(binaryOp.Right);
                    WriteLine($"    {_typeMapper.MapBinaryOperator(binaryOp.Operation)}");
                    _currentStack--;
                    return;

                case IRCompare compare:
                    EmitInlineValue(compare.Left);
                    EmitInlineValue(compare.Right);
                    EmitCompareOpcodes(compare.Comparison);
                    _currentStack--;
                    return;

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
            var paramTypes = string.Join(", ", newObj.Arguments.Select(a => MapType(a.Type)));

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

            // Load 'this' reference (the object on which the method is called)
            EmitLoadValue(methodCall.Object);

            // Load arguments
            foreach (var arg in methodCall.Arguments)
            {
                EmitLoadValue(arg);
            }

            // Build method signature
            string returnType, paramTypes, className, methodName;
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
                returnType = IlTypeSpec(methodCall.Type);
                paramTypes = string.Join(", ", methodCall.Arguments.Select(a => IlTypeSpec(a.Type)));
                className = IlReceiverToken(methodCall.Object?.Type);
                methodName = SanitizeName(methodCall.MethodName);
            }
            // Use callvirt for virtual dispatch (polymorphic behavior)
            // For non-virtual calls, the backend should use 'call instance' instead, but callvirt is safer as default
            var callInstruction = methodCall.IsVirtual || !methodCall.IsVirtual ? "callvirt" : "call";
            WriteLine($"    {callInstruction} instance {returnType} {className}::{methodName}({paramTypes})");

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
            var returnType = MapType(baseCall.Type);
            var paramTypes = string.Join(", ", baseCall.Arguments.Select(a => MapType(a.Type)));
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
            var className = IlReceiverToken(fieldAccess.Object?.Type);
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
            // Load object reference
            EmitLoadValue(fieldStore.Object);

            // Load value to store
            EmitLoadValue(fieldStore.Value);

            // Store value to field
            var fieldType = MapType(fieldStore.Value?.Type);
            var className = fieldStore.Object?.Type?.Name != null ? SanitizeName(fieldStore.Object.Type.Name) : "object";
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

            var name = SanitizeName(clause.VariableName);
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

        public override void Visit(IRForEach forEach)
        {
            // MSIL foreach uses GetEnumerator pattern
            var elemType = MapType(forEach.ElementType);
            var varName = SanitizeName(forEach.VariableName);
            var collectionVal = GetValueName(forEach.Collection);

            var loopStart = $"foreach_start_{_labelCounter}";
            var loopBody = $"foreach_body_{_labelCounter}";
            var loopEnd = $"foreach_end_{_labelCounter}";
            _labelCounter++;

            WriteLine($"    // ForEach loop: {varName} in {collectionVal}");

            // Get enumerator
            EmitLoadValue(forEach.Collection);
            WriteLine($"    callvirt instance class [mscorlib]System.Collections.IEnumerator [mscorlib]System.Collections.IEnumerable::GetEnumerator()");
            var enumLocal = _localCounter++;
            WriteLine($"    stloc.s {enumLocal}");
            _currentStack--;

            // Loop start - MoveNext check
            WriteLine($"  {loopStart}:");
            WriteLine($"    ldloc.s {enumLocal}");
            WriteLine($"    callvirt instance bool [mscorlib]System.Collections.IEnumerator::MoveNext()");
            WriteLine($"    brfalse.s {loopEnd}");
            _currentStack++;
            _currentStack--;

            // Loop body - get Current
            WriteLine($"  {loopBody}:");
            WriteLine($"    ldloc.s {enumLocal}");
            WriteLine($"    callvirt instance object [mscorlib]System.Collections.IEnumerator::get_Current()");
            _currentStack++;

            // Cast to element type if needed
            if (elemType != "object")
            {
                WriteLine($"    unbox.any {elemType}");
            }

            // Store in loop variable
            var varLocal = _localCounter++;
            _localIndices[varName] = varLocal;
            WriteLine($"    stloc.s {varLocal}");
            _currentStack--;

            // Process body block
            if (forEach.BodyBlock != null)
            {
                foreach (var inst in forEach.BodyBlock.Instructions)
                {
                    if (inst is IRBranch or IRConditionalBranch) continue;
                    inst.Accept(this);
                }
            }

            // Jump back to loop start
            WriteLine($"    br.s {loopStart}");

            // Loop end
            WriteLine($"  {loopEnd}:");
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
