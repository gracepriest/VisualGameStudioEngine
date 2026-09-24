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
        /// and methods, functions, parameters and locals. Empty for almost every program, and
        /// every consumer treats empty as "change nothing", so output is byte-identical then.
        /// </summary>
        public static HashSet<string> UserOwned(IRModule module)
        {
            var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (module == null) return reserved;

            void Reserve(string name)
            {
                if (IsTempShaped(name)) reserved.Add(name);
            }

            foreach (var global in module.GlobalVariables) { Reserve(global.Key); Reserve(global.Value?.Name); }
            foreach (var cls in module.Classes.Values)
            {
                foreach (var f in cls.Fields) Reserve(f.Name);
                foreach (var p in cls.Properties) Reserve(p.Name);
                foreach (var m in cls.Methods) Reserve(m.Name);
            }
            foreach (var fn in AllFunctions(module))
            {
                Reserve(fn.Name);
                foreach (var p in fn.Parameters) Reserve(p.Name);
                foreach (var l in fn.LocalVariables) Reserve(l.Name);
            }
            return reserved;
        }
    }
}
