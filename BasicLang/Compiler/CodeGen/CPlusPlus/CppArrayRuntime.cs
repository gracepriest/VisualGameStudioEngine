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
    /// definition. Include-free: needs &lt;vector&gt;, &lt;memory&gt;, &lt;initializer_list&gt;,
    /// &lt;stdexcept&gt; and &lt;cstdint&gt;, all in both modes' unconditional include sets.</para>
    /// </summary>
    public static class CppArrayRuntime
    {
        public const string Source = @"#ifndef BASICLANG_ARRAY_RUNTIME
#define BASICLANG_ARRAY_RUNTIME
namespace BasicLang {

/* A BasicLang array: a handle to SHARED std::vector storage. Copying it aliases, as a .NET
   array reference does. See CppArrayRuntime.cs. */
template <typename T>
class Array {
    std::shared_ptr<std::vector<T>> _p;
public:
    using value_type = T;
    using iterator = typename std::vector<T>::iterator;
    using const_iterator = typename std::vector<T>::const_iterator;

    Array() : _p(std::make_shared<std::vector<T>>()) {}
    Array(std::vector<T> v) : _p(std::make_shared<std::vector<T>>(std::move(v))) {}
    Array(std::initializer_list<T> items) : _p(std::make_shared<std::vector<T>>(items)) {}

    T& operator[](size_t i) { return (*_p)[i]; }
    const T& operator[](size_t i) const { return (*_p)[i]; }
    size_t size() const { return _p->size(); }
    bool empty() const { return _p->empty(); }
    T* data() { return _p->data(); }
    const T* data() const { return _p->data(); }
    T& front() { return _p->front(); }
    T& back() { return _p->back(); }
    iterator begin() { return _p->begin(); }
    iterator end() { return _p->end(); }
    const_iterator begin() const { return _p->begin(); }
    const_iterator end() const { return _p->end(); }

    /* The storage itself, for runtime code written against std::vector. */
    std::vector<T>& vec() { return *_p; }
    const std::vector<T>& vec() const { return *_p; }
    operator std::vector<T>&() { return *_p; }
    operator const std::vector<T>&() const { return *_p; }

    /* Reference equality, as `Is` on a .NET array. */
    bool operator==(const Array& other) const { return _p == other._p; }
    bool operator!=(const Array& other) const { return _p != other._p; }
};

/* ReDim builds a NEW array, as .NET's does: an alias taken before the ReDim keeps the old one. */
template <typename T>
inline Array<T> ReDimArray(const Array<T>& array, int64_t count, bool preserve) {
    if (count < 0) throw std::out_of_range(""ReDim size cannot be negative"");
    std::vector<T> resized;
    if (preserve) {
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
