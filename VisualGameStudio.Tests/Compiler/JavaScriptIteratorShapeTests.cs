using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// #145, the FAST half (no node, no CLI; the RUN half is <see cref="JavaScriptIteratorExecutionTests"/>): the TEXT a class's Iterator method becomes on JavaScript, through the
/// non-optimizing helper and the standard-pass one the CLI ships. A plain method holding `yield` is a SyntaxError and a `function*` METHOD returns the one-shot generator object, so the
/// shape is `return {[Symbol.iterator]: function* (params) {body}.bind(this, params)}` — and `super` is a SyntaxError inside that `function*`, so `MyBase.M()` is spelled
/// `Object.getPrototypeOf(Class.prototype).M.call(this, ...)`.
/// </summary>
[TestFixture]
public class JavaScriptIteratorShapeTests
{
    private static string[] Compilations(string source)
        => new[] { JsTestSupport.Compile(source), JsTestSupport.CompileOptimized(source) };

    private static string ClassText(string js, string name)
    {
        var start = js.IndexOf("class " + name, System.StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), $"no `class {name}` in:\n{js}");
        var end = js.IndexOf("\nclass ", start + 1, System.StringComparison.Ordinal);
        var next = js.IndexOf("\nfunction ", start + 1, System.StringComparison.Ordinal);
        if (end < 0 || (next >= 0 && next < end)) end = next;
        return end < 0 ? js[start..] : js[start..end];
    }

    /// <summary>
    /// A member iterator is a PLAIN method returning a re-iterable whose generator takes the call's parameters as its own and is bound to `this` AND to those arguments: not a
    /// bare `yield` in the method (SyntaxError), and not a generator method (one-shot; the generator would also close over the parameters instead of copying them).
    /// </summary>
    [Test]
    public void AMemberIterator_IsAPlainMethodReturningAReiterableGeneratorBoundToItsArguments()
    {
        const string source =
            "Class Counter\n" +
            "Public Base As Integer\n" +
            "Iterator Function Range(lo As Integer, hi As Integer) As IEnumerable(Of Integer)\n" +
            "Dim i As Integer = lo\n" +
            "While i <= hi\nYield Base + i\ni = i + 1\nEnd While\n" +
            "End Function\n" +
            "End Class\n" +
            "Sub Main()\nDim c As New Counter()\nFor Each v In c.Range(1, 3)\nConsole.WriteLine(CStr(v))\nNext\nEnd Sub";

        foreach (var js in Compilations(source))
            Assert.Multiple(() =>
            {
                var cls = ClassText(js, "Counter");
                Assert.That(cls, Does.Contain("Range(lo, hi) {"), "a plain method, not `*Range`");
                Assert.That(cls, Does.Contain("[Symbol.iterator]: function* (lo, hi) {"), "a fresh generator per walk, over its own copies of the parameters");
                Assert.That(cls, Does.Contain("}.bind(this, lo, hi)"), "bound to `this` and to the call's arguments");
            });
    }

    /// <summary>
    /// `MyBase.Sounds()` inside an iterator must not be `super.Sounds()`: `super` is a SyntaxError inside the `function*` expression, and the whole file stops loading.
    /// </summary>
    [Test]
    public void AMyBaseCallInsideAnIterator_IsNotSuper()
    {
        const string source =
            "Class Animal\n" +
            "Overridable Iterator Function Sounds() As IEnumerable(Of String)\nYield \"breath\"\nEnd Function\n" +
            "End Class\n" +
            "Class Dog\nInherits Animal\n" +
            "Overrides Iterator Function Sounds() As IEnumerable(Of String)\n" +
            "For Each s In MyBase.Sounds()\nYield s\nNext\nYield \"woof\"\n" +
            "End Function\n" +
            "End Class\n" +
            "Sub Main()\nDim a As Animal = New Dog()\nFor Each s In a.Sounds()\nConsole.WriteLine(s)\nNext\nEnd Sub";

        foreach (var js in Compilations(source))
            Assert.Multiple(() =>
            {
                var dog = ClassText(js, "Dog");
                Assert.That(dog, Does.Not.Contain("super."), "`super` inside a function* expression is a SyntaxError");
                Assert.That(dog, Does.Contain("Object.getPrototypeOf(Dog.prototype).Sounds.call(this"), "the base method, called on `this`");
                Assert.That(dog, Does.Contain("[Symbol.iterator]: function* () {"));
            });
    }
}
