namespace BasicLang.Forms;

/// <summary>
/// The style a form's code-behind is written in — what decides a handler's signature and placement through the ONE
/// rule, <c>FormHandlers.Shape(owner, evt, target, style)</c> (ADR 0021 §4).
///
/// <para>⚠ Created by property-grid slice 5 with its single member: <see cref="Classic"/> — WinForms
/// <c>(sender As Object, e As …)</c> below the init region, the page's <c>(e As DomEvent)</c> / <c>()</c> above it.
/// Piece 2 (the portable control library, its Task 30) EXTENDS this enum with <c>Portable</c> as one more arm of
/// <c>Shape</c> — never a second signature site, and never a second copy of this file.</para>
/// </summary>
public enum FormCodeStyle
{
    Classic
}
