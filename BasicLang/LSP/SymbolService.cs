using System.Collections.Generic;
using System.Linq;
using BasicLang.Compiler.AST;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace BasicLang.Compiler.LSP
{
    /// <summary>
    /// Service that provides symbol information
    /// </summary>
    public class SymbolService
    {
        private static readonly Dictionary<string, string> BuiltInDocs = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
        {
            // Keywords
            ["Sub"] = "**Sub** - Declares a subroutine (procedure that doesn't return a value)\n\n```vb\nSub Name(parameters)\n    ' statements\nEnd Sub\n```",
            ["Function"] = "**Function** - Declares a function that returns a value\n\n```vb\nFunction Name(parameters) As ReturnType\n    Return value\nEnd Function\n```",
            ["If"] = "**If** - Conditional statement\n\n```vb\nIf condition Then\n    ' statements\nElseIf condition Then\n    ' statements\nElse\n    ' statements\nEnd If\n```",
            ["For"] = "**For** - Counting loop\n\n```vb\nFor i = start To end Step increment\n    ' statements\nNext\n```",
            ["ForEach"] = "**For Each** - Iterate over a collection\n\n```vb\nFor Each item In collection\n    ' statements\nNext\n```",
            ["While"] = "**While** - Loop while condition is true\n\n```vb\nWhile condition\n    ' statements\nWend\n```",
            ["Do"] = "**Do** - Loop with condition\n\n```vb\nDo While condition\n    ' statements\nLoop\n```",
            ["Class"] = "**Class** - Declares a class\n\n```vb\nClass Name\n    ' members\nEnd Class\n```",
            ["Module"] = "**Module** - Declares a module containing shared members\n\n```vb\nModule Name\n    ' shared members\nEnd Module\n```",
            ["Interface"] = "**Interface** - Declares an interface contract\n\n```vb\nInterface IName\n    Sub Method()\n    Function Property() As Type\nEnd Interface\n```",
            ["Implements"] = "**Implements** - Implements an interface in a class\n\n```vb\nClass MyClass Implements IInterface\n    ' implementation\nEnd Class\n```",
            ["Inherits"] = "**Inherits** - Inherits from a base class\n\n```vb\nClass Derived Inherits BaseClass\n    ' members\nEnd Class\n```",
            ["Dim"] = "**Dim** - Declares a variable\n\n```vb\nDim name As Type\nDim name As Type = initialValue\n```",
            ["Const"] = "**Const** - Declares a constant\n\n```vb\nConst NAME As Type = value\n```",
            ["Property"] = "**Property** - Declares a property with getter/setter\n\n```vb\nProperty Name As Type\n    Get\n        Return _value\n    End Get\n    Set(value As Type)\n        _value = value\n    End Set\nEnd Property\n```",
            ["Public"] = "**Public** - Access modifier: visible to all code",
            ["Private"] = "**Private** - Access modifier: visible only within the containing type",
            ["Protected"] = "**Protected** - Access modifier: visible to containing type and derived types",
            ["Static"] = "**Static** - Shared member accessible without an instance",
            ["Using"] = "**Using** - Imports a namespace\n\n```vb\nUsing System.Collections\n```",
            ["Import"] = "**Import** - Imports a module from another file\n\n```vb\nImport MyModule\nImport \"./path/to/file.bas\"\n```",
            ["Namespace"] = "**Namespace** - Declares a namespace for organizing code\n\n```vb\nNamespace MyApp.Utils\n    ' types and members\nEnd Namespace\n```",
            ["Select"] = "**Select Case** - Multi-way branch statement\n\n```vb\nSelect Case expression\n    Case value1\n        ' statements\n    Case value2, value3\n        ' statements\n    Case Else\n        ' default\nEnd Select\n```",
            ["Try"] = "**Try** - Exception handling block\n\n```vb\nTry\n    ' code that may throw\nCatch ex As ExceptionType\n    ' handle exception\nFinally\n    ' cleanup code\nEnd Try\n```",
            ["Throw"] = "**Throw** - Raises an exception\n\n```vb\nThrow New Exception(\"Error message\")\n```",
            ["Return"] = "**Return** - Returns a value from a function\n\n```vb\nReturn value\n```",
            ["Exit"] = "**Exit** - Exits a loop, function, or subroutine early\n\n```vb\nExit For\nExit While\nExit Function\nExit Sub\n```",
            ["Continue"] = "**Continue** - Skips to the next iteration of a loop\n\n```vb\nContinue For\nContinue While\n```",
            ["Me"] = "**Me** - Reference to the current instance within a class",
            ["MyBase"] = "**MyBase** - Reference to the base class implementation",
            ["New"] = "**New** - Creates a new instance of a class\n\n```vb\nDim obj As New ClassName()\nDim obj = New ClassName(args)\n```",
            ["Nothing"] = "**Nothing** - Null reference value",
            ["True"] = "**True** - Boolean true value",
            ["False"] = "**False** - Boolean false value",

            // Built-in functions - I/O
            ["PrintLine"] = "**PrintLine**(text As String)\n\nPrints a line of text to the console followed by a newline.",
            ["Print"] = "**Print**(text As String)\n\nPrints text to the console without a newline.",
            ["ReadLine"] = "**ReadLine**() As String\n\nReads a line of text from the console.",
            ["ReadKey"] = "**ReadKey**() As Char\n\nReads a single key press from the console.",

            // String functions
            ["Len"] = "**Len**(str As String) As Integer\n\nReturns the length of a string.",
            ["Left"] = "**Left**(str As String, count As Integer) As String\n\nReturns the leftmost characters of a string.",
            ["Right"] = "**Right**(str As String, count As Integer) As String\n\nReturns the rightmost characters of a string.",
            ["Mid"] = "**Mid**(str As String, start As Integer, length As Integer) As String\n\nReturns a substring from the middle of a string.",
            ["UCase"] = "**UCase**(str As String) As String\n\nConverts a string to uppercase.",
            ["LCase"] = "**LCase**(str As String) As String\n\nConverts a string to lowercase.",
            ["Trim"] = "**Trim**(str As String) As String\n\nRemoves leading and trailing whitespace.",
            ["LTrim"] = "**LTrim**(str As String) As String\n\nRemoves leading whitespace.",
            ["RTrim"] = "**RTrim**(str As String) As String\n\nRemoves trailing whitespace.",
            ["InStr"] = "**InStr**(str As String, search As String) As Integer\n\nFinds the position of a substring (1-based, 0 if not found).",
            ["InStrRev"] = "**InStrRev**(str As String, search As String) As Integer\n\nFinds the last position of a substring (1-based, 0 if not found).",
            ["Replace"] = "**Replace**(str As String, old As String, new As String) As String\n\nReplaces all occurrences of a substring.",
            ["Split"] = "**Split**(str As String, delimiter As String) As String()\n\nSplits a string into an array of substrings.",
            ["Join"] = "**Join**(arr As String(), delimiter As String) As String\n\nJoins an array of strings with a delimiter.",
            ["Format"] = "**Format**(value, formatString As String) As String\n\nFormats a value according to a format string.",
            ["Chr"] = "**Chr**(code As Integer) As Char\n\nReturns the character for a Unicode code point.",
            ["Asc"] = "**Asc**(char As Char) As Integer\n\nReturns the Unicode code point of a character.",
            ["Space"] = "**Space**(count As Integer) As String\n\nReturns a string of spaces.",
            ["String"] = "**String**(count As Integer, char As Char) As String\n\nReturns a string of repeated characters.",
            ["StrReverse"] = "**StrReverse**(str As String) As String\n\nReverses a string.",

            // Math functions
            ["Abs"] = "**Abs**(num As Double) As Double\n\nReturns the absolute value of a number.",
            ["Sqrt"] = "**Sqrt**(num As Double) As Double\n\nReturns the square root of a number.",
            ["Pow"] = "**Pow**(base As Double, exponent As Double) As Double\n\nReturns base raised to the power of exponent.",
            ["Sin"] = "**Sin**(radians As Double) As Double\n\nReturns the sine of an angle in radians.",
            ["Cos"] = "**Cos**(radians As Double) As Double\n\nReturns the cosine of an angle in radians.",
            ["Tan"] = "**Tan**(radians As Double) As Double\n\nReturns the tangent of an angle in radians.",
            ["Asin"] = "**Asin**(value As Double) As Double\n\nReturns the arc sine in radians.",
            ["Acos"] = "**Acos**(value As Double) As Double\n\nReturns the arc cosine in radians.",
            ["Atan"] = "**Atan**(value As Double) As Double\n\nReturns the arc tangent in radians.",
            ["Atan2"] = "**Atan2**(y As Double, x As Double) As Double\n\nReturns the arc tangent of y/x in radians.",
            ["Log"] = "**Log**(num As Double) As Double\n\nReturns the natural logarithm (base e).",
            ["Log10"] = "**Log10**(num As Double) As Double\n\nReturns the base-10 logarithm.",
            ["Exp"] = "**Exp**(num As Double) As Double\n\nReturns e raised to the specified power.",
            ["Floor"] = "**Floor**(num As Double) As Double\n\nRounds down to the nearest integer.",
            ["Ceiling"] = "**Ceiling**(num As Double) As Double\n\nRounds up to the nearest integer.",
            ["Round"] = "**Round**(num As Double) As Double\n\nRounds to the nearest integer.",
            ["Truncate"] = "**Truncate**(num As Double) As Double\n\nRemoves the fractional part of a number.",
            ["Sign"] = "**Sign**(num As Double) As Integer\n\nReturns -1, 0, or 1 indicating the sign.",
            ["Min"] = "**Min**(a As Double, b As Double) As Double\n\nReturns the smaller of two values.",
            ["Max"] = "**Max**(a As Double, b As Double) As Double\n\nReturns the larger of two values.",
            ["Clamp"] = "**Clamp**(value As Double, min As Double, max As Double) As Double\n\nRestricts a value to a range.",
            ["Rnd"] = "**Rnd**() As Double\n\nReturns a random number between 0 and 1.",
            ["Randomize"] = "**Randomize**(seed As Integer)\n\nInitializes the random number generator.",

            // Type conversion
            ["CInt"] = "**CInt**(value) As Integer\n\nConverts a value to Integer.",
            ["CLng"] = "**CLng**(value) As Long\n\nConverts a value to Long.",
            ["CSng"] = "**CSng**(value) As Single\n\nConverts a value to Single.",
            ["CDbl"] = "**CDbl**(value) As Double\n\nConverts a value to Double.",
            ["CStr"] = "**CStr**(value) As String\n\nConverts a value to String.",
            ["CBool"] = "**CBool**(value) As Boolean\n\nConverts a value to Boolean.",
            ["CChar"] = "**CChar**(value) As Char\n\nConverts a value to Char.",
            ["CByte"] = "**CByte**(value) As Byte\n\nConverts a value to Byte.",
            ["CDate"] = "**CDate**(value) As Date\n\nConverts a value to Date.",

            // Array functions
            ["UBound"] = "**UBound**(arr As Array) As Integer\n\nReturns the upper bound (last index) of an array.",
            ["LBound"] = "**LBound**(arr As Array) As Integer\n\nReturns the lower bound (first index) of an array.",
            ["Array"] = "**Array**(elements...) As Array\n\nCreates an array from the specified elements.",
            ["ReDim"] = "**ReDim** arr(size) [Preserve]\n\nResizes an array, optionally preserving existing elements.",

            // Collections
            ["CreateList"] = "**CreateList**() As List\n\nCreates a new empty list (dynamic array).\n\n```vb\nDim items = CreateList()\nListAdd(items, \"value\")\n```",
            ["ListAdd"] = "**ListAdd**(list As List, item)\n\nAdds an item to the end of a list.",
            ["ListInsert"] = "**ListInsert**(list As List, index As Integer, item)\n\nInserts an item at the specified index.",
            ["ListRemove"] = "**ListRemove**(list As List, item) As Boolean\n\nRemoves the first occurrence of an item. Returns True if found.",
            ["ListRemoveAt"] = "**ListRemoveAt**(list As List, index As Integer)\n\nRemoves the item at the specified index.",
            ["ListClear"] = "**ListClear**(list As List)\n\nRemoves all items from the list.",
            ["ListCount"] = "**ListCount**(list As List) As Integer\n\nReturns the number of items in the list.",
            ["ListGet"] = "**ListGet**(list As List, index As Integer)\n\nGets the item at the specified index.",
            ["ListSet"] = "**ListSet**(list As List, index As Integer, item)\n\nSets the item at the specified index.",
            ["ListContains"] = "**ListContains**(list As List, item) As Boolean\n\nReturns True if the list contains the item.",
            ["ListIndexOf"] = "**ListIndexOf**(list As List, item) As Integer\n\nReturns the index of the item, or -1 if not found.",
            ["CreateDictionary"] = "**CreateDictionary**() As Dictionary\n\nCreates a new empty dictionary (key-value pairs).\n\n```vb\nDim dict = CreateDictionary()\nDictSet(dict, \"key\", \"value\")\n```",
            ["DictSet"] = "**DictSet**(dict As Dictionary, key, value)\n\nSets a value for the specified key.",
            ["DictGet"] = "**DictGet**(dict As Dictionary, key)\n\nGets the value for the specified key.",
            ["DictRemove"] = "**DictRemove**(dict As Dictionary, key) As Boolean\n\nRemoves the key-value pair. Returns True if found.",
            ["DictClear"] = "**DictClear**(dict As Dictionary)\n\nRemoves all key-value pairs.",
            ["DictCount"] = "**DictCount**(dict As Dictionary) As Integer\n\nReturns the number of key-value pairs.",
            ["DictContainsKey"] = "**DictContainsKey**(dict As Dictionary, key) As Boolean\n\nReturns True if the dictionary contains the key.",
            ["DictKeys"] = "**DictKeys**(dict As Dictionary) As List\n\nReturns a list of all keys.",
            ["DictValues"] = "**DictValues**(dict As Dictionary) As List\n\nReturns a list of all values.",
            ["CreateHashSet"] = "**CreateHashSet**() As HashSet\n\nCreates a new empty hash set (unique values).\n\n```vb\nDim set = CreateHashSet()\nSetAdd(set, \"value\")\n```",
            ["SetAdd"] = "**SetAdd**(set As HashSet, item) As Boolean\n\nAdds an item to the set. Returns True if added (not duplicate).",
            ["SetRemove"] = "**SetRemove**(set As HashSet, item) As Boolean\n\nRemoves an item from the set. Returns True if found.",
            ["SetClear"] = "**SetClear**(set As HashSet)\n\nRemoves all items from the set.",
            ["SetCount"] = "**SetCount**(set As HashSet) As Integer\n\nReturns the number of items in the set.",
            ["SetContains"] = "**SetContains**(set As HashSet, item) As Boolean\n\nReturns True if the set contains the item.",

            // LINQ-style operations
            ["Where"] = "**Where**(collection, predicate) As Collection\n\nFilters elements that match the predicate.\n\n```vb\nDim evens = Where(numbers, Function(x) x Mod 2 = 0)\n```",
            ["Select"] = "**Select**(collection, selector) As Collection\n\nProjects each element using the selector function.\n\n```vb\nDim doubled = Select(numbers, Function(x) x * 2)\n```",
            ["OrderBy"] = "**OrderBy**(collection, keySelector) As Collection\n\nSorts elements in ascending order by key.\n\n```vb\nDim sorted = OrderBy(people, Function(p) p.Name)\n```",
            ["OrderByDescending"] = "**OrderByDescending**(collection, keySelector) As Collection\n\nSorts elements in descending order by key.",
            ["First"] = "**First**(collection) As Element\n\nReturns the first element, or throws if empty.",
            ["FirstOrDefault"] = "**FirstOrDefault**(collection) As Element\n\nReturns the first element, or default value if empty.",
            ["Last"] = "**Last**(collection) As Element\n\nReturns the last element, or throws if empty.",
            ["LastOrDefault"] = "**LastOrDefault**(collection) As Element\n\nReturns the last element, or default value if empty.",
            ["Single"] = "**Single**(collection) As Element\n\nReturns the only element, throws if not exactly one.",
            ["SingleOrDefault"] = "**SingleOrDefault**(collection) As Element\n\nReturns the only element, or default if empty, throws if more than one.",
            ["Take"] = "**Take**(collection, count As Integer) As Collection\n\nReturns the first N elements.",
            ["Skip"] = "**Skip**(collection, count As Integer) As Collection\n\nSkips the first N elements.",
            ["Any"] = "**Any**(collection, [predicate]) As Boolean\n\nReturns True if any element matches (or if not empty).",
            ["All"] = "**All**(collection, predicate) As Boolean\n\nReturns True if all elements match the predicate.",
            ["Count"] = "**Count**(collection, [predicate]) As Integer\n\nCounts elements, optionally filtered by predicate.",
            ["Sum"] = "**Sum**(collection, [selector]) As Double\n\nSums numeric values.",
            ["Average"] = "**Average**(collection, [selector]) As Double\n\nCalculates the average of numeric values.",
            ["Aggregate"] = "**Aggregate**(collection, seed, func) As Result\n\nApplies an accumulator function.\n\n```vb\nDim sum = Aggregate(nums, 0, Function(a, x) a + x)\n```",
            ["Distinct"] = "**Distinct**(collection) As Collection\n\nReturns distinct elements.",
            ["GroupBy"] = "**GroupBy**(collection, keySelector) As GroupedCollection\n\nGroups elements by a key.",
            ["ToList"] = "**ToList**(collection) As List\n\nConverts a collection to a List.",
            ["ToArray"] = "**ToArray**(collection) As Array\n\nConverts a collection to an Array.",

            // String interpolation (for hover on $ strings)
            ["$\""] = "**String Interpolation**\n\nEmbed expressions in strings using $\"...{expr}...\" syntax.\n\n```vb\nDim name = \"World\"\nDim msg = $\"Hello, {name}!\"\nDim calc = $\"Result: {2 + 2}\"\n```",

            // Nullable types
            ["?"] = "**Nullable Type**\n\nDeclare nullable value types with ? suffix.\n\n```vb\nDim x As Integer?  ' Can be Nothing\nIf x.HasValue Then\n    Print(x.Value)\nEnd If\n```",
            ["HasValue"] = "**HasValue** As Boolean\n\nReturns True if the nullable has a value (not Nothing).",
            ["Value"] = "**Value**\n\nGets the value of a nullable. Throws if Nothing.",
            ["GetValueOrDefault"] = "**GetValueOrDefault**(defaultValue) \n\nReturns the value if present, otherwise the default.",

            // Types
            ["Integer"] = "**Integer**\n\n32-bit signed integer (-2,147,483,648 to 2,147,483,647)",
            ["Long"] = "**Long**\n\n64-bit signed integer",
            ["Short"] = "**Short**\n\n16-bit signed integer (-32,768 to 32,767)",
            ["Byte"] = "**Byte**\n\n8-bit unsigned integer (0 to 255)",
            ["SByte"] = "**SByte**\n\n8-bit signed integer (-128 to 127)",
            ["Single"] = "**Single**\n\n32-bit floating-point number",
            ["Double"] = "**Double**\n\n64-bit floating-point number",
            ["Decimal"] = "**Decimal**\n\n128-bit decimal number for financial calculations",
            ["String"] = "**String**\n\nText string of Unicode characters",
            ["Boolean"] = "**Boolean**\n\nTrue or False value",
            ["Char"] = "**Char**\n\nSingle Unicode character",
            ["Date"] = "**Date**\n\nDate and time value",
            ["Object"] = "**Object**\n\nBase type for all reference types",
            ["Variant"] = "**Variant**\n\nDynamic type that can hold any value",
            ["List"] = "**List**\n\nDynamic array that can grow and shrink.\n\nUse `CreateList()` to create a new list.",
            ["Dictionary"] = "**Dictionary**\n\nKey-value pair collection.\n\nUse `CreateDictionary()` to create a new dictionary.",
            ["HashSet"] = "**HashSet**\n\nCollection of unique values.\n\nUse `CreateHashSet()` to create a new hash set.",

            // Operators
            ["And"] = "**And** - Logical AND operator\n\n```vb\nIf a And b Then\n```",
            ["Or"] = "**Or** - Logical OR operator\n\n```vb\nIf a Or b Then\n```",
            ["Not"] = "**Not** - Logical NOT operator\n\n```vb\nIf Not condition Then\n```",
            ["Xor"] = "**Xor** - Logical XOR operator\n\n```vb\nIf a Xor b Then\n```",
            ["Mod"] = "**Mod** - Modulo (remainder) operator\n\n```vb\nresult = a Mod b\n```",
            ["AndAlso"] = "**AndAlso** - Short-circuit logical AND\n\nSecond operand only evaluated if first is True.",
            ["OrElse"] = "**OrElse** - Short-circuit logical OR\n\nSecond operand only evaluated if first is False.",
            ["Is"] = "**Is** - Reference equality comparison\n\n```vb\nIf obj Is Nothing Then\n```",
            ["IsNot"] = "**IsNot** - Reference inequality comparison\n\n```vb\nIf obj IsNot Nothing Then\n```",
            ["Like"] = "**Like** - Pattern matching operator\n\n```vb\nIf str Like \"A*\" Then  ' Starts with A\n```",
            ["TypeOf"] = "**TypeOf** - Type checking\n\n```vb\nIf TypeOf obj Is MyClass Then\n```",

            // Date/Time functions
            ["Now"] = "**Now**() As Date\n\nReturns the current date and time.",
            ["Today"] = "**Today**() As Date\n\nReturns the current date (time is midnight).",
            ["Year"] = "**Year**(d As Date) As Integer\n\nReturns the year component.",
            ["Month"] = "**Month**(d As Date) As Integer\n\nReturns the month component (1-12).",
            ["Day"] = "**Day**(d As Date) As Integer\n\nReturns the day component (1-31).",
            ["Hour"] = "**Hour**(d As Date) As Integer\n\nReturns the hour component (0-23).",
            ["Minute"] = "**Minute**(d As Date) As Integer\n\nReturns the minute component (0-59).",
            ["Second"] = "**Second**(d As Date) As Integer\n\nReturns the second component (0-59).",
            ["DateAdd"] = "**DateAdd**(interval As String, number As Integer, d As Date) As Date\n\nAdds a time interval to a date.",
            ["DateDiff"] = "**DateDiff**(interval As String, d1 As Date, d2 As Date) As Long\n\nReturns the difference between two dates.",

            // File I/O
            ["FileExists"] = "**FileExists**(path As String) As Boolean\n\nReturns True if the file exists.",
            ["DirectoryExists"] = "**DirectoryExists**(path As String) As Boolean\n\nReturns True if the directory exists.",
            ["ReadAllText"] = "**ReadAllText**(path As String) As String\n\nReads all text from a file.",
            ["WriteAllText"] = "**WriteAllText**(path As String, content As String)\n\nWrites text to a file, overwriting if exists.",
            ["AppendAllText"] = "**AppendAllText**(path As String, content As String)\n\nAppends text to a file.",
            ["ReadAllLines"] = "**ReadAllLines**(path As String) As String()\n\nReads all lines from a file.",
            ["WriteAllLines"] = "**WriteAllLines**(path As String, lines As String())\n\nWrites lines to a file.",
        };

        /// <summary>
        /// Get hover information for a word
        /// </summary>
        public string GetHoverInfo(DocumentState state, string word)
        {
            // Check built-in docs first
            if (BuiltInDocs.TryGetValue(word, out var docs))
            {
                return docs;
            }

            // Check document symbols
            if (state?.AST != null)
            {
                foreach (var decl in state.AST.Declarations)
                {
                    var info = GetDeclarationHoverInfo(decl, word);
                    if (info != null)
                        return info;
                }
            }

            return null;
        }

        private string GetDeclarationHoverInfo(ASTNode node, string word)
        {
            switch (node)
            {
                case FunctionNode func when func.Name.Equals(word, System.StringComparison.OrdinalIgnoreCase):
                    return FormatFunctionHover(func);

                case SubroutineNode sub when sub.Name.Equals(word, System.StringComparison.OrdinalIgnoreCase):
                    return FormatSubroutineHover(sub);

                case ClassNode cls when cls.Name.Equals(word, System.StringComparison.OrdinalIgnoreCase):
                    return FormatClassHover(cls);

                case VariableDeclarationNode varDecl when varDecl.Name.Equals(word, System.StringComparison.OrdinalIgnoreCase):
                    return FormatVariableHover(varDecl);

                case ConstantDeclarationNode constDecl when constDecl.Name.Equals(word, System.StringComparison.OrdinalIgnoreCase):
                    return FormatConstantHover(constDecl);

                case PropertyNode prop when prop.Name.Equals(word, System.StringComparison.OrdinalIgnoreCase):
                    return FormatPropertyHover(prop);

                case ClassNode cls:
                    // Search class members for the symbol
                    foreach (var member in cls.Members)
                    {
                        var memberInfo = GetDeclarationHoverInfo(member, word);
                        if (memberInfo != null)
                            return memberInfo;
                    }
                    break;
            }

            return null;
        }

        private string FormatFunctionHover(FunctionNode func)
        {
            var sb = new System.Text.StringBuilder();

            // Access modifier
            var access = func.Access == AccessModifier.Public ? "Public " :
                        func.Access == AccessModifier.Private ? "Private " :
                        func.Access == AccessModifier.Protected ? "Protected " : "";

            // Signature
            var funcParams = string.Join(", ", func.Parameters.Select(p =>
                $"{(p.IsByRef ? "ByRef " : "")}{p.Name} As {p.Type?.Name ?? "Variant"}{(p.DefaultValue != null ? $" = {FormatDefaultValue(p.DefaultValue)}" : "")}"));

            sb.AppendLine($"```vb");
            sb.AppendLine($"{access}Function {func.Name}({funcParams}) As {func.ReturnType?.Name ?? "Void"}");
            sb.AppendLine($"```");
            sb.AppendLine();
            sb.AppendLine("*User-defined function*");

            // Location info
            if (func.Line > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"📍 Defined at line {func.Line}");
            }

            return sb.ToString();
        }

        private string FormatSubroutineHover(SubroutineNode sub)
        {
            var sb = new System.Text.StringBuilder();

            // Access modifier
            var access = sub.Access == AccessModifier.Public ? "Public " :
                        sub.Access == AccessModifier.Private ? "Private " :
                        sub.Access == AccessModifier.Protected ? "Protected " : "";

            // Signature
            var subParams = string.Join(", ", sub.Parameters.Select(p =>
                $"{(p.IsByRef ? "ByRef " : "")}{p.Name} As {p.Type?.Name ?? "Variant"}{(p.DefaultValue != null ? $" = {FormatDefaultValue(p.DefaultValue)}" : "")}"));

            sb.AppendLine($"```vb");
            sb.AppendLine($"{access}Sub {sub.Name}({subParams})");
            sb.AppendLine($"```");
            sb.AppendLine();
            sb.AppendLine("*User-defined subroutine*");

            // Location info
            if (sub.Line > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"📍 Defined at line {sub.Line}");
            }

            return sb.ToString();
        }

        private string FormatClassHover(ClassNode cls)
        {
            var sb = new System.Text.StringBuilder();

            // Class declaration
            sb.Append($"```vb\nClass {cls.Name}");
            if (!string.IsNullOrEmpty(cls.BaseClass))
                sb.Append($" Inherits {cls.BaseClass}");
            if (cls.Interfaces != null && cls.Interfaces.Count > 0)
                sb.Append($" Implements {string.Join(", ", cls.Interfaces)}");
            sb.AppendLine("\n```");
            sb.AppendLine();
            sb.AppendLine("*User-defined class*");

            // Member summary
            if (cls.Members != null && cls.Members.Count > 0)
            {
                int methodCount = 0, propertyCount = 0, fieldCount = 0;
                foreach (var member in cls.Members)
                {
                    if (member is FunctionNode || member is SubroutineNode) methodCount++;
                    else if (member is PropertyNode) propertyCount++;
                    else if (member is VariableDeclarationNode) fieldCount++;
                }

                sb.AppendLine();
                var parts = new List<string>();
                if (methodCount > 0) parts.Add($"{methodCount} method{(methodCount != 1 ? "s" : "")}");
                if (propertyCount > 0) parts.Add($"{propertyCount} propert{(propertyCount != 1 ? "ies" : "y")}");
                if (fieldCount > 0) parts.Add($"{fieldCount} field{(fieldCount != 1 ? "s" : "")}");
                if (parts.Count > 0)
                    sb.AppendLine($"📦 Contains: {string.Join(", ", parts)}");
            }

            // Location info
            if (cls.Line > 0)
            {
                sb.AppendLine($"📍 Defined at line {cls.Line}");
            }

            return sb.ToString();
        }

        private string FormatVariableHover(VariableDeclarationNode varDecl)
        {
            var sb = new System.Text.StringBuilder();

            var typeName = varDecl.Type?.Name ?? "Variant";
            var isNullable = typeName.EndsWith("?");

            sb.AppendLine($"```vb");
            sb.AppendLine($"Dim {varDecl.Name} As {typeName}");
            sb.AppendLine($"```");
            sb.AppendLine();

            if (isNullable)
                sb.AppendLine($"*Nullable variable* (can be `Nothing`)");
            else
                sb.AppendLine("*Variable*");

            // Show initial value if present
            if (varDecl.Initializer != null)
            {
                sb.AppendLine();
                sb.AppendLine($"Initial value: `{FormatDefaultValue(varDecl.Initializer)}`");
            }

            // Location info
            if (varDecl.Line > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"📍 Defined at line {varDecl.Line}");
            }

            return sb.ToString();
        }

        private string FormatConstantHover(ConstantDeclarationNode constDecl)
        {
            var sb = new System.Text.StringBuilder();

            sb.AppendLine($"```vb");
            sb.AppendLine($"Const {constDecl.Name} As {constDecl.Type?.Name ?? "Variant"} = {FormatDefaultValue(constDecl.Value)}");
            sb.AppendLine($"```");
            sb.AppendLine();
            sb.AppendLine("*Constant* (read-only)");

            // Location info
            if (constDecl.Line > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"📍 Defined at line {constDecl.Line}");
            }

            return sb.ToString();
        }

        private string FormatPropertyHover(PropertyNode prop)
        {
            var sb = new System.Text.StringBuilder();

            // Access modifier
            var access = prop.Access == AccessModifier.Public ? "Public " :
                        prop.Access == AccessModifier.Private ? "Private " :
                        prop.Access == AccessModifier.Protected ? "Protected " : "";

            sb.AppendLine($"```vb");
            sb.AppendLine($"{access}Property {prop.Name} As {prop.PropertyType?.Name ?? "Variant"}");
            sb.AppendLine($"```");
            sb.AppendLine();

            // Accessor info
            var accessors = new List<string>();
            if (prop.Getter != null) accessors.Add("Get");
            if (prop.Setter != null) accessors.Add("Set");

            if (accessors.Count > 0)
                sb.AppendLine($"*Property* with {string.Join(" and ", accessors)} accessor{(accessors.Count > 1 ? "s" : "")}");
            else if (prop.IsReadOnly)
                sb.AppendLine("*Read-only property*");
            else
                sb.AppendLine("*Property*");

            // Location info
            if (prop.Line > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"📍 Defined at line {prop.Line}");
            }

            return sb.ToString();
        }

        private string FormatDefaultValue(ASTNode node)
        {
            if (node == null) return "Nothing";

            switch (node)
            {
                case LiteralExpressionNode lit:
                    if (lit.Value is string s)
                        return $"\"{s}\"";
                    if (lit.Value is bool b)
                        return b ? "True" : "False";
                    return lit.Value?.ToString() ?? "Nothing";

                case IdentifierExpressionNode id:
                    return id.Name;

                default:
                    return "...";
            }
        }

        /// <summary>
        /// Find the definition location of a symbol
        /// </summary>
        public Location FindDefinition(DocumentState state, string word)
        {
            if (state?.AST == null)
                return null;

            foreach (var decl in state.AST.Declarations)
            {
                var location = FindDeclarationLocation(state, decl, word);
                if (location != null)
                    return location;
            }

            return null;
        }

        private Location FindDeclarationLocation(DocumentState state, ASTNode node, string word)
        {
            int line = -1;
            int column = -1;

            switch (node)
            {
                case FunctionNode func when func.Name.Equals(word, System.StringComparison.OrdinalIgnoreCase):
                    line = func.Line;
                    column = func.Column;
                    break;

                case SubroutineNode sub when sub.Name.Equals(word, System.StringComparison.OrdinalIgnoreCase):
                    line = sub.Line;
                    column = sub.Column;
                    break;

                case ClassNode cls when cls.Name.Equals(word, System.StringComparison.OrdinalIgnoreCase):
                    line = cls.Line;
                    column = cls.Column;
                    break;

                case VariableDeclarationNode varDecl when varDecl.Name.Equals(word, System.StringComparison.OrdinalIgnoreCase):
                    line = varDecl.Line;
                    column = varDecl.Column;
                    break;

                case ConstantDeclarationNode constDecl when constDecl.Name.Equals(word, System.StringComparison.OrdinalIgnoreCase):
                    line = constDecl.Line;
                    column = constDecl.Column;
                    break;
            }

            if (line > 0)
            {
                return new Location
                {
                    Uri = state.Uri,
                    Range = new LspRange(
                        new Position(line - 1, column - 1),
                        new Position(line - 1, column - 1 + word.Length))
                };
            }

            return null;
        }

        /// <summary>
        /// Get document symbols for outline view
        /// </summary>
        public List<DocumentSymbol> GetDocumentSymbols(DocumentState state)
        {
            var symbols = new List<DocumentSymbol>();

            if (state?.AST == null)
                return symbols;

            foreach (var decl in state.AST.Declarations)
            {
                var symbol = CreateDocumentSymbol(decl);
                if (symbol != null)
                    symbols.Add(symbol);
            }

            return symbols;
        }

        private DocumentSymbol CreateDocumentSymbol(ASTNode node)
        {
            switch (node)
            {
                case FunctionNode func:
                    var funcChildren = new List<DocumentSymbol>();
                    foreach (var param in func.Parameters)
                    {
                        funcChildren.Add(new DocumentSymbol
                        {
                            Name = param.Name,
                            Kind = SymbolKind.Variable,
                            Range = new LspRange(new Position(param.Line - 1, 0), new Position(param.Line - 1, 100)),
                            SelectionRange = new LspRange(new Position(param.Line - 1, 0), new Position(param.Line - 1, param.Name.Length))
                        });
                    }
                    return new DocumentSymbol
                    {
                        Name = func.Name,
                        Kind = SymbolKind.Function,
                        Detail = $"As {func.ReturnType?.Name ?? "Void"}",
                        Range = new LspRange(new Position(func.Line - 1, 0), new Position(func.Line + 10, 0)),
                        SelectionRange = new LspRange(new Position(func.Line - 1, 0), new Position(func.Line - 1, func.Name.Length + 10)),
                        Children = funcChildren.Count > 0 ? funcChildren : null
                    };

                case SubroutineNode sub:
                    return new DocumentSymbol
                    {
                        Name = sub.Name,
                        Kind = SymbolKind.Method,
                        Range = new LspRange(new Position(sub.Line - 1, 0), new Position(sub.Line + 10, 0)),
                        SelectionRange = new LspRange(new Position(sub.Line - 1, 0), new Position(sub.Line - 1, sub.Name.Length + 5))
                    };

                case ClassNode cls:
                    var classChildren = new List<DocumentSymbol>();
                    foreach (var member in cls.Members)
                    {
                        var memberSymbol = CreateDocumentSymbol(member);
                        if (memberSymbol != null)
                            classChildren.Add(memberSymbol);
                    }
                    return new DocumentSymbol
                    {
                        Name = cls.Name,
                        Kind = SymbolKind.Class,
                        Detail = !string.IsNullOrEmpty(cls.BaseClass) ? $"Inherits {cls.BaseClass}" : null,
                        Range = new LspRange(new Position(cls.Line - 1, 0), new Position(cls.Line + 50, 0)),
                        SelectionRange = new LspRange(new Position(cls.Line - 1, 0), new Position(cls.Line - 1, cls.Name.Length + 7)),
                        Children = classChildren.Count > 0 ? classChildren : null
                    };

                case VariableDeclarationNode varDecl:
                    return new DocumentSymbol
                    {
                        Name = varDecl.Name,
                        Kind = SymbolKind.Variable,
                        Detail = varDecl.Type?.Name,
                        Range = new LspRange(new Position(varDecl.Line - 1, 0), new Position(varDecl.Line - 1, 100)),
                        SelectionRange = new LspRange(new Position(varDecl.Line - 1, 0), new Position(varDecl.Line - 1, varDecl.Name.Length))
                    };

                case ConstantDeclarationNode constDecl:
                    return new DocumentSymbol
                    {
                        Name = constDecl.Name,
                        Kind = SymbolKind.Constant,
                        Detail = constDecl.Type?.Name,
                        Range = new LspRange(new Position(constDecl.Line - 1, 0), new Position(constDecl.Line - 1, 100)),
                        SelectionRange = new LspRange(new Position(constDecl.Line - 1, 0), new Position(constDecl.Line - 1, constDecl.Name.Length))
                    };
            }

            return null;
        }
    }
}
