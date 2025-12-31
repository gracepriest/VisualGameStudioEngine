using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GeneratedCode
{
    public class ConversionOps
    {
        public static void Main()
        {
            string strNum = "";
            int intVal = 0;
            double dblVal = 0.0;
            string strResult = "";
            bool boolVal = false;

            strNum = "42";
            intVal = Convert.ToInt32("42");
            dblVal = Convert.ToDouble("3.14159");
            strResult = Convert.ToString(intVal);
            boolVal = Convert.ToBoolean(1);
            Console.WriteLine(intVal);
            Console.WriteLine(dblVal);
            Console.WriteLine(strResult);
            Console.WriteLine(boolVal);
        }

    }
}

