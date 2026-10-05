using System;
using System.Collections.Generic;
using System.Linq;
using BasicLang.Compiler.IR;
using BasicLang.Compiler.SemanticAnalysis;

namespace BasicLang.Compiler.CodeGen
{
    /// <summary>
    /// Which interfaces a class implements, which interface ACCESSORS its properties fill, and
    /// which interface members it leaves to an inherited property or method — one set of answers
    /// shared by every backend that has to know.
    ///
    /// <para>⛔ AN IMPLEMENTING ACCESSOR IS NOT AN ORDINARY METHOD on either native-ish backend.
    /// On C++ it must carry EXACTLY the interface's signature or it does not override, the class
    /// stays abstract, and <c>make_shared</c> refuses it. On MSIL it must be <c>virtual</c> or the
    /// type does not load (TypeLoadException "does not have an implementation"). Both backends
    /// ask the same questions, and a second hand-rolled walk would drift from the first
    /// (ADR-0004 D1).</para>
    ///
    /// <para>⚠ ONLY THE INTERFACES A CLASS LISTS ITSELF. A class that inherits an interface from
    /// its base does not re-implement it: the base's mapping stands (on MSIL the derived class's
    /// <c>implements</c> clause does not even name it), so a same-named member the derived class
    /// declares fills no slot and must stay an ordinary member. An earlier version also walked
    /// the base-class chain here; no program on any backend could tell the difference (measured:
    /// an Overrides chain, a plain redeclaration, and the method twins of both), so it was
    /// removed rather than kept as untestable code.</para>
    /// </summary>
    internal static class InterfaceImplementationLookup
    {
        /// <summary>
        /// The interfaces <paramref name="irClass"/> names in its own <c>Implements</c> list, plus
        /// every base interface of those, each yielded once. A name absent from
        /// <paramref name="module"/> is skipped; a cycle is cut, not followed.
        /// </summary>
        public static IEnumerable<IRInterface> ImplementedInterfaces(IRModule module, IRClass irClass)
        {
            if (module?.Interfaces == null || irClass == null) yield break;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pending = new Queue<string>(irClass.Interfaces ?? new List<string>());
            while (pending.Count > 0)
            {
                var name = pending.Dequeue();
                if (string.IsNullOrEmpty(name) || !seen.Add(name)) continue;
                if (!module.Interfaces.TryGetValue(name, out var iface) || iface == null) continue;

                yield return iface;
                foreach (var b in iface.BaseInterfaces ?? new List<string>()) pending.Enqueue(b);
            }
        }

        /// <summary>
        /// True when an interface <paramref name="irClass"/> implements declares a property named
        /// <paramref name="propertyName"/> WITH the getter (<paramref name="getter"/> true) or the
        /// setter (false) — i.e. the class's own accessor of that kind fills an interface slot.
        /// Reads <see cref="IRInterfaceProperty.HasGetter"/>/<see cref="IRInterfaceProperty.HasSetter"/>
        /// as "declares this accessor" (ADR-0002).
        /// </summary>
        public static bool ImplementsInterfaceAccessor(IRModule module, IRClass irClass, string propertyName, bool getter)
        {
            if (string.IsNullOrEmpty(propertyName)) return false;
            return ImplementedInterfaces(module, irClass).Any(i => i.Properties != null && i.Properties.Any(p =>
                p?.Name != null
                && string.Equals(p.Name, propertyName, StringComparison.OrdinalIgnoreCase)
                && (getter ? p.HasGetter : p.HasSetter)));
        }

        /// <summary>
        /// An interface accessor that <see cref="Class"/> must implement but does not declare:
        /// the member that fills it is inherited from <see cref="DeclaringClass"/>.
        /// </summary>
        public sealed class InheritedAccessor
        {
            public IRClass Class { get; init; }
            public IRInterface Interface { get; init; }
            public IRInterfaceProperty InterfaceProperty { get; init; }
            public IRClass DeclaringClass { get; init; }
            public IRProperty InheritedProperty { get; init; }
            public bool Getter { get; init; }
        }

