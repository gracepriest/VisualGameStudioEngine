namespace BasicLang.Compiler.CodeGen.CPlusPlus
{
    /// <summary>
    /// Single source of truth for the portable C++ collection wrapper types used by the
    /// C++ backend's Layer-1 standard library: <c>BasicLang::List&lt;T&gt;</c> (over
    /// <c>std::vector</c>), <c>BasicLang::Dictionary&lt;K,V&gt;</c> (over
    /// <c>std::unordered_map</c>), and <c>BasicLang::HashSet&lt;T&gt;</c> (over
    /// <c>std::unordered_set</c>).
    ///
    /// These wrappers own the .NET-name -> C++ semantics (e.g. <c>Add</c>, <c>Count()</c>,
    /// <c>ContainsKey</c>) so that most BasicLang member calls later lower to C++ by raw
    /// passthrough.
    ///
    /// <para>
    /// The <see cref="Source"/> string below is BOTH emitted verbatim into generated
    /// <c>.cpp</c> output AND compiled directly by the isolated unit test
    /// (<c>CppCollectionsRuntimeTests</c>) to prove the wrappers behave correctly. Keep this
    /// as the ONLY definition of these wrappers so the emitted code and the tested code can
    /// never drift.
    /// </para>
    ///
    /// <para>
    /// Requires the following standard headers to be included by any translation unit that
    /// uses these types: <c>&lt;vector&gt; &lt;unordered_map&gt; &lt;unordered_set&gt;
    /// &lt;algorithm&gt; &lt;stdexcept&gt; &lt;cstdint&gt;</c>.
    /// </para>
    /// </summary>
    public static class CppCollectionsRuntime
    {
        /// <summary>
        /// The C++ source defining <c>BasicLang::List</c>, <c>BasicLang::Dictionary</c>, and
        /// <c>BasicLang::HashSet</c>. Emitted into generated code and compiled by the unit test.
        /// </summary>
        public const string Source = @"namespace BasicLang {

template <typename T>
class List {
    std::vector<T> _v;
public:
    List() = default;
    void Add(const T& item) { _v.push_back(item); }
    int32_t Count() const { return static_cast<int32_t>(_v.size()); }
    T& operator[](int32_t i) { return _v.at(static_cast<size_t>(i)); }
    const T& operator[](int32_t i) const { return _v.at(static_cast<size_t>(i)); }
    bool Contains(const T& item) const { return std::find(_v.begin(), _v.end(), item) != _v.end(); }
    int32_t IndexOf(const T& item) const {
        auto it = std::find(_v.begin(), _v.end(), item);
        return it == _v.end() ? -1 : static_cast<int32_t>(it - _v.begin());
    }
    void Remove(const T& item) {
        auto it = std::find(_v.begin(), _v.end(), item);
        if (it != _v.end()) _v.erase(it);
    }
    void RemoveAt(int32_t i) { _v.erase(_v.begin() + i); }
    void Insert(int32_t i, const T& item) { _v.insert(_v.begin() + i, item); }
    void Clear() { _v.clear(); }
    typename std::vector<T>::iterator begin() { return _v.begin(); }
    typename std::vector<T>::iterator end() { return _v.end(); }
    typename std::vector<T>::const_iterator begin() const { return _v.begin(); }
    typename std::vector<T>::const_iterator end() const { return _v.end(); }
};

template <typename K, typename V>
class Dictionary {
    std::unordered_map<K, V> _m;
public:
    Dictionary() = default;
    void Add(const K& key, const V& value) {
        if (_m.count(key)) throw std::runtime_error(""An item with the same key has already been added."");
        _m[key] = value;
    }
    V Get(const K& key) const {
        auto it = _m.find(key);
        if (it == _m.end()) throw std::runtime_error(""The given key was not present in the dictionary."");
        return it->second;
    }
    void Set(const K& key, const V& value) { _m[key] = value; }
    bool ContainsKey(const K& key) const { return _m.count(key) > 0; }
    bool TryGetValue(const K& key, V& value) const {
        auto it = _m.find(key);
        if (it == _m.end()) return false;
        value = it->second;
        return true;
    }
    // Keys()/Values() return a std::shared_ptr<List<...>> so a BasicLang-level
    // `List(Of K)` (which is std::shared_ptr<BasicLang::List<K>>) binds type-consistently,
    // and `For Each k In dict.Keys` derefs the returned shared_ptr in the range-for.
    std::shared_ptr<List<K>> Keys() const { auto ks = std::make_shared<List<K>>(); for (const auto& kv : _m) ks->Add(kv.first); return ks; }
    std::shared_ptr<List<V>> Values() const { auto vs = std::make_shared<List<V>>(); for (const auto& kv : _m) vs->Add(kv.second); return vs; }
    bool Remove(const K& key) { return _m.erase(key) > 0; }
    int32_t Count() const { return static_cast<int32_t>(_m.size()); }
    void Clear() { _m.clear(); }
};

template <typename T>
class HashSet {
    std::unordered_set<T> _s;
public:
    HashSet() = default;
    bool Add(const T& item) { return _s.insert(item).second; }
    bool Contains(const T& item) const { return _s.count(item) > 0; }
    bool Remove(const T& item) { return _s.erase(item) > 0; }
    int32_t Count() const { return static_cast<int32_t>(_s.size()); }
    void Clear() { _s.clear(); }
    typename std::unordered_set<T>::iterator begin() { return _s.begin(); }
    typename std::unordered_set<T>::iterator end() { return _s.end(); }
    typename std::unordered_set<T>::const_iterator begin() const { return _s.begin(); }
    typename std::unordered_set<T>::const_iterator end() const { return _s.end(); }
};

}
";
    }
}
