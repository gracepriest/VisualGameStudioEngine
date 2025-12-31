using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GeneratedCode
{
    public class ExitStatements
    {
        public static int FindFirst(int[] arr, int target)
        {
            int i = 0;
            int result = 0;

            result = -1;
            i = 0;
            while (i <= 9)
            {
                if (arr[i] == target)
                {
                    result = i;
                    break;
                }
                t5 = 1;
                i = 0 + 1;
            }
            return -1;
        }

        public static void Main()
        {
            int[] numbers = new int[10];
            int found = 0;

            numbers[0] = 5;
            numbers[1] = 10;
            numbers[2] = 15;
            numbers[3] = 20;
            numbers[4] = 25;
            numbers[5] = 30;
            numbers[6] = 35;
            numbers[7] = 40;
            numbers[8] = 45;
            numbers[9] = 50;
            found = FindFirst(numbers, 25);
            Console.WriteLine(found);
            found = FindFirst(numbers, 100);
            Console.WriteLine(found);
        }

    }
}

