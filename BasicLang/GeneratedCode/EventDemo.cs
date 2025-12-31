using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GeneratedCode
{
    public delegate void ClickHandler(object sender, int x, int y);

    public delegate void MessageHandler(string message);

    public class Button
    {
        private string _text;

        public Button(string text)
        {
            _text = text;
        }

        public string Text
        {
            get
            {
                return _text;
            }
            set
            {
                _text = value;
            }
        }

        public event ClickHandler Click;
        public event MessageHandler MouseEnter;

        public void OnClick(int x, int y)
        {
            Console.WriteLine(((((("Button '" + _text) + "' clicked at (") + x) + ", ") + y) + ")");
        }

        public void OnMouseEnter()
        {
            Console.WriteLine(("Mouse entered button '" + _text) + "'");
        }

    }

    public class EventDemo
    {
        public static void Main()
        {
            Button btn = null;

            btn = new Button("Submit");
            Console.WriteLine("Button text: " + btn.Text);
        }

    }
}

