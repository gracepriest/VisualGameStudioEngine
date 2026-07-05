namespace VisualGameStudio.Editor.Completion;

/// <summary>
/// Pure helpers for rendering signature help (active-parameter highlighting).
/// </summary>
public static class SignatureHelpFormatter
{
    /// <summary>
    /// Finds the character range of the active parameter's label inside the
    /// signature label so it can be bolded inline. Parameters are located
    /// left-to-right starting after the opening parenthesis, so identical
    /// labels resolve in declaration order. Returns null when the range
    /// cannot be determined (out-of-range index, label not present).
    /// </summary>
    public static (int Start, int Length)? GetActiveParameterRange(
        string signatureLabel,
        IReadOnlyList<string>? parameterLabels,
        int activeParameter)
    {
        if (string.IsNullOrEmpty(signatureLabel) || parameterLabels == null) return null;
        if (activeParameter < 0 || activeParameter >= parameterLabels.Count) return null;

        var searchFrom = signatureLabel.IndexOf('(');
        if (searchFrom < 0) searchFrom = 0;

        for (var i = 0; i <= activeParameter; i++)
        {
            var label = parameterLabels[i];
            if (string.IsNullOrEmpty(label)) return null;

            var index = signatureLabel.IndexOf(label, searchFrom, StringComparison.Ordinal);
            if (index < 0) return null;

            if (i == activeParameter)
            {
                return (index, label.Length);
            }

            searchFrom = index + label.Length;
        }

        return null;
    }
}

/// <summary>
/// Editor-level model of an LSP signature help result, carrying everything
/// the popup needs to bold the active parameter inline and cycle overloads.
/// </summary>
public class SignatureHelpDisplayData
{
    public List<SignatureDisplayInfo> Signatures { get; } = new();
    public int ActiveSignature { get; set; }
    public int ActiveParameter { get; set; }
}

/// <summary>A single overload within <see cref="SignatureHelpDisplayData"/>.</summary>
public class SignatureDisplayInfo
{
    public string Label { get; set; } = "";
    public string? Documentation { get; set; }
    public List<string> ParameterLabels { get; } = new();
    public List<string?> ParameterDocumentation { get; } = new();
}
