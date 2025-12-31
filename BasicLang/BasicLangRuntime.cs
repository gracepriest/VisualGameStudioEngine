using System;
using System.Collections.Generic;

namespace BasicLang.Runtime
{
    /// <summary>
    /// Runtime library for BasicLang - provides helper functions for features
    /// that don't map directly to C#
    /// </summary>
    public static class BasicLangRuntime
    {
        // ====================================================================
        // String Functions
        // ====================================================================
        
        /// <summary>
        /// VB-style Mid function - extracts substring
        /// </summary>
        public static string Mid(string str, int start, int length)
        {
            if (str == null) return "";
            if (start < 1) start = 1;
            if (start > str.Length) return "";
            
            int index = start - 1; // VB uses 1-based indexing
            
            if (index + length > str.Length)
            {
                length = str.Length - index;
            }
            
            return str.Substring(index, length);
        }
        
        /// <summary>
        /// VB-style Len function - returns length of string
        /// </summary>
        public static int Len(string str)
        {
            return str?.Length ?? 0;
        }
        
        /// <summary>
        /// VB-style Left function
        /// </summary>
        public static string Left(string str, int length)
        {
            if (str == null) return "";
            if (length < 0) return "";
            if (length > str.Length) return str;
            
            return str.Substring(0, length);
        }
        
        /// <summary>
        /// VB-style Right function
        /// </summary>
        public static string Right(string str, int length)
        {
            if (str == null) return "";
            if (length < 0) return "";
            if (length > str.Length) return str;
            
            return str.Substring(str.Length - length, length);
        }
        
        /// <summary>
        /// VB-style UCase function
        /// </summary>
        public static string UCase(string str)
        {
            return str?.ToUpper() ?? "";
        }
        
        /// <summary>
        /// VB-style LCase function
        /// </summary>
        public static string LCase(string str)
        {
            return str?.ToLower() ?? "";
        }
        
        /// <summary>
        /// VB-style Trim function
        /// </summary>
        public static string Trim(string str)
        {
            return str?.Trim() ?? "";
        }
        
        // ====================================================================
        // Array Functions
        // ====================================================================
        
        /// <summary>
        /// VB-style UBound function - returns upper bound of array
        /// </summary>
        public static int UBound<T>(T[] array, int dimension = 1)
        {
            if (array == null)
                throw new ArgumentNullException(nameof(array));
            
            if (dimension == 1)
            {
                return array.Length - 1;
            }
            
            throw new ArgumentException("Multi-dimensional arrays not yet supported");
        }
        
        /// <summary>
        /// VB-style LBound function - returns lower bound of array
        /// </summary>
        public static int LBound<T>(T[] array, int dimension = 1)
        {
            if (array == null)
                throw new ArgumentNullException(nameof(array));
            
            return 0; // C# arrays are always 0-based
        }
        
        // ====================================================================
        // Conversion Functions
        // ====================================================================
        
        /// <summary>
        /// Convert to integer with VB-style rounding
        /// </summary>
        public static int CInt(object value)
        {
            if (value == null) return 0;
            
            if (value is int i) return i;
            if (value is double d) return (int)Math.Round(d);
            if (value is float f) return (int)Math.Round(f);
            if (value is string s) return int.Parse(s);
            
            return Convert.ToInt32(value);
        }
        
        /// <summary>
        /// Convert to long with VB-style rounding
        /// </summary>
        public static long CLng(object value)
        {
            if (value == null) return 0;
            
            if (value is long l) return l;
            if (value is double d) return (long)Math.Round(d);
            if (value is float f) return (long)Math.Round(f);
            if (value is string s) return long.Parse(s);
            
            return Convert.ToInt64(value);
        }
        
        /// <summary>
        /// Convert to string
        /// </summary>
        public static string CStr(object value)
        {
            return value?.ToString() ?? "";
        }
        
        /// <summary>
        /// Convert to boolean
        /// </summary>
        public static bool CBool(object value)
        {
            if (value == null) return false;
            
            if (value is bool b) return b;
            if (value is int i) return i != 0;
            if (value is string s) return bool.Parse(s);
            
            return Convert.ToBoolean(value);
        }
        
        // ====================================================================
        // Math Functions
        // ====================================================================
        
        /// <summary>
        /// Integer division
        /// </summary>
        public static int IntDiv(int a, int b)
        {
            return a / b;
        }
        
        /// <summary>
        /// Integer division
        /// </summary>
        public static long IntDiv(long a, long b)
        {
            return a / b;
        }
        
        // ====================================================================
        // Console I/O Functions
        // ====================================================================
        
        /// <summary>
        /// Print to console (VB-style Print)
        /// </summary>
        public static void Print(object value)
        {
            Console.Write(value);
        }
        
        /// <summary>
        /// Print line to console (VB-style Print with newline)
        /// </summary>
        public static void PrintLine(object value = null)
        {
            if (value != null)
                Console.WriteLine(value);
            else
                Console.WriteLine();
        }
        
        /// <summary>
        /// Read input from console
        /// </summary>
        public static string Input()
        {
            return Console.ReadLine();
        }
        
        /// <summary>
        /// Read input with prompt
        /// </summary>
        public static string Input(string prompt)
        {
            Console.Write(prompt);
            return Console.ReadLine();
        }
    }
    
    /// <summary>
    /// Exception types for BasicLang runtime
    /// </summary>
    public class BasicLangRuntimeException : Exception
    {
        public BasicLangRuntimeException(string message) : base(message) { }
        public BasicLangRuntimeException(string message, Exception inner) : base(message, inner) { }
    }
}
