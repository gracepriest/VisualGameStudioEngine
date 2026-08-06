using System;
using System.Collections.Generic;
using System.Linq;

namespace BasicLang.Compiler.StdLib
{
    /// <summary>
    /// The standard library for the JavaScript backend.
    ///
    /// <para><b>This is an ALLOW-LIST, and the omissions are the design.</b> The other
    /// providers answer for nearly every category because they target a host with a
    /// filesystem, processes and blocking I/O. A browser has none of those, and the backend's
    /// governing rule is that a feature either lowers cleanly or is refused — so
    /// <c>CanHandle</c> returning false is a FEATURE here, surfacing an unsupported function
    /// as a compile-time error instead of emitting something that misbehaves at runtime.</para>
    ///
    /// <para>Refused outright: Networking (see <see cref="Networking"/> below), FileIO,
    /// Process, Environment, Crypto, and the cursor/colour half of Console.</para>
    ///
    /// <para><b>What is NOT here, deliberately:</b> String and Conversion. The generator
    /// already lowers those inline, and it has to — <c>Len(s)</c> becomes the MEMBER
    /// expression <c>s.length</c>, not a renamed callee, so it cannot be expressed as a
    /// simple call rewrite. Adding a second implementation here would create exactly the
    /// two-implementations-one-wired split that has bitten this repo before.</para>
    /// </summary>
    public class JavaScriptStdLibProvider : IStdLibProvider
    {
        /// <summary>
        /// Why every networking function is refused rather than emitted.
        ///
        /// <para><c>HttpGet(url) As String</c> is a SYNCHRONOUS signature. The only browser
        /// primitive is <c>fetch</c>, which is asynchronous, so any emission would hand back a
        /// Promise where a String is expected. That does not throw — it stringifies to
        /// "[object Promise]" and flows onward as a plausible-looking wrong value. TCP and UDP
        /// have no browser primitive at all.</para>
        /// </summary>
        private static readonly string Networking = "unavailable on a web target";

