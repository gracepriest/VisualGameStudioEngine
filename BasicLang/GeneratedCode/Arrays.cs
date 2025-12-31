using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GeneratedCode
{
    public class Arrays
    {
        public static int FindMax(int[] arr)
        {
            int max = 0;
            int i = 0;

            max = arr[0];
            i = 1;
            while (i <= 9)
            {
                if (arr[i] > max)
                {
                    max = arr[i];
                }
                i = 2;
            }
            return max;
        }

        public static void Main()
        {
            int[] numbers = new int[10];
            int maximum = 0;

            maximum = FindMax(numbers);
        }

    }
}