        /// <summary>
        /// Every interface accessor <paramref name="irClass"/> takes on through its OWN
        /// <c>Implements</c> list but leaves to an INHERITED property — <c>Class Holder :
        /// Inherits BaseHolder : Implements IHolder</c> where only <c>BaseHolder</c> declares
        /// <c>Slot</c>.
        ///
        /// <para>⛔ NEITHER BACKEND LINKS THE TWO BY ITSELF. On C++ <c>BaseHolder::get_Slot</c>
        /// does not override <c>IHolder::get_Slot</c> — they are unrelated bases — so
        /// <c>Holder</c> stays abstract. On MSIL <c>BaseHolder</c>'s accessor is not virtual, and
        /// <c>Holder</c>, the class that lists the interface, fails to load. Each backend
        /// therefore emits a forwarder in <c>Holder</c> for every entry here. Measured: this
        /// shape ran on both at HEAD only because the flag fix had not yet made the interface
        /// declare its accessors.</para>
        ///
        /// <para>A property the class declares itself is never listed — its own accessors fill
        /// the slot. The nearest base that declares the property wins; a Shared one, or one
        /// without the accessor, yields nothing (the class stays as broken as it was).</para>
        /// </summary>
        public static IEnumerable<InheritedAccessor> InheritedInterfaceAccessors(IRModule module, IRClass irClass)
        {
            if (irClass == null) yield break;

            var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var iface in ImplementedInterfaces(module, irClass))
            {
                foreach (var ip in iface.Properties ?? new List<IRInterfaceProperty>())
                {
                    if (ip?.Name == null) continue;
                    if (irClass.Properties.Any(p => string.Equals(p.Name, ip.Name, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    var (owner, inherited) = FindInheritedProperty(module, irClass, ip.Name);
                    if (inherited == null || inherited.IsStatic) continue;

                    foreach (var getter in new[] { true, false })
                    {
                        if (!(getter ? ip.HasGetter : ip.HasSetter)) continue;
                        if (!HasAccessor(inherited, getter)) continue;
                        if (!emitted.Add((getter ? "get_" : "set_") + ip.Name)) continue;

                        yield return new InheritedAccessor
                        {
                            Class = irClass,
                            Interface = iface,
                            InterfaceProperty = ip,
                            DeclaringClass = owner,
                            InheritedProperty = inherited,
                            Getter = getter
                        };
                    }
                }
            }
        }

        /// <summary>
        /// An interface METHOD that <see cref="Class"/> must implement but does not declare: the
        /// method that fills it, <see cref="Method"/>, is inherited from
        /// <see cref="DeclaringClass"/>.
        /// </summary>
        public sealed class InheritedMethod
        {
            public IRClass Class { get; init; }
            public IRInterface Interface { get; init; }
            public IRInterfaceMethod InterfaceMethod { get; init; }
            public IRClass DeclaringClass { get; init; }
            public IRMethod Method { get; init; }
        }

        /// <summary>
        /// The method twin of <see cref="InheritedInterfaceAccessors"/>: every interface method
        /// <paramref name="irClass"/> takes on through its OWN <c>Implements</c> list but leaves
        /// to an INHERITED method — <c>Class Rect : Inherits BaseShape : Implements IShape</c>
        /// where only <c>BaseShape</c> declares <c>Area</c> (#131).
        ///
        /// <para>⛔ THE SAME TWO FAILURES AS THE ACCESSORS, for the same reason. On C++
        /// <c>BaseShape::Area</c> and <c>IShape::Area</c> sit in unrelated bases, so <c>Rect</c>
        /// stays abstract (and a call through <c>Rect</c> is ambiguous between the two); on MSIL
        /// <c>BaseShape::Area</c> is not virtual and <c>Rect</c> fails to load with
        /// TypeLoadException. C# and JavaScript need nothing: an inherited member satisfying an
        /// interface is their own semantics.</para>
        ///
        /// <para>One entry per interface SLOT: two interfaces that both declare <c>Name()</c> are
        /// two entries naming the same inherited method. MSIL needs a stub per slot; C++ needs
        /// one forwarder per C++ signature, since one C++ function overrides every same-signature
        /// pure virtual in every base.</para>
        ///
        /// <para>Unlike a property, a method is matched by SIGNATURE as well as name
        /// (case-insensitively), because a name can be shared: a class may declare its own
        /// <c>Area()</c> and still inherit the <c>Area(scale)</c> its interface asks for, so a
        /// test by name alone would leave that slot empty. A method the class declares itself
        /// with the slot's signature fills it and is never listed. The NEAREST base declaring a
        /// matching method wins, even past a nearer base whose same-named method has other
        /// parameters; a Shared one yields nothing (the class stays as broken as it was).</para>
        /// </summary>
        public static IEnumerable<InheritedMethod> InheritedInterfaceMethods(IRModule module, IRClass irClass)
        {
            if (irClass == null) yield break;

            foreach (var iface in ImplementedInterfaces(module, irClass))
            {
                foreach (var im in iface.Methods ?? new List<IRInterfaceMethod>())
                {
                    if (im?.Name == null) continue;
                    if (irClass.Methods.Any(m => FillsSlot(m, im))) continue;

                    var (owner, inherited) = FindInheritedMethod(module, irClass, im);
                    if (inherited == null || inherited.IsStatic) continue;

                    yield return new InheritedMethod
                    {
                        Class = irClass,
                        Interface = iface,
                        InterfaceMethod = im,
                        DeclaringClass = owner,
                        Method = inherited
                    };
                }
            }
        }

        /// <summary>
        /// True when <paramref name="method"/> has <paramref name="slot"/>'s name
        /// (case-insensitively) and signature: the same number of parameters, each of the same
        /// type and passing (ByRef or not), and the same return type. Optional and ParamArray are
        /// not part of a signature. A generic method never matches: the IR carries no type
        /// parameters for an interface method, so its signature could not be compared.
        /// </summary>
        private static bool FillsSlot(IRMethod method, IRInterfaceMethod slot)
        {
            if (method?.Name == null || !string.Equals(method.Name, slot.Name, StringComparison.OrdinalIgnoreCase))
                return false;
            if (method.GenericParameters != null && method.GenericParameters.Count > 0) return false;

            var parameters = method.Implementation?.Parameters ?? new List<IRVariable>();
            var slotParameters = slot.Parameters ?? new List<IRParameter>();
            if (parameters.Count != slotParameters.Count) return false;
            if (!SameReturnType(method.ReturnType, slot.ReturnType)) return false;

            for (var i = 0; i < parameters.Count; i++)
            {
                var declared = slotParameters[i];
                var actual = parameters[i];
                if (declared == null || actual == null || declared.IsByRef != actual.IsByRef) return false;
                var same = declared.Type != null
                    ? SameType(declared.Type, actual.Type)
                    : string.Equals(declared.TypeName, actual.Type?.Name, StringComparison.OrdinalIgnoreCase);
                if (!same) return false;
            }
            return true;
        }

        private static bool SameReturnType(TypeInfo a, TypeInfo b)
        {
            static bool IsVoid(TypeInfo t) =>
                t == null || t.Kind == TypeKind.Void || string.Equals(t.Name, "Void", StringComparison.OrdinalIgnoreCase);
            return IsVoid(a) || IsVoid(b) ? IsVoid(a) && IsVoid(b) : SameType(a, b);
        }

        private static bool SameType(TypeInfo a, TypeInfo b)
        {
            if (a == null || b == null) return a == null && b == null;
            if (!string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase)) return false;
            if (a.ArrayRank != b.ArrayRank || a.IsPointer != b.IsPointer) return false;
            if ((a.ElementType != null || b.ElementType != null) && !SameType(a.ElementType, b.ElementType)) return false;

            var aArgs = a.GenericArguments ?? new List<TypeInfo>();
            var bArgs = b.GenericArguments ?? new List<TypeInfo>();
            if (aArgs.Count != bArgs.Count) return false;
            for (var i = 0; i < aArgs.Count; i++)
                if (!SameType(aArgs[i], bArgs[i])) return false;
            return true;
        }

        private static (IRClass Owner, IRMethod Method) FindInheritedMethod(IRModule module, IRClass irClass, IRInterfaceMethod slot)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { irClass.Name ?? "" };
            for (var current = FindClass(module, irClass.BaseClass); current != null; current = FindClass(module, current.BaseClass))
            {
                if (!seen.Add(current.Name ?? "")) break;
                var method = current.Methods.FirstOrDefault(m => FillsSlot(m, slot));
                if (method != null) return (current, method);
            }
            return (null, null);
        }

