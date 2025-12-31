using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GeneratedCode
{
    public interface IShape
    {
        double GetArea();
        double GetPerimeter();
    }

    public enum Color
    {
        Red = 0,
        Green = 5,
        Blue = 6
    }

    public enum DayOfWeek : int
    {
        Sunday = 0,
        Monday = 1,
        Tuesday = 2,
        Wednesday = 3,
        Thursday = 4,
        Friday = 5,
        Saturday = 6
    }

    public delegate int MathOperation(int a, int b);

    public delegate void EventHandler(object sender, string args);

    public class Circle
    {
        private double _radius;

        public Circle(double radius)
        {
            _radius = radius;
        }

        public double GetArea()
        {
            return (3.14159 * _radius) * _radius;
        }

        public double GetPerimeter()
        {
            return (2 * 3.14159) * _radius;
        }

    }

    public class InterfacesEnumsDelegates
    {
        public static void Main()
        {
            Circle circle = null;

            circle = new Circle(5);
            Console.WriteLine("Circle area: " + circle.GetArea());
            Console.WriteLine("Circle perimeter: " + circle.GetPerimeter());
        }

    }
}

