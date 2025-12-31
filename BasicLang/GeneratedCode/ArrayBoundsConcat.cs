using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GeneratedCode
{
    public class ArrayBoundsConcat
    {
        public static void Main()
        {
            int[] arr = new int[5];
            int lower = 0;
            int upper = 0;
            string greeting = "";
            string name = "";
            string message = "";

            lower = 0;
            upper = (arr.Length - 1);
            name = "World";
            greeting = "Hello, " + "World" + "!";
            Console.WriteLine(lower);
            Console.WriteLine(upper);
            Console.WriteLine(greeting);
        }

    }
}

