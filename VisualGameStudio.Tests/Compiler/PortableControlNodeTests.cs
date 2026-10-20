using System.IO;
using System.Linq;
using BasicLang.Compiler;
using BasicLang.Compiler.CodeGen.JavaScript;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task 29 — the portable Control/Form library, compiled through the real project route
/// (so the Web Forms inclusion hook supplies the library) and RUN under Node against a
/// recording stub DOM. These are behavioural tests, not assertions on generated text.
/// </summary>
[TestFixture]
[Category("Integration")]
public class PortableControlNodeTests
{
    private const string StubDom = """
        globalThis.__state = {
            els: Object.create(null),
            alerts: [],
            confirms: [],
            confirmAnswer: true,
            title: "base title"
        };

        class StubClassList {
            constructor(el) { this.el = el; this.names = new Set(); }
            add(...names) { for (const name of names) this.names.add(name); this.sync(); }
            remove(...names) { for (const name of names) this.names.delete(name); this.sync(); }
            contains(name) { return this.names.has(name); }
            toggle(name, force) {
                const on = force === undefined ? !this.names.has(name) : Boolean(force);
                if (on) this.names.add(name); else this.names.delete(name);
                this.sync();
                return on;
            }
            sync() { this.el.className = Array.from(this.names).join(" "); }
        }

        class StubElement {
            constructor(id, tagName) {
                this.id = id || "";
                this.tagName = tagName || "DIV";
                this.textContent = "";
                this.innerHTML = "";
                this.innerText = "";
                this.value = "";
                this.checked = false;
                this.disabled = false;
                this.hidden = false;
                this.parentElement = null;
                this.children = [];
                this.style = {};
                this.attributes = Object.create(null);
                this.listeners = Object.create(null);
                this.classList = new StubClassList(this);
                this.className = "";
                this.focused = false;
            }
            get firstElementChild() { return this.children.length === 0 ? null : this.children[0]; }
            get nextElementSibling() {
                if (this.parentElement === null) return null;
                const siblings = this.parentElement.children;
                const index = siblings.indexOf(this);
                return index >= 0 && index + 1 < siblings.length ? siblings[index + 1] : null;
            }
            addEventListener(type, listener) {
                (this.listeners[type] ||= []).push(listener);
            }
            removeEventListener(type, listener) {
                const list = this.listeners[type];
                if (!list) return;
                const index = list.indexOf(listener);
                if (index >= 0) list.splice(index, 1);
            }
            dispatch(type, props) {
                const event = Object.assign({
                    type,
                    target: this,
                    currentTarget: this,
                    detail: 1,
                    button: 0,
                    clientX: 0,
                    clientY: 0,
                    key: "",
                    code: "",
                    shiftKey: false,
                    ctrlKey: false,
                    altKey: false,
                    relatedTarget: null,
                    defaultPrevented: false,
                    preventDefault() { this.defaultPrevented = true; }
                }, props || {});
                for (const listener of (this.listeners[type] || []).slice()) listener(event);
                return event;
            }
            click() { return this.dispatch("click", { detail: 1 }); }
            fire(type, button, x, y, key, code, shift, control, alt, relatedId) {
                return this.dispatch(type, {
                    button: button || 0,
                    clientX: x || 0,
                    clientY: y || 0,
                    key: key || "",
                    code: code || "",
                    shiftKey: Boolean(shift),
                    ctrlKey: Boolean(control),
                    altKey: Boolean(alt),
                    relatedTarget: relatedId ? globalThis.__state.els[relatedId] : null
                });
            }
            focus() {
                this.focused = true;
                this.dispatch("focusin", { relatedTarget: null });
            }
            getAttribute(name) {
                return Object.prototype.hasOwnProperty.call(this.attributes, name)
                    ? this.attributes[name]
                    : null;
            }
            setAttribute(name, value) { this.attributes[name] = String(value); }
            removeAttribute(name) { delete this.attributes[name]; }
            appendChild(child) {
                if (child.parentElement !== null) child.parentElement.removeChild(child);
                this.children.push(child);
                child.parentElement = this;
                return child;
            }
            removeChild(child) {
                const index = this.children.indexOf(child);
                if (index >= 0) this.children.splice(index, 1);
                child.parentElement = null;
                return child;
            }
            remove() {
                if (this.parentElement !== null) this.parentElement.removeChild(this);
            }
            contains(other) {
                if (other === null || other === undefined) return false;
                if (other === this) return true;
                return this.children.some(child => child.contains(other));
            }
        }

        function __make(id, tagName) {
            const element = new StubElement(id, tagName);
            globalThis.__state.els[id] = element;
            return element;
        }

        const __form = __make("formArea", "DIV");
        __form.className = "vgs-form";
        const __panel = __make("panel", "DIV");
        __form.appendChild(__panel);
        const __btn = __make("btn", "BUTTON");
        __panel.appendChild(__btn);
        const __plain = __make("plain", "DIV");
        __panel.appendChild(__plain);
        const __other = __make("other", "BUTTON");
        __form.appendChild(__other);
        const __outside = __make("outside", "DIV");
        const __inner = __make("inner", "SPAN");
        __btn.appendChild(__inner);

        globalThis.document = {
            body: __form,
            getElementById(id) { return globalThis.__state.els[id] || null; },
            querySelector(selector) { return selector === ".vgs-form" ? __form : null; },
            createElement(tagName) { return new StubElement("", tagName); },
            createTextNode(data) { const element = new StubElement("", "TEXT"); element.textContent = data; return element; }
        };
        Object.defineProperty(globalThis.document, "title", {
            get() { return globalThis.__state.title; },
            set(value) { globalThis.__state.title = String(value); }
        });
        globalThis.getComputedStyle = element => element.style;
        globalThis.window = globalThis;
        globalThis.alert = message => {
            globalThis.__state.alerts.push(String(message));
            console.log("alert:" + String(message));
        };
        globalThis.confirm = message => {
            globalThis.__state.confirms.push(String(message));
            console.log("confirm:" + String(message));
            return globalThis.__state.confirmAnswer;
        };
        """;

