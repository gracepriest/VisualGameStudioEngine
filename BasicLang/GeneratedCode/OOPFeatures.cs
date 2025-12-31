using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GeneratedCode
{
    public class Person
    {
        private string _name;
        private int _age;

        public Person(string name, int age)
        {
            _name = name;
            _age = age;
        }

        public string Name
        {
            get
            {
                return _name;
            }
            set
            {
                _name = value;
            }
        }

        public int Age
        {
            get
            {
                return _age;
            }
            set
            {
                _age = value;
            }
        }

        public string GetInfo()
        {
            return ((this.Name + " is ") + Convert.ToString(this.Age)) + " years old";
        }

        public virtual string Greet()
        {
            return "Hello, I am " + _name;
        }

        public static Person CreateDefault()
        {
            return new Person("Unknown", 0);
        }

    }

    public class Employee : Person
    {
        private string _department;

        public Employee(string name, int age, string dept) : base(name, age)
        {
            _department = dept;
        }

        public string Department
        {
            get
            {
                return _department;
            }
            set
            {
                _department = value;
            }
        }

        public override string Greet()
        {
            return (base.Greet() + " from ") + _department;
        }

    }

    public class OOPFeatures
    {
        public static void Main()
        {
            Person person = null;
            Employee employee = null;
            Person defaultPerson = null;

            person = new Person("John", 30);
            employee = new Employee("Jane", 25, "Engineering");
            Console.WriteLine(person.Name);
            Console.WriteLine(person.Age);
            Console.WriteLine(person.GetInfo());
            person.Name = "Johnny";
            Console.WriteLine(person.Name);
            Console.WriteLine(employee.Department);
            Console.WriteLine(employee.Greet());
            defaultPerson = Person.CreateDefault();
        }

    }
}

