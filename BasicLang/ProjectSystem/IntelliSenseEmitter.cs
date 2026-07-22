using System;

namespace BasicLang.Compiler.ProjectSystem
{
    /// <summary>
    /// Produces the two artifacts clangd needs for a native project — the generated
    /// <c>obj/gen</c> headers (so a user's <c>#include "Logic.g.h"</c> resolves) and
    /// <c>obj/compile_commands.json</c> — WITHOUT running a build and WITHOUT requiring an
    /// installed C++ toolchain. That is what makes IntelliSense work on a fresh checkout,
    /// before anything has ever been compiled.
    ///
    /// This is a thin seam, not a second implementation: it drives the very same
    /// <see cref="CppProjectBuilder.EmitCore"/> that <see cref="CppProjectBuilder.Build"/>
    /// drives, so the flags the editor sees and the flags the build uses cannot diverge.
    /// Only the BUILD-specific gates are bypassed (no-sources, entry point, no-toolchain,
    /// native libs / engine auto-link) — none of them affects a compile flag. See EmitCore's
    /// remarks for the per-gate rationale.
    ///
    /// Failure semantics: regen-on-success-only. A transpile failure returns before obj/gen
    /// is cleaned, so the last good headers survive and clangd keeps serving them while the
    /// user's edit is mid-flight.
    /// </summary>
    public static class IntelliSenseEmitter
    {
        /// <summary>
        /// Emits obj/gen + obj/compile_commands.json for <paramref name="project"/>.
        /// </summary>
        /// <param name="toolchain">
        /// The installed toolchain whose driver and flag style to emit, or null to emit for
        /// the blessed default (clang++, GNU flags). Callers that have probed should pass
        /// what they found so the database matches what a real build would produce; callers
        /// that have not may pass null. For a project that pins &lt;CppToolchain&gt;, the pin
        /// overrides this parameter — EmitCore resolves it by id via <paramref name="resolveById"/>
        /// (a cheap probe by default); otherwise discovery stays the caller's choice, and a
        /// null here is a supported state, not an error.
        /// </param>
        /// <param name="resolveById">
        /// How EmitCore resolves a project's pinned &lt;CppToolchain&gt; id to a
        /// <see cref="CppToolchain"/>. Defaults to <see cref="CppToolchain.TryFindById"/> (a PATH
        /// probe) when null. A caller that knows about a settings-configured, possibly off-PATH
        /// compiler override should pass a resolver that honors it — otherwise a pin to an
        /// off-PATH toolchain resolves to null here and the compile database silently falls back
        /// to the clang++ identity, drifting from what a real build (which DOES honor the
        /// override) would use.
        /// </param>
        /// <returns>
        /// <see cref="CppProjectBuildResult.Success"/> is true when both artifacts were
        /// written; on false the diagnostics carry why (a transpile or codegen error, or an
        /// IO failure). No compile, link, or deploy is ever attempted.
        /// </returns>
        public static CppProjectBuildResult Emit(
            ProjectFile project, string configuration, CppToolchain toolchain = null,
            Func<string, CppToolchain> resolveById = null)
        {
            var result = new CppProjectBuildResult();
            var outcome = CppProjectBuilder.EmitCore(
                project, configuration, result, () => toolchain, forIntelliSense: true, resolveById);
            // EmitCore stops at the compile-database write, so Completed IS success here —
            // unlike Build, where it only means "ready to compile".
            result.Success = outcome.Completed;
            return result;
        }
    }
}
