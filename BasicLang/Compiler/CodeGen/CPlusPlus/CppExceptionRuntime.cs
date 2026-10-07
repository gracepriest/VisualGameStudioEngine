namespace BasicLang.Compiler.CodeGen.CPlusPlus
{
    /// <summary>
    /// #151 (owner's ruling, option b): <c>BasicLang::Exception</c> — the base of a BasicLang class
    /// that <c>Inherits Exception</c> (or another built-in exception, <see cref="CppExceptionTypes"/>)
    /// — and <c>BasicLang::ThrownException</c>, what a <c>Throw</c> of such an object throws.
    ///
    /// <para><b>The object.</b> <c>BasicLang::Exception</c> is an ORDINARY hierarchy root of the C++
    /// object model (ADR-0015): it carries the one <c>enable_shared_from_this</c>, has a tag
    /// constructor and <c>ctor_</c> overloads, and is reached through <c>std::shared_ptr</c> and
    /// <c>-&gt;</c> like any BasicLang class; the generated class below it is NOT a root
    /// (<see cref="CppObjectModel.IsRuntimeExceptionBase"/>) and calls
    /// <c>BasicLang::Exception::ctor_(msg)</c> for its <c>MyBase.New(msg)</c>. <c>Message</c> is a
    /// data member because every lowering of a read of it — bare, <c>Me.Message</c>,
    /// <c>e.Message</c> — is a member access by name. Each generated exception class overrides
    /// <c>blExceptionChain_</c> with its own chain (<see cref="CppObjectModel.ExceptionChainOf"/>),
    /// so the chain a throw carries is the DYNAMIC type's, also for <c>Throw e</c> through a base
    /// reference, and the parameterless <c>ctor_</c> can name the class in .NET's default message
    /// (it runs after the tag constructors, so the virtual reaches the most-derived class).</para>
    ///
    /// <para><b>The throw.</b> <c>ThrownException</c> DERIVES from <c>NetException</c> (whose own
    /// derivation is untouched): it carries the object AND its chain, so it enters the §11.1
    /// ladder every typed <c>Catch</c> already emits, whose ';'-delimited element matching then
    /// decides a <c>Catch e As MyErr</c> — or <c>As Exception</c>, or <c>As ArgumentException</c>
    /// for a class built on it — by TYPE, never by clause position. <c>what()</c> is the message at
    /// throw time, so <c>Catch ex As Exception</c> reads it unchanged. A <c>Catch</c> of a user
    /// exception binds the SAME object back through <c>BasicLang::Caught&lt;T&gt;</c>
    /// (<c>dynamic_pointer_cast</c>), so its own members read back, and a bare <c>Throw</c>
    /// rethrows the very carrier.</para>
    ///
    /// <para>Spliced ON DEMAND — only for a module that declares an exception class — in both
    /// emission modes (CppCodeGenerator.GenerateHeader and CppCodeGenerator.Split's
    /// EmitRuntimeHeader — keep them in sync), AFTER <see cref="CppObjectModelRuntime"/> (it needs
    /// <c>Construct</c>) and <see cref="CppNetExceptionRuntime"/> (it derives from
    /// <c>NetException</c>), so a program with no exception class emits byte-identically.
    /// <c>#ifndef</c>-guarded like its siblings. Include-free: &lt;memory&gt;, &lt;string&gt; and
    /// &lt;stdexcept&gt; are in both modes' unconditional include sets. No name here contains
    /// <c>__</c> (a reserved identifier).</para>
    /// </summary>
    public static class CppExceptionRuntime
    {
        public const string Source = @"#ifndef BASICLANG_EXCEPTION_RUNTIME
#define BASICLANG_EXCEPTION_RUNTIME
namespace BasicLang {

/* #151: the base of a BasicLang class that Inherits Exception (or another built-in exception).
   An ordinary ADR-0015 hierarchy root: New<T> builds it in two phases, Me is Self(this). */
class Exception : public std::enable_shared_from_this<Exception> {
public:
    std::string Message;

    explicit Exception(Construct) {}
    void ctor_() { Message = ""Exception of type '"" + blTypeName_() + ""' was thrown.""; }
    void ctor_(const std::string& message) { Message = message; }
    virtual ~Exception() {}

    /* The ';'-separated type chain, most-derived FIRST; every generated exception class
       overrides it with its own. */
    virtual const char* blExceptionChain_() const { return ""System.Exception""; }

    std::string blTypeName_() const {
        const std::string chain = blExceptionChain_();
        const std::size_t sep = chain.find(';');
        return sep == std::string::npos ? chain : chain.substr(0, sep);
    }

    virtual std::string ToString() {
        return Message.empty() ? blTypeName_() : blTypeName_() + "": "" + Message;
    }
};

/* #151: what `Throw obj` throws for a BasicLang exception object — a NetException carrying the
   object's chain (so the typed-Catch ladder decides by type) and the object itself (so a Catch
   of the user type binds the same object back). */
class ThrownException : public NetException {
public:
    explicit ThrownException(const std::shared_ptr<Exception>& object)
        : NetException(std::string(object->blExceptionChain_()), object->Message), object_(object) {}

    const std::shared_ptr<Exception>& Object() const { return object_; }

private:
    std::shared_ptr<Exception> object_;
};

/* `Throw obj`: .NET throws NullReferenceException for a Nothing operand. */
[[noreturn]] inline void ThrowObject(const std::shared_ptr<Exception>& object) {
    if (!object)
        throw NetException(""System.NullReferenceException;System.SystemException;System.Exception"",
                           ""Object reference not set to an instance of an object."");
    throw ThrownException(object);
}

/* A `Catch e As T` arm's binding: the thrown object as T, or null for anything else. */
template<class T> std::shared_ptr<T> Caught(const NetException& caught) {
    const ThrownException* thrown = dynamic_cast<const ThrownException*>(&caught);
    return thrown != nullptr ? std::dynamic_pointer_cast<T>(thrown->Object()) : nullptr;
}

} /* namespace BasicLang */
#endif /* BASICLANG_EXCEPTION_RUNTIME */
";
    }
}
