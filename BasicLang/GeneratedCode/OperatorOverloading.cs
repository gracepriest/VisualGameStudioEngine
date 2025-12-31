using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GeneratedCode
{
    public class OperatorOverloading
    {
        public static void Main()
        {
            Vector2D v1 = null;
            Vector2D v2 = null;

            v1 = new Vector2D(3, 4);
            v2 = new Vector2D(1, 2);
            Console.WriteLine("Vector2D class with operators +, -, * defined");
            Console.WriteLine("v1 = " + v1.ToString());
            Console.WriteLine("v2 = " + v2.ToString());
        }

    }
}

namespace MathLibrary
{
    public class Vector2D
    {
        private double _x;
        private double _y;

        public Vector2D(double x, double y)
        {
            _x = x;
            _y = y;
        }

        public double X
        {
            get
            {
                return _x;
            }
            set
            {
                _x = value;
            }
        }

        public double Y
        {
            get
            {
                return _y;
            }
            set
            {
                _y = value;
            }
        }

        public static Vector2D operator +(Vector2D left, Vector2D right)
        {
            return new Vector2D(left.X + right.X, left.Y + right.Y);
        }

        public static Vector2D operator -(Vector2D left, Vector2D right)
        {
            return new Vector2D(left.X - right.X, left.Y - right.Y);
        }

        public static Vector2D operator *(Vector2D vec, double scalar)
        {
            return new Vector2D(vec.X * scalar, vec.Y * scalar);
        }

        public virtual string ToString()
        {
            return ((("(" + _x) + ", ") + _y) + ")";
        }

    }

}

