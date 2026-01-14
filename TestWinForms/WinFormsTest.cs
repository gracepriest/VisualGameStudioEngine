using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
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
using System.Windows.Forms;

namespace GeneratedCode
{
    public static class WinFormsTest
    {
        public static void Main()
        {
            Form frm = default!;
            Button btn = default!;
            Label lbl = default!;

            frm = new Form();
            frm.Text = "My BasicLang WinForms App";
            frm.Size = new Size(400, 300);
            frm.StartPosition = FormStartPosition.CenterScreen;
            btn = new Button();
            btn.Text = "Click Me!";
            btn.Size = new Size(100, 30);
            btn.Location = new Point(150, 100);
            frm.Controls.Add(btn);
            lbl = new Label();
            lbl.Text = "Hello from BasicLang!";
            lbl.Location = new Point(130, 50);
            lbl.AutoSize = true;
            frm.Controls.Add(lbl);
            Application.Run(frm);
        }

    }

}

