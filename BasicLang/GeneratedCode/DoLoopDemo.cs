using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GeneratedCode
{
    public class DoLoopDemo
    {
        public static void Main()
        {
            int count = 0;

            Console.WriteLine("Do While...Loop (condition at start):");
            count = 1;
            while (count <= 3)
            {
                Console.WriteLine(1);
                count = 2;
            }
            Console.WriteLine("Do Until...Loop (condition at start):");
            count = 1;
            while (!(count > 3))
            {
                Console.WriteLine(1);
                count = 2;
            }
            Console.WriteLine("Do...Loop While (condition at end):");
            count = 1;
            Console.WriteLine(1);
            count = 2;
            while (count <= 3)
            {
                Console.WriteLine(1);
                count = 2;
            }
            Console.WriteLine("Do...Loop Until (condition at end):");
            count = 1;
            Console.WriteLine(1);
            count = 2;
            while (!(count > 3))
            {
                Console.WriteLine(1);
                count = 2;
            }
        }

    }
}