    private static (bool Ok, string Output, string Errors) Run(string source)
    {
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "bl-portable-" + Path.GetRandomFileName())).FullName;
        try
        {
            var file = Path.Combine(dir, "Main.bas");
            File.WriteAllText(file, source);
            var result = new BasicCompiler(new CompilerOptions { TargetBackend = "javascript" }).CompileProjectFiles(new[] { file });
            if (result.HasErrors) return (false, "", string.Join(" | ", result.AllErrors.Select(e => e.Message)));
            var js = new JavaScriptCodeGenerator().Generate(result.CombinedIR!);
            var (exitCode, stdout, stderr) = JavaScriptExecutionTests.RunNodeScriptForOutcome(StubDom + js);
            return (exitCode == 0, FourBackends.Norm(stdout), FourBackends.Norm(stderr));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Test]
    public void AttachTextClick_IsTheCorePath()
    {
        var source = """
            Using System
            Using System.Windows.Forms
            Module Program
                Sub Main()
                    Dim f As New Form()
                    Dim b As New Button()
                    b.Name = "btn"
                    f.Controls.Add(b)
                    b.Text = "hello"
                    Console.WriteLine(b.Text)
                    AddHandler b.Click, AddressOf HandleClick
                    Dim doc As Document = ::document
                    doc.getElementById("btn").click()
                    doc.getElementById("btn").click()
                End Sub
                Sub HandleClick(sender As Object, e As EventArgs)
                    Console.WriteLine("clicked")
                End Sub
            End Module
            """;

        var result = Run(source);
        Assert.That(result.Ok, Is.True, result.Errors);
        Assert.That(result.Output, Is.EqualTo("hello\nclicked\nclicked"));
    }

    [Test]
    public void UnattachedAccess_NamesTheMemberAndThrowsInvalidOperationException()
    {
        var result = Run("""
            Using System
            Using System.Windows.Forms
            Module Program
                Sub Main()
                    Dim b As New Button()
                    Try
                        b.Text = "x"
                    Catch ex As InvalidOperationException
                        Console.WriteLine("caught")
                        Console.WriteLine(ex.Message)
                    End Try
                End Sub
            End Module
            """);

        Assert.That(result.Ok, Is.True, result.Errors);
        Assert.That(result.Output, Does.StartWith("caught\n"));
        Assert.That(result.Output, Does.Contain("'Text'"));
        Assert.That(result.Output, Does.Contain("Controls collection"));
    }

    [Test]
    public void Text_IsLiteralTextContent_AndTextChangedIsChangeOnly()
    {
        var result = Run("""
            Using System
            Using System.Windows.Forms
            Module Program
                Dim changes As Integer
                Sub Main()
                    Dim f As New Form()
                    Dim b As New Button()
                    b.Name = "btn"
                    f.Controls.Add(b)
                    AddHandler b.TextChanged, AddressOf HandleText
                    b.Text = "same"
                    b.Text = "same"
                    b.Text = "<b>x</b>"
                    Console.WriteLine("literal=" & b.Text)
                    Console.WriteLine("markup=" & b.Element.innerHTML)
                    Console.WriteLine("changes=" & changes)
                End Sub
                Sub HandleText(sender As Object, e As EventArgs)
                    changes = changes + 1
                End Sub
            End Module
            """);

        Assert.That(result.Ok, Is.True, result.Errors);
        Assert.That(result.Output, Is.EqualTo("literal=<b>x</b>\nmarkup=\nchanges=2"));
    }

    [Test]
    public void EnabledAndVisible_PropagateThroughAncestorsAndDescendants()
    {
        var result = Run("""
            Using System
            Using System.Windows.Forms
            Module Program
                Sub Main()
                    Dim f As New Form()
                    Dim p As New Panel()
                    p.Name = "panel"
                    Dim c As New Button()
                    c.Name = "btn"
                    p.Controls.Add(c)
                    f.Controls.Add(p)
                    Dim doc As Document = ::document
                    p.Enabled = False
                    Console.WriteLine("child=" & CStr(c.Enabled))
                    Console.WriteLine("panel=" & doc.getElementById("panel").getAttribute("aria-disabled"))
                    Console.WriteLine("button=" & CStr(doc.getElementById("btn").disabled))
                    c.Enabled = True
                    Console.WriteLine("own=" & CStr(c.Enabled))
                    p.Enabled = True
                    Console.WriteLine("restored=" & CStr(c.Enabled))
                    Console.WriteLine("button2=" & CStr(doc.getElementById("btn").disabled))
                    p.Enabled = False
                    c.Enabled = False
                    p.Enabled = True
                    Console.WriteLine("explicitOff=" & CStr(c.Enabled))
                    p.Visible = False
                    Console.WriteLine("childHidden=" & CStr(c.Visible))
                    Console.WriteLine("ownHidden=" & CStr(doc.getElementById("btn").hidden))
                    c.Visible = False
                    p.Visible = True
                    Console.WriteLine("explicitHidden=" & CStr(c.Visible))
                    c.Visible = True
                    p.Visible = False
                    p.Visible = True
                    Console.WriteLine("childShown=" & CStr(c.Visible))
                End Sub
            End Module
            """);

        Assert.That(result.Ok, Is.True, result.Errors);
        Assert.That(result.Output, Is.EqualTo(
            "child=False\npanel=true\nbutton=True\nown=False\nrestored=True\nbutton2=False\n" +
            "explicitOff=False\nchildHidden=False\nownHidden=False\nexplicitHidden=False\nchildShown=True"));
    }

    [Test]
    public void ClickMouseKeysAndEnter_CarryTheDomValues()
    {
        var result = Run("""
            Using System
            Using System.Windows.Forms
            Module Program
                Dim clicks As Integer
                Dim enters As Integer
                Sub Main()
                    Dim f As New Form()
                    Dim b As New Button()
                    b.Name = "btn"
                    f.Controls.Add(b)
                    AddHandler b.Click, AddressOf HandleClick
                    AddHandler b.MouseDown, AddressOf HandleMouse
                    AddHandler b.KeyDown, AddressOf HandleKeyDown
                    AddHandler b.KeyPress, AddressOf HandleKeyPress
                    AddHandler b.Enter, AddressOf HandleEnter
                    Dim doc As Document = ::document
                    Dim el As Object = doc.getElementById("btn")
                    el.fire("mousedown", 2, 11, 22, "", "", False, False, False, "")
                    el.fire("keydown", 0, 0, 0, "a", "KeyA", False, True, False, "")
                    el.fire("keydown", 0, 0, 0, "a", "KeyA", False, False, False, "")
                    el.fire("focusin", 0, 0, 0, "", "", False, False, False, "outside")
                    el.fire("focusin", 0, 0, 0, "", "", False, False, False, "inner")
                    el.click()
                    Console.WriteLine("clicks=" & clicks & ";enters=" & enters)
                End Sub
                Sub HandleClick(sender As Object, e As EventArgs)
                    clicks = clicks + 1
                    Console.WriteLine("sender=" & CType(sender, Control).Name)
                    Console.WriteLine("click")
                End Sub
                Sub HandleMouse(sender As Object, e As MouseEventArgs)
                    Console.WriteLine("right=" & CStr(e.Button = MouseButtons.Right) & ",x=" & e.X & ",y=" & e.Y & ",lx=" & e.Location.X)
                End Sub
                Sub HandleKeyDown(sender As Object, e As KeyEventArgs)
                    If e.Control Then Console.WriteLine("keyA=" & CStr(e.KeyCode = Keys.A) & ",control=" & CStr(e.Control))
                End Sub
                Sub HandleKeyPress(sender As Object, e As KeyPressEventArgs)
                    Console.WriteLine("char=" & CStr(e.KeyChar))
                End Sub
                Sub HandleEnter(sender As Object, e As EventArgs)
                    enters = enters + 1
                End Sub
            End Module
            """);

        Assert.That(result.Ok, Is.True, result.Errors);
        Assert.That(result.Output, Is.EqualTo(
            "right=True,x=11,y=22,lx=11\nkeyA=True,control=True\nchar=a\nsender=btn\nclick\nclicks=1;enters=1"));
    }

    [Test]
    public void MessageBox_MapsButtonsAndPrefixesTheCaption()
    {
        var result = Run("""
            Using System
            Using System.Windows.Forms
            Module Program
                Sub Main()
                    Dim r As DialogResult
                    r = MessageBox.Show("hello")
                    If r = DialogResult.OK Then Console.WriteLine("ok")
                    r = MessageBox.Show("save?", "Caption", MessageBoxButtons.YesNo)
                    If r = DialogResult.Yes Then Console.WriteLine("yes")
                    Dim state As Object = ::__state
                    state.confirmAnswer = False
                    r = MessageBox.Show("save?", "Caption", MessageBoxButtons.YesNo)
                    If r = DialogResult.No Then Console.WriteLine("no")
                    r = MessageBox.Show("cancel?", "", MessageBoxButtons.OKCancel)
                    If r = DialogResult.Cancel Then Console.WriteLine("cancel")
                End Sub
            End Module
            """);

        Assert.That(result.Ok, Is.True, result.Errors);
        Assert.That(result.Output, Does.Contain("alert:hello"));
        Assert.That(result.Output, Does.Contain("ok"));
        Assert.That(result.Output, Does.Contain("confirm:Caption\nsave?"));
        Assert.That(result.Output, Does.Contain("yes"));
        Assert.That(result.Output, Does.Contain("no"));
        Assert.That(result.Output, Does.Contain("cancel"));
    }

    [Test]
    public void FormAttachesTextCloseAndShow()
    {
        var result = Run("""
            Using System
            Using System.Windows.Forms
            Module Program
                Sub Main()
                    Dim f As New Form()
                    f.Text = "Title X"
                    f.Close()
                    Dim doc As Document = ::document
                    Console.WriteLine("title=" & doc.title)
                    Console.WriteLine("closed=" & CStr(doc.querySelector(".vgs-form").hidden))
                    f.Show()
                    Console.WriteLine("shown=" & CStr(doc.querySelector(".vgs-form").hidden))
                End Sub
            End Module
            """);

        Assert.That(result.Ok, Is.True, result.Errors);
        Assert.That(result.Output, Is.EqualTo("title=Title X\nclosed=True\nshown=False"));
    }

    [Test]
    public void ControlsRemove_DetachesListenersAndTheElement()
    {
        var result = Run("""
            Using System
            Using System.Windows.Forms
            Module Program
                Dim hits As Integer
                Sub Main()
                    Dim f As New Form()
                    Dim b As New Button()
                    b.Name = "btn"
                    f.Controls.Add(b)
                    Console.WriteLine("count=" & f.Controls.Count)
                    Console.WriteLine("item=" & f.Controls.Item(0).Name)
                    AddHandler b.Click, AddressOf HandleClick
                    f.Controls.Remove(b)
                    Dim doc As Document = ::document
                    doc.getElementById("btn").click()
                    Console.WriteLine("after=" & f.Controls.Count & ",hits=" & hits)
                    If doc.getElementById("btn").parentElement Is Nothing Then
                        Console.WriteLine("detached")
                    Else
                        Console.WriteLine("still")
                    End If
                End Sub
                Sub HandleClick(sender As Object, e As EventArgs)
                    hits = hits + 1
                End Sub
            End Module
            """);

        Assert.That(result.Ok, Is.True, result.Errors);
        Assert.That(result.Output, Is.EqualTo("count=1\nitem=btn\nafter=0,hits=0\ndetached"));
    }

    [Test]
    public void ControlsAdd_RejectsAnElementOutsideAnAttachedParent()
    {
        var result = Run("""
            Using System
            Using System.Windows.Forms
            Module Program
                Sub Main()
                    Dim f As New Form()
                    Dim missing As New Button()
                    missing.Name = "missing"
                    Try
                        f.Controls.Add(missing)
                    Catch ex As InvalidOperationException
                        Console.WriteLine("missing")
                        Console.WriteLine(ex.Message)
                    End Try
                    Dim b As New Button()
                    b.Name = "outside"
                    Try
                        f.Controls.Add(b)
                    Catch ex As InvalidOperationException
                        Console.WriteLine("rejected")
                        Console.WriteLine(ex.Message)
                    End Try
                End Sub
            End Module
            """);

        Assert.That(result.Ok, Is.True, result.Errors);
        Assert.That(result.Output, Does.StartWith("missing\n"));
        Assert.That(result.Output, Does.Contain("has no element"));
        Assert.That(result.Output, Does.Contain("rejected"));
        Assert.That(result.Output, Does.Contain("not inside its parent"));
    }

    [Test]
    public void BringToFront_MovesTheElementToTheEndOfItsParent()
    {
        var result = Run("""
            Using System
            Using System.Windows.Forms
            Module Program
                Sub Main()
                    Dim f As New Form()
                    Dim p As New Panel()
                    p.Name = "panel"
                    Dim o As New Button()
                    o.Name = "other"
                    f.Controls.Add(p)
                    f.Controls.Add(o)
                    p.BringToFront()
                    Dim doc As Document = ::document
                    Dim nextEl As Element = doc.getElementById("other").nextElementSibling
                    If nextEl Is Nothing Then
                        Console.WriteLine("none")
                    Else
                        Console.WriteLine(nextEl.id)
                    End If
                End Sub
            End Module
            """);

        Assert.That(result.Ok, Is.True, result.Errors);
        Assert.That(result.Output, Is.EqualTo("panel"));
    }

    [Test]
    public void StyleBackedProperties_WriteThroughTheSingleSeam()
    {
        var result = Run("""
            Using System
            Using System.Windows.Forms
            Module Program
                Sub Main()
                    Dim f As New Form()
                    Dim b As New Button()
                    b.Name = "btn"
                    f.Controls.Add(b)
                    b.ForeColor = Color.Red
                    b.BackColor = Color.Black
                    Dim fnt As New Font()
                    fnt.Name = "Arial"
                    fnt.Size = 12
                    fnt.Bold = True
                    b.Font = fnt
                    b.Cursor = Cursors.Hand
                    Dim pad As New Padding()
                    pad.Top = 1
                    pad.Right = 2
                    pad.Bottom = 3
                    pad.Left = 4
                    b.Padding = pad
                    Dim el As Element = ::document.getElementById("btn")
                    Console.WriteLine("color=" & el.style.color)
                    Console.WriteLine("background=" & el.style.backgroundColor)
                    Console.WriteLine("family=" & el.style.fontFamily)
                    Console.WriteLine("size=" & el.style.fontSize)
                    Console.WriteLine("weight=" & el.style.fontWeight)
                    Console.WriteLine("style=" & el.style.fontStyle)
                    Console.WriteLine("cursor=" & el.style.cursor)
                    Console.WriteLine("padding=" & el.style.padding)
                    b.Hide()
                    Console.WriteLine("hidden=" & CStr(el.hidden))
                    b.Show()
                    b.Focus()
                    Dim raw As Object = el
                    Console.WriteLine("focused=" & CStr(raw.focused))
                End Sub
            End Module
            """);

        Assert.That(result.Ok, Is.True, result.Errors);
        Assert.That(result.Output, Is.EqualTo(
            "color=red\nbackground=black\nfamily=Arial\nsize=12pt\nweight=bold\nstyle=normal\n" +
            "cursor=pointer\npadding=1px 2px 3px 4px\nhidden=True\nfocused=True"));
    }
}