        /// <summary>
        /// name → a formatter over the rendered argument expressions.
        ///
        /// <para>Formatters rather than a name map because several of these are not renames:
        /// a 1-based month, a global regex flag and banker's rounding all need real shape.</para>
        /// </summary>
        private static readonly Dictionary<string, Func<string[], string>> _emitters =
            new Dictionary<string, Func<string[], string>>(StringComparer.OrdinalIgnoreCase)
            {
                // ---------------------------------------------------------------- I/O
                ["Print"] = a => $"process.stdout.write(String({Arg(a, 0)}))",
                ["PrintLine"] = a => $"console.log({Arg(a, 0)})",

                // ---------------------------------------------------------------- Math
                ["Abs"] = a => $"Math.abs({Arg(a, 0)})",
                ["Sqr"] = a => $"Math.sqrt({Arg(a, 0)})",
                ["Sqrt"] = a => $"Math.sqrt({Arg(a, 0)})",
                ["Sin"] = a => $"Math.sin({Arg(a, 0)})",
                ["Cos"] = a => $"Math.cos({Arg(a, 0)})",
                ["Tan"] = a => $"Math.tan({Arg(a, 0)})",
                ["Atn"] = a => $"Math.atan({Arg(a, 0)})",
                ["Exp"] = a => $"Math.exp({Arg(a, 0)})",
                ["Log"] = a => $"Math.log({Arg(a, 0)})",
                ["Sgn"] = a => $"Math.sign({Arg(a, 0)})",
                ["Floor"] = a => $"Math.floor({Arg(a, 0)})",
                ["Ceiling"] = a => $"Math.ceil({Arg(a, 0)})",
                ["Pow"] = a => $"Math.pow({Arg(a, 0)}, {Arg(a, 1)})",
                ["Min"] = a => $"Math.min({Arg(a, 0)}, {Arg(a, 1)})",
                ["Max"] = a => $"Math.max({Arg(a, 0)}, {Arg(a, 1)})",

                // ⚠ Int and Fix DIFFER on negatives and are constantly confused: VB's Int
                // FLOORS, so Int(-3.5) is -4, while Fix TRUNCATES toward zero, giving -3.
                // Mapping both to one function is right for every positive input and wrong
                // for every negative one.
                ["Int"] = a => $"Math.floor({Arg(a, 0)})",
                ["Fix"] = a => $"Math.trunc({Arg(a, 0)})",

                ["Round"] = a => BankersRound(Arg(a, 0)),
                ["Rnd"] = a => "Math.random()",

                // Argless Randomize means "reseed from the clock". Math.random is already
                // unpredictable and cannot be seeded, so the reseed is a no-op — and unlike a
                // silent drop this one is honest: there is no observable contract to break.
                // A SEEDED Randomize(n) would be a different matter and is not in the roster.
                ["Randomize"] = a => "void 0",

                // ---------------------------------------------------------------- DateTime
                ["Now"] = a => "new Date()",
                ["Today"] = a => "(() => { const d = new Date(); d.setHours(0, 0, 0, 0); return d; })()",
                ["Year"] = a => $"({Arg(a, 0)}).getFullYear()",

                // ⚠ getMonth() is 0-BASED where VB's Month is 1-based, and getDate() is the
                // day of the MONTH where getDay() is the day of the WEEK. Both mistakes
                // produce a number in a plausible range on every single date.
                ["Month"] = a => $"(({Arg(a, 0)}).getMonth() + 1)",
                ["Day"] = a => $"({Arg(a, 0)}).getDate()",

                ["Hour"] = a => $"({Arg(a, 0)}).getHours()",
                ["Minute"] = a => $"({Arg(a, 0)}).getMinutes()",
                ["Second"] = a => $"({Arg(a, 0)}).getSeconds()",

                // ---------------------------------------------------------------- Regex
                ["IsMatch"] = a => $"new RegExp({Arg(a, 1)}).test({Arg(a, 0)})",

                // .NET's Match.Value is "" when nothing matched; JS String.match answers null,
                // which would print "null" and compare unequal to "".
                ["RegexMatch"] = a =>
                    $"((m) => m === null ? \"\" : m[0])(String({Arg(a, 0)}).match(new RegExp({Arg(a, 1)})))",
                ["RegexMatches"] = a =>
                    $"(String({Arg(a, 0)}).match(new RegExp({Arg(a, 1)}, \"g\")) || [])",

                // ⚠ The "g" flag is load-bearing. .NET's Regex.Replace replaces EVERY match;
                // a JS RegExp without it replaces only the first, which is indistinguishable
                // from correct on any single-match input.
                ["RegexReplace"] = a =>
                    $"String({Arg(a, 0)}).replace(new RegExp({Arg(a, 1)}, \"g\"), {Arg(a, 2)})",
                ["RegexSplit"] = a => $"String({Arg(a, 0)}).split(new RegExp({Arg(a, 1)}))",

                // ---------------------------------------------------------------- JSON
                // The one category a browser does BETTER than the host runtime.
                ["JsonParse"] = a => $"JSON.parse({Arg(a, 0)})",
                ["JsonStringify"] = a => $"JSON.stringify({Arg(a, 0)})",
                ["JsonIsValid"] = a =>
                    $"((s) => {{ try {{ JSON.parse(s); return true; }} catch {{ return false; }} }})({Arg(a, 0)})",
            };

        /// <summary>
        /// .NET rounds HALF TO EVEN — Math.Round(2.5) is 2, not 3 — where JavaScript's
        /// Math.round rounds half UP. So a bare rename disagrees on every exact .5, and
        /// agrees everywhere else, which is what makes it survive casual testing.
        ///
        /// <para>Wrapped in an arrow IIFE because the value is read three times; inlining the
        /// expression would evaluate the argument three times and call <c>Round(Next())</c>
        /// three times over.</para>
        /// </summary>
        private static string BankersRound(string value) =>
            $"((v) => Math.abs(v % 1) === 0.5 ? 2 * Math.round(v / 2) : Math.round(v))({value})";

        /// <summary>Missing arguments become <c>undefined</c> rather than throwing here —
        /// arity is the front end's job, and this must not crash the generator.</summary>
        private static string Arg(string[] args, int index) =>
            args != null && index < args.Length ? args[index] : "undefined";

        public bool CanHandle(string functionName) =>
            !string.IsNullOrEmpty(functionName) && _emitters.ContainsKey(functionName);

        public string EmitCall(string functionName, string[] arguments) =>
            functionName != null && _emitters.TryGetValue(functionName, out var emit)
                ? emit(arguments ?? Array.Empty<string>())
                : null;

        /// <summary>Nothing to import: every one of these is a JavaScript global.</summary>
        public IEnumerable<string> GetRequiredImports(string functionName) =>
            Enumerable.Empty<string>();

        /// <summary>
        /// No prelude. Each emission is a self-contained expression, which keeps the output
        /// readable in devtools — half of why source maps are on the plan at all.
        /// </summary>
        public string GetInlineImplementation(string functionName) => null;
    }
}