        /// <summary>
        /// Whether a backend emits this accessor for <paramref name="prop"/> — the rule both
        /// backends' property emitters already apply: an auto property has both unless
        /// ReadOnly/WriteOnly removes one; an explicit one has what it declares.
        /// </summary>
        public static bool HasAccessor(IRProperty prop, bool getter)
        {
            var isAuto = prop.Getter == null && prop.Setter == null;
            return getter
                ? !prop.IsWriteOnly && (prop.Getter != null || isAuto)
                : !prop.IsReadOnly && (prop.Setter != null || isAuto);
        }

        private static (IRClass Owner, IRProperty Property) FindInheritedProperty(IRModule module, IRClass irClass, string name)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { irClass.Name ?? "" };
            for (var current = FindClass(module, irClass.BaseClass); current != null; current = FindClass(module, current.BaseClass))
            {
                if (!seen.Add(current.Name ?? "")) break;
                var prop = current.Properties.FirstOrDefault(p =>
                    string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
                if (prop != null) return (current, prop);
            }
            return (null, null);
        }

        private static IRClass FindClass(IRModule module, string name)
        {
            if (string.IsNullOrEmpty(name) || module?.Classes == null) return null;
            return module.Classes.Values.FirstOrDefault(c =>
                string.Equals(c?.Name, name, StringComparison.OrdinalIgnoreCase));
        }
    }
}
