namespace VisualGameStudio.Core.Abstractions.Services;

/// <summary>
/// Provides code metrics analysis for source files.
/// </summary>
public interface ICodeMetricsService
{
    /// <summary>
    /// Calculates metrics for a source file.
    /// </summary>
    /// <param name="sourceCode">The source code to analyze.</param>
    /// <returns>The calculated metrics.</returns>
    CodeMetrics CalculateMetrics(string sourceCode);

    /// <summary>
    /// Calculates metrics for a specific method or function.
    /// </summary>
    /// <param name="methodCode">The method/function code to analyze.</param>
    /// <returns>The calculated method metrics.</returns>
    MethodMetrics CalculateMethodMetrics(string methodCode);
}

/// <summary>
/// Represents code metrics for a source file.
/// </summary>
public class CodeMetrics
{
    /// <summary>
    /// Gets or sets the total number of lines in the file.
    /// </summary>
    public int TotalLines { get; set; }

    /// <summary>
    /// Gets or sets the number of lines containing code.
    /// </summary>
    public int CodeLines { get; set; }

    /// <summary>
    /// Gets or sets the number of comment lines.
    /// </summary>
    public int CommentLines { get; set; }

    /// <summary>
    /// Gets or sets the number of blank lines.
    /// </summary>
    public int BlankLines { get; set; }

    /// <summary>
    /// Gets or sets the number of classes/modules defined.
    /// </summary>
    public int ClassCount { get; set; }

    /// <summary>
    /// Gets or sets the number of functions/subs defined.
    /// </summary>
    public int MethodCount { get; set; }

    /// <summary>
    /// Gets or sets the average cyclomatic complexity.
    /// </summary>
    public double AverageCyclomaticComplexity { get; set; }

    /// <summary>
    /// Gets or sets the maximum nesting depth.
    /// </summary>
    public int MaxNestingDepth { get; set; }

    /// <summary>
    /// Gets a maintainability index (0-100, higher is better).
    /// </summary>
    public int MaintainabilityIndex { get; set; }
}

/// <summary>
/// Represents metrics for a single method or function.
/// </summary>
public class MethodMetrics
{
    /// <summary>
    /// Gets or sets the method name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Gets or sets the line number where the method starts.
    /// </summary>
    public int StartLine { get; set; }

    /// <summary>
    /// Gets or sets the number of lines in the method.
    /// </summary>
    public int LineCount { get; set; }

    /// <summary>
    /// Gets or sets the number of parameters.
    /// </summary>
    public int ParameterCount { get; set; }

    /// <summary>
    /// Gets or sets the cyclomatic complexity.
    /// </summary>
    public int CyclomaticComplexity { get; set; }

    /// <summary>
    /// Gets or sets the maximum nesting depth.
    /// </summary>
    public int NestingDepth { get; set; }

    /// <summary>
    /// Gets or sets the number of local variables.
    /// </summary>
    public int LocalVariableCount { get; set; }

    /// <summary>
    /// Gets a complexity rating based on cyclomatic complexity.
    /// </summary>
    public ComplexityRating Rating =>
        CyclomaticComplexity <= 5 ? ComplexityRating.Simple :
        CyclomaticComplexity <= 10 ? ComplexityRating.Moderate :
        CyclomaticComplexity <= 20 ? ComplexityRating.Complex :
        ComplexityRating.VeryComplex;
}

/// <summary>
/// Complexity rating categories.
/// </summary>
public enum ComplexityRating
{
    /// <summary>Simple, easy to understand (CC 1-5).</summary>
    Simple,
    /// <summary>Moderate complexity (CC 6-10).</summary>
    Moderate,
    /// <summary>Complex, may be difficult to maintain (CC 11-20).</summary>
    Complex,
    /// <summary>Very complex, should be refactored (CC 21+).</summary>
    VeryComplex
}
