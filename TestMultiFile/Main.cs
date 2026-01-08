using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace GeneratedCode
{
    public static class Program
    {
        public static void Main()
        {
            int sum = 0;
            int product = 0;
            string greeting = "";

            sum = MathUtils.Add(5, 3);
            Console.WriteLine("Sum = " + MathUtils.Add(5, 3));
            product = MathUtils.Multiply(4, 7);
            Console.WriteLine("Product = " + product);
            greeting = StringUtils.Concat("Hello ", "World");
            StringUtils.PrintMessage(greeting);
        }

    }

    public static class MathUtils
    {
        public static int Add(int a, int b)
        {
            return a + b;
        }

        public static int Multiply(int a, int b)
        {
            return a * b;
        }

        private static int InternalHelper(int x)
        {
            return x * 2;
        }

        public static int DoubleValue(int x)
        {
            return InternalHelper(x);
        }

    }

    public static class StringUtils
    {
        public static string Concat(string a, string b)
        {
            return a + b;
        }

        public static int GetLength(string s)
        {
            return s.Length;
        }

        public static void PrintMessage(string msg)
        {
            Console.WriteLine(msg);
        }

    }

}

