using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GeneratedCode
{
    public class StringOps
    {
        public static void Main()
        {
            string text = "";
            int length = 0;
            string upper = "";
            string lower = "";
            string sub1 = "";
            string sub2 = "";
            string sub3 = "";
            string trimmed = "";
            int pos = 0;
            string replaced = "";

            text = "  Hello World  ";
            trimmed = "  Hello World  ".Trim();
            length = trimmed.Length;
            upper = trimmed.ToUpper();
            lower = trimmed.ToLower();
            sub1 = trimmed.Substring(0, 5);
            sub2 = trimmed.Substring(trimmed.Length - 5);
            sub3 = trimmed.Substring(7 - 1, 5);
            pos = (trimmed.IndexOf("World") + 1);
            replaced = trimmed.Replace("World", "BasicLang");
            Console.WriteLine(trimmed);
            Console.WriteLine(length);
            Console.WriteLine(upper);
            Console.WriteLine(lower);
            Console.WriteLine(sub1);
            Console.WriteLine(sub2);
            Console.WriteLine(sub3);
            Console.WriteLine(pos);
            Console.WriteLine(replaced);
        }

    }
}

