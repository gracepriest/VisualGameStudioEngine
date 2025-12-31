using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GeneratedCode
{
    public class PatternMatching
    {
        public static void Main()
        {
            int day = 0;
            string command = "";

            day = 3;
            switch (day)
            {
                case 1:
                    Console.WriteLine("Monday");
                    break;
                case 2:
                    Console.WriteLine("Tuesday");
                    break;
                case 3:
                    Console.WriteLine("Wednesday");
                    break;
                case 4:
                    Console.WriteLine("Thursday");
                    break;
                case 5:
                    Console.WriteLine("Friday");
                    break;
                case 6:
                    Console.WriteLine("Saturday");
                    break;
                case 7:
                    Console.WriteLine("Sunday");
                    break;
                default:
                    Console.WriteLine("Invalid day");
                    break;
            }
            command = "save";
            switch (command)
            {
                case "open":
                    Console.WriteLine("Opening file...");
                    break;
                case "save":
                    Console.WriteLine("Saving file...");
                    break;
                case "close":
                    Console.WriteLine("Closing file...");
                    break;
                default:
                    Console.WriteLine("Unknown command");
                    break;
            }
        }

    }
}

