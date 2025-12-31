using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GeneratedCode
{
    public class Person
    {
        public string Name;
        public int Age;

        public Person(string n, int a)
        {
            Name = n;
            Age = a;
        }

    }

    public class OptionalParamArray
    {
        public static string Greet(string name, string greeting = "Hello", bool excited = false)
        {
            string result = "";

            result = greeting + ", " + name;
            if (excited)
            {
                result = result + "!";
            }
            return result;
        }

        public static int Sum(params int[] numbers)
        {
            return 0;
        }

        public static void Increment(ref int value)
        {
            value = value + 1;
        }

        public static void Main()
        {
            int result = 0;
            int counter = 0;

            Console.WriteLine("=== Optional Parameters Demo ===");
            Console.WriteLine(Greet("World"));
            Console.WriteLine(Greet("User", "Hi"));
            Console.WriteLine(Greet("Developer", "Welcome", true));
            Console.WriteLine("");
            Console.WriteLine("=== ParamArray Demo ===");
            result = Sum(1, 2, 3);
            Console.WriteLine(result);
            Console.WriteLine("");
            Console.WriteLine("=== ByRef Parameter Demo ===");
            counter = 10;
            Console.WriteLine(10);
            Increment(ref 10);
            Console.WriteLine(10);
        }

    }
}

