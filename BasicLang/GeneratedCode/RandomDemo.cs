using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GeneratedCode
{
    public class RandomDemo
    {
        public static void Main()
        {
            int i = 0;
            double rand = 0.0;
            int diceRoll = 0;

            Console.WriteLine("Random numbers (0 to 1):");
            i = 1;
            while (i <= 5)
            {
                rand = Random.Shared.NextDouble();
                Console.WriteLine(rand);
                i = 2;
            }
            Console.WriteLine("Simulated dice rolls:");
            i = 1;
            while (i <= 6)
            {
                diceRoll = Convert.ToInt32(Math.Floor(Random.Shared.NextDouble() * 6)) + 1;
                Console.WriteLine(diceRoll);
                i = 2;
            }
        }

    }
}

