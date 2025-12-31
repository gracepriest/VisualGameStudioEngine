using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GeneratedCode
{
    public class SimpleFunction
    {
        public static int Add(int a, int b)
        {
            return a + b;
        }

        public static void Main()
        {
            int result = 0;

            result = Add(5, 3);
            Console.WriteLine("The result is:");
            Console.WriteLine(result);
        }

    }
}

