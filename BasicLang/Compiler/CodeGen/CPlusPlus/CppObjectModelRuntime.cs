namespace BasicLang.Compiler.CodeGen.CPlusPlus
{
    /// <summary>
    /// The C++ object model's runtime half (ADR-0015): how a BasicLang CLASS instance is created
    /// and how it names ITSELF as the <c>std::shared_ptr</c> every other reference to it is.
    ///
    /// <para><b><c>BasicLang::Self(this)</c></b> is the ONE spelling of <c>Me</c> used as a
    /// value (D1/D3). Every hierarchy ROOT derives from <c>std::enable_shared_from_this&lt;Root&gt;</c>
    /// as its LAST base, so <c>shared_from_this()</c> is found unambiguously from any class in the
    /// hierarchy; the helper takes <c>T*</c>, which is what keeps the dependent-name problem
    /// (<c>this-&gt;shared_from_this()</c> inside a template) solved once, here, and casts the
    /// root's pointer back to the enclosing class.</para>
    ///
    /// <para><b>Two-phase construction</b> (D2). <c>BasicLang::New&lt;T&gt;(args...)</c> is the only
    /// way a class instance is made: <c>make_shared</c> runs the TAG constructor
    /// (<c>T(Construct)</c>), which leaves every field at its .NET default, and only then — with
    /// ownership already in place, so <c>Me</c> is usable — calls <c>ctor_(args...)</c>, which runs
    /// the base's <c>ctor_</c>, this class's field initializers and the body, in VB's order. A
    /// virtual call from a base constructor therefore reaches the derived override (as .NET's
    /// does), and a constructor may hand <c>Me</c> to anything.</para>
    ///
    /// <para><b>A foreign-rooted hierarchy</b> (D2a: the root's base is a <c>#CppInclude</c>d
    /// class) has tag constructors that ALSO take the VB constructor's parameters, because the
    /// foreign base can only be built in a member-initializer list. <c>New</c> stays ONE
    /// spelling: when <c>T(Construct, A&amp;...)</c> is constructible the arguments reach the tag
    /// constructor as LVALUES (never moved), and only <c>ctor_</c> receives them forwarded.</para>
    ///
    /// <para><c>HasSharedFromThis&lt;F&gt;</c> backs the <c>static_assert</c> emitted beside a
    /// foreign-rooted class head: an <c>enable_shared_from_this</c> inside the foreign base would
    /// be a SECOND such subobject in one object, which leaves <c>weak_this</c> unset and fails
    /// with <c>bad_weak_ptr</c> at run time instead of at compile time.</para>
    ///
    /// <para>Spliced ON DEMAND — only for a module that declares a class (a
    /// <c>Structure</c> does not count) — in both emission modes (CppCodeGenerator.GenerateHeader
    /// and CppCodeGenerator.Split's EmitRuntimeHeader — keep them in sync), so a program with no
    /// class emits byte-identically to before it existed. <c>#ifndef</c>-guarded like its
    /// siblings. Include-free: needs &lt;memory&gt;, &lt;type_traits&gt; and &lt;utility&gt;, which the
    /// always-spliced BCL body already relies on (its <c>StringBuilder</c> is itself an
    /// <c>enable_shared_from_this</c>). No name here contains <c>__</c> (a reserved identifier).</para>
    /// </summary>
    public static class CppObjectModelRuntime
    {
        public const string Source = @"#ifndef BASICLANG_OBJECT_MODEL_RUNTIME
#define BASICLANG_OBJECT_MODEL_RUNTIME
namespace BasicLang {

/* ADR-0015 D2: the tag that selects a class's TAG constructor — the first phase of
   construction, which leaves every field at its .NET default and runs no user code. */
struct Construct {};

/* ADR-0015 D1/D3: `Me` as a VALUE. The hierarchy root is an enable_shared_from_this, so this
   is the owning shared_ptr, cast back to the enclosing class. */
template<class T> std::shared_ptr<T> Self(T* self) {
    return std::static_pointer_cast<T>(self->shared_from_this());
}

/* ADR-0015 D2/D2a: the ONE way a class instance is created. The tag constructor runs inside
   make_shared; ctor_ runs once ownership exists. A foreign-rooted class's tag constructor also
   takes the arguments (as lvalues) to build its foreign base; ctor_ alone gets them forwarded. */
template<class T, class... A> std::shared_ptr<T> New(A&&... a) {
    std::shared_ptr<T> p;
    if constexpr (std::is_constructible_v<T, Construct, A&...>)
        p = std::make_shared<T>(Construct{}, a...);
    else
        p = std::make_shared<T>(Construct{});
    p->ctor_(std::forward<A>(a)...);
    return p;
}

/* ADR-0015 D2a: true when F already derives from some std::enable_shared_from_this — a
   foreign base of a BasicLang class must not, or the object would carry two. */
namespace ObjectModelDetail {
template<class U> std::true_type SharedFromThisProbe(const volatile std::enable_shared_from_this<U>*);
std::false_type SharedFromThisProbe(...);
}
template<class F> inline constexpr bool HasSharedFromThis =
    decltype(ObjectModelDetail::SharedFromThisProbe(static_cast<F*>(nullptr)))::value;

} /* namespace BasicLang */
#endif /* BASICLANG_OBJECT_MODEL_RUNTIME */
";
    }
}
