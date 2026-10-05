namespace BasicLang.Compiler.CodeGen.CPlusPlus
{
    /// <summary>
    /// <c>BasicLang::Array&lt;T&gt;</c> — the C++ shape of a BasicLang array: a HANDLE to shared
    /// <c>std::vector</c> storage, so an array has .NET's REFERENCE semantics.
    ///
    /// <para>⛔ A BARE <c>std::vector</c> IS A VALUE, AND THAT WAS THE BUG. Arrays used to lower to
    /// <c>std::vector&lt;T&gt;</c> itself, so every copy duplicated the storage where .NET copies a
    /// reference: <c>Dim b() As Integer = a : b(0) = 99</c> left <c>a(0)</c> unchanged, a Sub that
    /// filled or sorted its array argument changed nothing the caller could see, and
    /// <c>lst.Add(a)</c> stored a snapshot. C#, JavaScript and MSIL all printed the .NET answer;
    /// C++ alone printed the old values, from a clean build. Copying this handle copies the
    /// <c>shared_ptr</c>, so all three alias one array — the same fix the collections got
    /// (<c>std::shared_ptr&lt;BasicLang::List&lt;T&gt;&gt;</c>, see CLAUDE.md).</para>
    ///
    /// <para>⭐ A HANDLE HAS A NULL STATE, as a .NET array reference does (#196, ADR-0011 D3(c)). A
    /// DEFAULT-CONSTRUCTED handle owns NO storage: it is <c>Nothing</c>. That is what
    /// <c>Dim a() As Integer</c>, <c>a = Nothing</c>, an array field never assigned and a jagged
    /// array's rows all hold — every one of them spells the default (<c>{}</c>,
    /// <c>Array&lt;T&gt;{}</c>). It used to allocate an EMPTY array instead, so null and empty
    /// were one value: <c>a Is Nothing</c> on an unsized <c>Dim</c> was False, <c>a.Length</c>
    /// printed 0 where .NET throws, and <c>Is Nothing</c> could only test emptiness. An EMPTY
    /// array (<c>{}</c>, <c>New Integer() {}</c>) converts in from an empty <c>std::vector</c>
    /// and owns storage. Every member that reaches the storage throws .NET's
    /// <c>NullReferenceException</c> on a Nothing handle — a <c>NetException</c> carrying the
    /// .NET chain, so a typed <c>Catch</c> sees it — and <c>is_nothing()</c> is the null test
    /// (<c>CppCodeGenerator.EmitNullTest</c>). <c>==</c> compares the handles, so two Nothing
    /// arrays are the same reference, as <c>Nothing Is Nothing</c> is.</para>
    ///
    /// <para>It keeps the <c>std::vector</c> surface the generator already emits against —
    /// <c>a[i]</c>, <c>size()</c>, range-for, <c>begin()/end()</c> — so array lowering did not
    /// change shape. A <c>std::vector</c> converts IN (allocation, literals, runtime results) and
    /// the handle converts OUT to <c>std::vector&lt;T&gt;&amp;</c> for non-template runtime code.
    /// Only the OUTERMOST rank is a handle: <c>Integer(,)</c> is
    /// <c>Array&lt;std::vector&lt;int32_t&gt;&gt;</c>, one shared object whose rows live inside it.</para>
    ///
    /// <para>Spliced unconditionally in both emission modes (CppCodeGenerator.GenerateHeader and
    /// CppCodeGenerator.Split's EmitRuntimeHeader — keep them in sync), because array types
    /// appear in too many places (temps, return types, runtime results) to detect reliably, and
    /// <c>#ifndef</c>-guarded so a translation unit that sees two runtime headers gets one
    /// definition. AFTER <see cref="CppNetExceptionRuntime"/> in both, whose class it throws.
    /// Include-free: needs &lt;vector&gt;, &lt;memory&gt;, &lt;initializer_list&gt;,
    /// &lt;stdexcept&gt; and &lt;cstdint&gt;, all in both modes' unconditional include sets.</para>
    /// </summary>
    public static class CppArrayRuntime
    {
        public static string Source { get; } = Build();

        private static string Build()
        {
            CppExceptionTypes.TryGetInheritanceChain("NullReferenceException", out var nullReference);

            return @"#ifndef BASICLANG_ARRAY_RUNTIME
#define BASICLANG_ARRAY_RUNTIME
namespace BasicLang {

/* A BasicLang array: a handle to SHARED std::vector storage. Copying it aliases, as a .NET
   array reference does. A default-constructed handle has NO storage: it is Nothing, and every
   member that reaches the storage throws .NET's NullReferenceException. See CppArrayRuntime.cs. */
template <typename T>
class Array {
    std::shared_ptr<std::vector<T>> _p;

    std::vector<T>& storage() const {
        if (!_p)
            throw NetException(""" + nullReference + @""", ""Object reference not set to an instance of an object."");
        return *_p;
    }
public:
    using value_type = T;
    using iterator = typename std::vector<T>::iterator;
    using const_iterator = typename std::vector<T>::const_iterator;

    /* Nothing. An EMPTY array is Array(std::vector<T>{}), which owns storage. */
    Array() = default;
    Array(std::vector<T> v) : _p(std::make_shared<std::vector<T>>(std::move(v))) {}
    Array(std::initializer_list<T> items) : _p(std::make_shared<std::vector<T>>(items)) {}

    /* `Is Nothing` (CppCodeGenerator.EmitNullTest). */
    bool is_nothing() const { return !_p; }

    T& operator[](size_t i) { return storage()[i]; }
    const T& operator[](size_t i) const { return storage()[i]; }
    size_t size() const { return storage().size(); }
    bool empty() const { return storage().empty(); }
    T* data() { return storage().data(); }
    const T* data() const { return storage().data(); }
    T& front() { return storage().front(); }
    T& back() { return storage().back(); }
    iterator begin() { return storage().begin(); }
    iterator end() { return storage().end(); }
    const_iterator begin() const { return storage().cbegin(); }
    const_iterator end() const { return storage().cend(); }

    /* The storage itself, for runtime code written against std::vector. */
    std::vector<T>& vec() { return storage(); }
    const std::vector<T>& vec() const { return storage(); }
    operator std::vector<T>&() { return storage(); }
    operator const std::vector<T>&() const { return storage(); }

    /* Reference equality, as `Is` on a .NET array: two Nothing handles are equal. */
    bool operator==(const Array& other) const { return _p == other._p; }
    bool operator!=(const Array& other) const { return _p != other._p; }
};

/* ReDim builds a NEW array, as .NET's does: an alias taken before the ReDim keeps the old one.
   `ReDim Preserve` of a Nothing array preserves nothing and allocates, as on .NET. */
template <typename T>
inline Array<T> ReDimArray(const Array<T>& array, int64_t count, bool preserve) {
    if (count < 0) throw std::out_of_range(""ReDim size cannot be negative"");
    std::vector<T> resized;
    if (preserve && !array.is_nothing()) {
        const size_t keep = array.size() < (size_t)count ? array.size() : (size_t)count;
        resized.assign(array.begin(), array.begin() + (std::ptrdiff_t)keep);
    }
    resized.resize((size_t)count);
    return Array<T>(std::move(resized));
}

}
#endif
";
        }
    }
}
