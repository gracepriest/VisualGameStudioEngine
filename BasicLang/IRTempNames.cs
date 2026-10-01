using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace BasicLang.Compiler.IR
{
    /// <summary>
    /// Compiler temps are named <c>t0, t1, ...</c> — by <see cref="IRFunction.GetNextTempName"/>
    /// in the IR, and again by a backend's own counter when it emits them. A program is free to
    /// name its own variables that way too, and nothing used to keep the two apart: one name then
    /// meant two unrelated values, and the optimizer and backends silently confused them (see
    /// <c>IRBuilder.SeparateTempsFromUserNames</c> for the measurements).
    ///
    /// <para>This is the single source of "which temp-shaped names does the user own", shared by
    /// the IRBuilder (which renames colliding IR temps) and <see cref="CodeGen.CodeGeneratorBase"/>
    /// (whose temp counter skips them), so the two cannot drift about what counts.</para>
    /// </summary>
    public static class IRTempNames
    {
        private static readonly Regex TempShaped =
            new(@"^t\d+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>True for a name a temp generator could also produce. Case-insensitive,
        /// because BasicLang is, and a backend may lower-case.</summary>
        public static bool IsTempShaped(string name) => name != null && TempShaped.IsMatch(name);

        /// <summary>Every function body in the module, class members' bodies included.</summary>
        public static List<IRFunction> AllFunctions(IRModule module)
        {
            var functions = new List<IRFunction>(module.Functions);
            foreach (var cls in module.Classes.Values)
            {
                functions.AddRange(cls.Methods.Select(m => m.Implementation));
                functions.AddRange(cls.Constructors.Select(c => c.Implementation));
                functions.AddRange(cls.Properties.SelectMany(p => new[] { p.Getter, p.Setter }));
            }
            return functions.Where(f => f != null).Distinct().ToList();
        }

        /// <summary>
        /// The temp-shaped names the program itself declares: globals, class fields, properties
        /// and methods, functions, parameters and locals — and every name a function RESERVES
        /// (<see cref="IRFunction.ReservedNames"/>, ADR-0018 D1): a For Each, Catch, pattern,
        /// LINQ range or lambda-captured variable no <see cref="IRFunction.LocalVariables"/> lists.
        /// Empty for almost every program, and every consumer treats empty as "change nothing",
        /// so output is byte-identical then.
        ///
        /// <para>⭐ The ONE reader that filters the reservation by shape, and it reads the UNION
        /// (reserved ∪ locals ∪ parameters ∪ module-level names), so a declaration that bypassed
        /// reservation still cannot collide — the verifier names the gap instead. Its two
        /// consumers are <c>IRBuilder.SeparateTempsFromUserNames</c> and every backend's temp
        /// counter (<see cref="CodeGen.CodeGeneratorBase"/>); ClosureLowering's is a third.</para>
        /// </summary>
        public static HashSet<string> UserOwned(IRModule module)
        {
            var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (module == null) return reserved;

            void Reserve(string name)
            {
                if (IsTempShaped(name)) reserved.Add(name);
            }

            foreach (var name in ModuleLevelNames(module)) Reserve(name);
            foreach (var fn in AllFunctions(module))
            {
                foreach (var p in fn.Parameters) Reserve(p.Name);
                foreach (var l in fn.LocalVariables) Reserve(l.Name);
                foreach (var r in fn.ReservedNames) Reserve(r);
            }
            return reserved;
        }

        /// <summary>
        /// ⭐ ADR-0018 E3: the program's MODULE-level names, whatever their shape — every global
        /// (its key and its name), every class field, property and method, and every function's
        /// name (class member bodies and lambdas included). The one list both
        /// <see cref="UserOwned"/> (filtered by shape) and <see cref="PublishModuleNames"/>
        /// (unfiltered) read, so the two cannot disagree about what "module-level" means.
        /// </summary>
        public static IEnumerable<string> ModuleLevelNames(IRModule module)
        {
            if (module == null) yield break;
            foreach (var global in module.GlobalVariables) { yield return global.Key; yield return global.Value?.Name; }
            foreach (var cls in module.Classes.Values)
            {
                foreach (var f in cls.Fields) yield return f.Name;
                foreach (var p in cls.Properties) yield return p.Name;
                foreach (var m in cls.Methods) yield return m.Name;
            }
            foreach (var fn in AllFunctions(module)) yield return fn.Name;
        }

        /// <summary>
        /// ⭐ ADR-0018 E3: publishes <see cref="ModuleLevelNames"/> as ONE set shared by every
        /// function of <paramref name="module"/> (<see cref="IRFunction.ModuleReservedNames"/>), so
        /// <see cref="IRFunction.GetNextTempName"/> and <see cref="IRFunction.DeclareTemp"/> never
        /// mint a module-level name and the verifier's D4 refusal covers them. Called where a
        /// module's set of functions becomes final: the end of <c>IRBuilder.Build</c>, and
        /// <c>CombineIRModules</c> for a multi-file build (a unit cannot see another unit's names,
        /// and the optimizer runs on the combined module). Byte-neutral: the renamer's union
        /// (<see cref="UserOwned"/>) already holds every temp-shaped module-level name.
        /// </summary>
        public static void PublishModuleNames(IRModule module)
        {
            if (module == null) return;
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in ModuleLevelNames(module))
                if (!string.IsNullOrEmpty(name)) names.Add(name);
            foreach (var fn in AllFunctions(module)) fn.ModuleReservedNames = names;
        }
    }
}
