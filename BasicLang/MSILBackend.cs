using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.SemanticAnalysis;

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
            var paramTypes = string.Join(", ", irDelegate.Parameters.Select(p => MapTypeName(p.TypeName)));

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
            return typeName.ToLowerInvariant() switch
            {
                "integer" => "int32",
                "long" => "int64",
                "single" => "float32",
                "double" => "float64",
                "string" => "string",
                "boolean" => "bool",
                "byte" => "uint8",
                "short" => "int16",
                "object" => "object",
                "void" => "void",
                _ => SanitizeName(typeName)
            };
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
                var paramTypes = string.Join(", ", method.Parameters.Select(p => MapTypeName(p.TypeName)));

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

            // Build extends and implements
            var extends = "[mscorlib]System.Object";
            if (!string.IsNullOrEmpty(irClass.BaseClass))
            {
                extends = SanitizeName(irClass.BaseClass);
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
                var fieldType = MapType(field.Type);
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
                    InitializeMethodContext(prop.Getter);
                    var visited = new HashSet<BasicBlock>();
                    GenerateBasicBlock(prop.Getter.EntryBlock, visited, isEntry: true);
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
                    InitializeMethodContext(prop.Setter);
                    var visited = new HashSet<BasicBlock>();
                    GenerateBasicBlock(prop.Setter.EntryBlock, visited, isEntry: true);
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
                    $"{MapType(p.Type)} {SanitizeName(p.Name)}"));
            }

            WriteLine("  .method public hidebysig specialname rtspecialname");
            WriteLine($"          instance void .ctor({paramTypes}) cil managed");
            WriteLine("  {");
            WriteLine("    .maxstack 8");

            // Call base constructor
            var baseClass = string.IsNullOrEmpty(irClass.BaseClass) ? "[mscorlib]System.Object" : SanitizeName(irClass.BaseClass);
            WriteLine("    ldarg.0");
            WriteLine($"    call instance void {baseClass}::.ctor()");

            // Generate constructor body
            if (ctor.Implementation?.EntryBlock != null)
            {
                _currentFunction = ctor.Implementation;
                InitializeMethodContext(ctor.Implementation);
                var visited = new HashSet<BasicBlock>();
                GenerateBasicBlock(ctor.Implementation.EntryBlock, visited, isEntry: true);
                _currentFunction = null;
            }

            if (!EndsWithRet())
                WriteLine("    ret");

            WriteLine("  } // end of method .ctor");
            WriteLine();
        }

        private void GenerateDefaultCtorForClass(IRClass irClass)
        {
            var baseClass = string.IsNullOrEmpty(irClass.BaseClass) ? "[mscorlib]System.Object" : SanitizeName(irClass.BaseClass);

            WriteLine("  .method public hidebysig specialname rtspecialname");
            WriteLine("          instance void .ctor() cil managed");
            WriteLine("  {");
            WriteLine("    .maxstack 8");
            WriteLine("    ldarg.0");
            WriteLine($"    call instance void {baseClass}::.ctor()");
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
                    $"{MapType(p.Type)} {SanitizeName(p.Name)}"));
            }

            WriteLine($"  .method public hidebysig {modifiers}{staticMod}");
            WriteLine($"          {instanceMod}{returnType} {methodName}({paramTypes}) cil managed");
            WriteLine("  {");

            if (method.Implementation != null && !method.IsAbstract)
            {
                _currentFunction = method.Implementation;
                InitializeMethodContext(method.Implementation);

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
                    var visited = new HashSet<BasicBlock>();
                    GenerateBasicBlock(method.Implementation.EntryBlock, visited, isEntry: true);
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

        private void InitializeMethodContext(IRFunction function)
        {
            _localIndices.Clear();
            _paramIndices.Clear();
            _tempIndices.Clear();
            _tempNameIndices.Clear();
            _declaredIdentifiers.Clear();
            _localCounter = 0;
            _maxStack = 8;
            _currentStack = 0;

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

            AllocateTemporaries(function);
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
                $"{MapType(p.Type)} {SanitizeName(p.Name)}"));

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

            // Generate method body
            if (function.EntryBlock != null)
            {
                var visitedBlocks = new HashSet<BasicBlock>();
                GenerateBasicBlock(function.EntryBlock, visitedBlocks, isEntry: true);
            }

            // Ensure method ends with ret
            if (returnType == "void" && !EndsWithRet())
            {
                WriteLine("    ret");
            }

            WriteLine($"  }} // end of method {methodName}");
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
            var locals = new List<string>();

            // Declared local variables
            foreach (var local in function.LocalVariables)
            {
                var localType = IlTypeSpec(local.Type);
                locals.Add($"      [{_localIndices[local.Name]}] {localType} {SanitizeName(local.Name)}");
            }

            // Temporary variables
            foreach (var (temp, index) in _tempIndices)
            {
                var tempType = IlTypeSpec(temp.Type);
                locals.Add($"      [{index}] {tempType} V_{index}");
            }

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

            WriteLine($"    // WARNING: Unknown local '{name}'");
        }

        private void EmitStoreLocal(string name)
        {
            var idx = GetLocalIndex(name);
            if (idx >= 0)
            {
                EmitStloc(idx);
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

            switch (compare.Comparison)
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
                return;
            }

            // Load arguments
            foreach (var arg in call.Arguments)
            {
                EmitLoadValue(arg);
            }

            // Generate call
            var returnType = MapType(call.Type);
            var paramTypes = string.Join(", ", call.Arguments.Select(a => MapType(a.Type)));
            var sanitizedName = SanitizeName(funcName);

            // Use module name for class reference
            var className = _moduleName ?? "Program";
            WriteLine($"    call {returnType} {className}::{sanitizedName}({paramTypes})");

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

        private bool TryEmitStdLibCall(string funcName, List<IRValue> args, bool hasReturn)
        {
            var lower = funcName.ToLower();

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
            if (ret.Value != null)
            {
                EmitLoadValue(ret.Value);
            }
            WriteLine("    ret");
        }

        public override void Visit(IRBranch branch)
        {
            var target = SanitizeLabel(branch.Target.Name);
            WriteLine($"    br {target}");
        }

        public override void Visit(IRConditionalBranch condBranch)
        {
            EmitLoadValue(condBranch.Condition);

            var trueTarget = SanitizeLabel(condBranch.TrueTarget.Name);
            var falseTarget = SanitizeLabel(condBranch.FalseTarget.Name);

            WriteLine($"    brtrue {trueTarget}");
            WriteLine($"    br {falseTarget}");
            _currentStack--;
        }

        public override void Visit(IRSwitch switchInst)
        {
            EmitLoadValue(switchInst.Value);

            var targets = switchInst.Cases.Select(c => SanitizeLabel(c.Target.Name)).ToList();
            var targetList = string.Join(", ", targets);

            WriteLine($"    switch ({targetList})");
            WriteLine($"    br {SanitizeLabel(switchInst.DefaultTarget.Name)}");
            _currentStack--;
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

        public override void Visit(IRArrayAlloc arrayAlloc)
        {
            var elementType = MapType(arrayAlloc.ElementType);
            WriteLine($"    ldc.i4 {arrayAlloc.Size}");
            WriteLine($"    newarr {elementType}");
            WriteLine($"    stloc {arrayAlloc.Name}");
        }

        public override void Visit(IRArrayStore arrayStore)
        {
            var elementType = MapType(arrayStore.Array.Type?.ElementType ?? new TypeInfo("object", TypeKind.Class));
            WriteLine($"    ldloc {arrayStore.Array.Name}");
            if (arrayStore.Index is IRConstant c)
                WriteLine($"    ldc.i4 {c.Value}");
            else
                WriteLine($"    ldloc {arrayStore.Index.Name}");
            if (arrayStore.Value is IRConstant vc)
                EmitLoadConstant(vc);
            else
                WriteLine($"    ldloc {arrayStore.Value.Name}");
            WriteLine($"    stelem {elementType}");
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

        public override void Visit(IRNewObject newObj)
        {
            // Generate newobj instruction. A collection construction needs its BCL generic
            // token — `newobj instance void List::.ctor()` names nothing.
            var className = TryCollectionToken(newObj.Type, out var collectionToken)
                ? "class " + collectionToken
                : SanitizeName(newObj.ClassName);

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

        public override void Visit(IRTryCatch tryCatch)
        {
            // MSIL exception handling with .try/.catch directives
            WriteLine("    .try");
            WriteLine("    {");
            foreach (var inst in tryCatch.TryBlock.Instructions)
            {
                if (inst is IRBranch or IRConditionalBranch) continue;
                inst.Accept(this);
            }
            WriteLine("        leave EndTry");
            WriteLine("    }");

            foreach (var catchClause in tryCatch.CatchClauses)
            {
                var exType = catchClause.ExceptionType?.Name ?? "[mscorlib]System.Exception";
                if (!exType.StartsWith("["))
                    exType = $"[mscorlib]System.{exType}";
                WriteLine($"    catch {exType}");
                WriteLine("    {");

                // Store exception to local if variable name is provided
                if (!string.IsNullOrEmpty(catchClause.VariableName))
                {
                    var varName = SanitizeName(catchClause.VariableName);
                    if (!_localIndices.ContainsKey(varName))
                    {
                        var localIndex = _localCounter++;
                        _localIndices[varName] = localIndex;
                    }
                    WriteLine($"        stloc {_localIndices[varName]}");
                }
                else
                {
                    WriteLine("        pop"); // Pop exception if not used
                }

                foreach (var inst in catchClause.Block.Instructions)
                {
                    if (inst is IRBranch or IRConditionalBranch) continue;
                    inst.Accept(this);
                }
                WriteLine("        leave EndTry");
                WriteLine("    }");
            }

            WriteLine("EndTry:");
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
