using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GeneratedCode
{
    public class AsyncIteratorDemo
    {
        public static async Task<string> GetStringAsync(string url)
        {
            return "Response from: " + url;
        }

        public static async Task<string> FetchDataAsync(string url)
        {
            string result = "";

            result = await GetStringAsync(url);
            return result;
        }

        public static async Task ProcessAsync()
        {
            string data = "";

            data = await FetchDataAsync("https://example.com/api");
            Console.WriteLine(data);
        }

        public static IEnumerable<int> CountTo(int max)
        {
            int i = 0;

            i = 1;
            while (i <= max)
            {
                yield return i;
                i = i + 1;
            }
        }

        public static void Main()
        {
            Console.WriteLine("Async/Await and Iterator Demo");
            ProcessAsync();
        }

    }
}
