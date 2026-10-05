using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace BasicLang.Compiler.SemanticAnalysis
{
    /// <summary>
    /// ⭐ Which of BasicLang's built-in functions VB also declares in a <c>Microsoft.VisualBasic</c>
    /// module — and so may be written QUALIFIED, as VB allows: <c>Strings.Left(s, 2)</c>,
    /// <c>Microsoft.VisualBasic.Left(s, 2)</c> or <c>Microsoft.VisualBasic.Strings.Left(s, 2)</c>.
    ///
    /// <para>⛔ WHY (portable-controls Task 7c review). Inside a class with a member named <c>Left</c>
    /// — every Form and Control has <c>Left</c>, <c>Top</c>, <c>Width</c>, <c>Text</c> — a bare
    /// <c>Left("hello", 2)</c> names the MEMBER (VB's own BC30471, the classic Form.Left gotcha). VB's
    /// way out is to qualify the built-in; BasicLang had no qualified spelling at all, so the built-in
    /// was unreachable from a form.</para>
    ///
    /// <para>⚠ ENUMERATED, never hand-picked: an intrinsic qualifies when one of VB's modules declares a
    /// public static method of that name whose SHAPE matches BasicLang's own signature — the same
    /// leading parameter types (VB's trailing optionals may follow) and the same return type, either
    /// VB side's <c>Object</c> matching anything (see <see cref="Compatible"/>). So <c>Strings.Left</c>
    /// qualifies, while <c>DateAdd</c> (VB takes the interval first, BasicLang the date) does not; and
    /// <c>Shell</c>, whose shape matches but whose MEANING does not, is excluded by name with the reason
    /// (<see cref="DifferentMeaning"/>): a qualified spelling that ran BasicLang's function under VB's
    /// name would be a lie. <c>CStr</c> and the other conversions are VB OPERATORS,
    /// not module functions, so <c>Microsoft.VisualBasic.CStr</c> is refused here as in VB.</para>
    /// </summary>
    internal static class VbIntrinsicQualifiers
    {
        /// <summary>VB's standard modules, in the order a name is looked for.</summary>
        private static readonly Type[] Modules =
        {
            typeof(Microsoft.VisualBasic.Strings),
            typeof(Microsoft.VisualBasic.Conversion),
            typeof(Microsoft.VisualBasic.Interaction),
            typeof(Microsoft.VisualBasic.Information),
            typeof(Microsoft.VisualBasic.DateAndTime),
            typeof(Microsoft.VisualBasic.VBMath),
            typeof(Microsoft.VisualBasic.FileSystem),
            typeof(Microsoft.VisualBasic.Financial),
        };

        private const string Namespace = "Microsoft.VisualBasic";

        private static readonly ConcurrentDictionary<string, string> Cache = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The VB module that declares <paramref name="intrinsic"/> with BasicLang's shape
        /// (<c>"Strings"</c>, <c>"Conversion"</c>, …), or null when none does.
        /// </summary>
        public static string ModuleOf(Symbol intrinsic)
        {
            if (intrinsic == null || string.IsNullOrEmpty(intrinsic.Name)) return null;
            var key = intrinsic.Name + "(" + string.Join(",", intrinsic.Parameters.Select(p => p.Type?.Name)) + ")"
                      + (intrinsic.ReturnType?.Name ?? "");
            var module = Cache.GetOrAdd(key, _ => FindModule(intrinsic) ?? "");
            return module.Length == 0 ? null : module;
        }

        /// <summary>Whether <paramref name="qualifier"/> (the dotted text before the function name) is a
        /// spelling VB accepts for a function of <paramref name="module"/>.</summary>
        public static bool IsQualifierFor(string qualifier, string module) =>
            !string.IsNullOrEmpty(qualifier) && !string.IsNullOrEmpty(module)
            && (string.Equals(qualifier, module, StringComparison.OrdinalIgnoreCase)
                || string.Equals(qualifier, Namespace, StringComparison.OrdinalIgnoreCase)
                || string.Equals(qualifier, Namespace + "." + module, StringComparison.OrdinalIgnoreCase));

        /// <summary>Whether <paramref name="qualifier"/> could be one of these spellings for SOME module — the
        /// first word of it is all a user declaration could collide with.</summary>
        public static bool LooksLikeAQualifier(string qualifier) =>
            !string.IsNullOrEmpty(qualifier)
            && (string.Equals(qualifier, Namespace, StringComparison.OrdinalIgnoreCase)
                || Modules.Any(m => string.Equals(qualifier, m.Name, StringComparison.OrdinalIgnoreCase)
                                    || string.Equals(qualifier, Namespace + "." + m.Name, StringComparison.OrdinalIgnoreCase)));

        private static string FindModule(Symbol intrinsic)
        {
            if (DifferentMeaning.ContainsKey(intrinsic.Name)) return null;
            foreach (var module in Modules)
            {
                foreach (var method in module.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (method.IsSpecialName
                        || !string.Equals(method.Name, intrinsic.Name, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (ShapeMatches(intrinsic, method)) return module.Name;
                }
            }
            return null;
        }

        private static bool ShapeMatches(Symbol intrinsic, MethodInfo method)
        {
            var vbParameters = method.GetParameters();
            var count = intrinsic.Parameters.Count;
            var required = vbParameters.Count(p => !p.IsOptional
                                                   && !p.IsDefined(typeof(ParamArrayAttribute), false));
            if (count < required || count > vbParameters.Length) return false;

            for (var i = 0; i < count; i++)
            {
                var vbType = vbParameters[i].ParameterType;
                if (vbType.IsByRef) vbType = vbType.GetElementType();
                if (!Compatible(intrinsic.Parameters[i].Type, vbType)) return false;
            }
            return Compatible(intrinsic.ReturnType, method.ReturnType);
        }

        /// <summary>
        /// A BasicLang type against VB's. VB's <c>Object</c> takes anything. BasicLang's <c>Object</c> is NOT a
        /// wildcard the other way — it matches VB's Object, or VB's <c>System.Array</c> (BasicLang types
        /// <c>UBound(array)</c>'s parameter Object, having no Array type) and nothing else: as a wildcard it
        /// matched <c>FileSystem.Print(FileNumber As Integer, …)</c> to BasicLang's console <c>Print(value)</c>.
        /// </summary>
        private static bool Compatible(TypeInfo basicLang, Type vb)
        {
            if (vb == typeof(object)) return true;
            var mapped = ClrTypeOf(basicLang);
            if (mapped == typeof(object)) return vb == typeof(Array);
            return mapped != null && mapped == vb;
        }

        /// <summary>
        /// Built-ins whose SHAPE matches VB's function of the same name but whose MEANING does not — so the
        /// qualified spelling would run BasicLang's behaviour under VB's name. Each entry says why.
        /// </summary>
        private static readonly Dictionary<string, string> DifferentMeaning = new(StringComparer.OrdinalIgnoreCase)
        {
            // BasicLang's Shell waits for the command and returns its EXIT CODE; VB's starts it and returns
            // the PROCESS ID at once.
            ["Shell"] = "BasicLang returns the exit code after waiting; VB returns the process id immediately",
        };

        private static readonly Dictionary<string, Type> ClrTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            ["String"] = typeof(string), ["Integer"] = typeof(int), ["Long"] = typeof(long),
            ["Short"] = typeof(short), ["Byte"] = typeof(byte), ["Double"] = typeof(double),
            ["Single"] = typeof(float), ["Decimal"] = typeof(decimal), ["Boolean"] = typeof(bool),
            ["Char"] = typeof(char), ["Date"] = typeof(DateTime), ["DateTime"] = typeof(DateTime),
            ["Object"] = typeof(object), ["Void"] = typeof(void),
        };

        private static Type ClrTypeOf(TypeInfo type) =>
            type?.Name != null && type.ArrayRank == 0 && ClrTypes.TryGetValue(type.Name, out var clr) ? clr : null;
    }
}
