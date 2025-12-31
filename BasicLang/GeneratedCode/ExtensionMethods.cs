using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GeneratedCode
{
    public class ExtensionMethods
    {
        public static double Squared(this double value)
        {
            return value * value;
        }

        public static int Doubled(this int value)
        {
            return value + value;
        }

        public static void Main()
        {
            int num = 0;
            double d = 0.0;

            num = 5;
            d = 3.5;
            Console.WriteLine("5 doubled: " + Doubled(5));
            Console.WriteLine("3.5 squared: " + Squared(3.5));
        }

    }
}

