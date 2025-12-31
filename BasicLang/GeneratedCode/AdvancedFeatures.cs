using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GeneratedCode
{
    public class Stack<T>
    {
        private T items;
        private int count;

        public Stack()
        {
            count = 0;
        }

        public void Push(T item)
        {
        }

        public T Pop()
        {
            return items(count - 1);
        }

        public bool IsEmpty()
        {
            return count == 0;
        }

    }

    public class AdvancedFeatures
    {
        public static T Max<T>(T a, T b)
        {
            if (a > b)
            {
                return a;
            }
            else
            {
                return b;
            }
        }

        public static void ProcessData(int value)
        {
            // Begin try block
            if (value < 0)
            {
                // Throw exception
            }
            Console.WriteLine("Processing: " + Str(value));
        }

        public static void Main()
        {
            Stack intStack = null;
            int maxVal = 0;

            Console.WriteLine("=== Generics Demo ===");
            intStack = new Stack();
            Console.WriteLine("Popped: " + Str(intStack.Pop()));
            maxVal = Math.Max(42, 17);
            Console.WriteLine("Max value: " + Str(maxVal));
            Console.WriteLine("");
            Console.WriteLine("=== Exception Handling Demo ===");
            ProcessData(100);
            ProcessData(-5);
            Console.WriteLine("");
            Console.WriteLine("=== Lambda Demo ===");
            Console.WriteLine("Lambda syntax supported: Function(x) x * 2");
        }

    }
}

