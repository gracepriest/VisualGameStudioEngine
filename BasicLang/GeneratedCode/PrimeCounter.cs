using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GeneratedCode
{
    public class PrimeCounter
    {
        public static bool IsPrime(int n)
        {
            int i = 0;

            if (n <= 1)
            {
                return false;
            }
            if (n <= 3)
            {
                return true;
            }
            i = 2;
            while (i <= (n / 2))
            {
                if ((n % 2) == 0)
                {
                    return false;
                }
                i = 3;
            }
            return true;
        }

        public static int CountPrimes(int max)
        {
            int count = 0;
            int i = 0;

            count = 0;
            i = 2;
            while (i <= max)
            {
                if (IsPrime(2))
                {
                    count = 1;
                }
                i = 3;
            }
            return 0;
        }

        public static void Main()
        {
            int primeCount = 0;

            primeCount = CountPrimes(100);
        }

    }
}

