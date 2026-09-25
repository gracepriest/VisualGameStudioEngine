namespace BasicLang.Forms;

/// <summary>
/// The ONE answer to "what does a WinForms accelerator-marked caption display as, and which
/// character is underlined" — shared by the web emitter (<see cref="FormAssetEmitter"/>), the
/// designer canvas (<c>FormCanvasControl</c>, which draws the display text and underlines the
/// mnemonic), the canvas LAYOUT (<c>FormCanvasTransform</c>, which sizes a cell from the display
/// text) and the item-id derivation (<c>FormPlacement.ItemId</c>).
///
/// <para>⛔⛔ Before this type there were three private copies of the stripping rule (two regexes
/// and a canvas that applied none), so "what does &amp;Open look like" had different answers on the
/// page and in the designer. Never spell the rule again beside a caller — call this.</para>
///
/// <para>The WinForms rule: <c>&amp;x</c> → <c>x</c> with <c>x</c> underlined; <c>&amp;&amp;</c> → a
/// literal <c>&amp;</c> with no underline; a trailing lone <c>&amp;</c> displays nothing; a caption
/// with no <c>&amp;</c> passes through unchanged. Only the FIRST mnemonic is underlined — it is the
/// one the running control binds to Alt+key.</para>
/// </summary>
public static class FormAccelerator
{
    /// <summary>
    /// Returns the text as it should be DISPLAYED (accelerator marks resolved) and the zero-based
    /// index, into that display text, of the character that should be drawn underlined — or -1 when
    /// no character is underlined.
    ///
    /// <para>⛔ The display text is byte-identical to the regex the web emitter used to apply
    /// (<c>Regex.Replace(text, "&amp;(&amp;?)", "$1")</c>): scanning left to right, each <c>&amp;</c>
    /// consumes one following <c>&amp;</c> if there is one, and is otherwise removed. Changing that
    /// changes every emitted page's caption.</para>
    /// </summary>
    public static (string Display, int UnderlineIndex) Display(string text)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf('&') < 0)
        {
            return (text ?? "", -1);
        }

        var sb = new System.Text.StringBuilder(text.Length);
        var underline = -1;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c != '&')
            {
                sb.Append(c);
                continue;
            }

            if (i + 1 < text.Length && text[i + 1] == '&')
            {
                // "&&" — a literal ampersand, never a mnemonic.
                sb.Append('&');
                i++;
                continue;
            }

            // A lone '&': dropped. The character after it (if any) is the mnemonic; it is appended
            // on the next iteration, at the index the builder is at now.
            if (underline < 0 && i + 1 < text.Length)
            {
                underline = sb.Length;
            }
        }

        return (sb.ToString(), underline);
    }
}
