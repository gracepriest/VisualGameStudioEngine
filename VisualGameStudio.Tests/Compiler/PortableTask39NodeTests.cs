using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task 39's node tier: the portable Timer and the strip/item host contract. The recording DOM
/// supplies the page elements; the library itself is compiled through the real project route.
/// </summary>
[TestFixture]
[Category("Integration")]
public class PortableTask39NodeTests
{
    [Test]
    public void Timer_StartsStopsAndRestartsOnlyWhileEnabled()
    {
        const string setup = """
            globalThis.__state.timers = [];
            globalThis.__state.fireTimer = function () {
                const timer = globalThis.__state.timers.find(t => !t.cleared);
                if (timer) timer.handler();
            };
            globalThis.__state.activeTimers = function () {
                return globalThis.__state.timers.filter(t => !t.cleared).length;
            };
            globalThis.__state.totalTimers = function () {
                return globalThis.__state.timers.length;
            };
            globalThis.setInterval = function (handler, milliseconds) {
                globalThis.__state.timers.push({ handler, milliseconds, cleared: false });
                return globalThis.__state.timers.length;
            };
            globalThis.clearInterval = function (handle) {
                const timer = globalThis.__state.timers[handle - 1];
                if (timer) timer.cleared = true;
            };
            """;

        var result = PortableControlNodeTests.Run("""
            Using System
            Using System.Windows.Forms
            Module Program
                Dim ticks As Integer

                Sub Main()
                    Dim f As New Form()
                    Dim timer As New Timer()
                    timer.Interval = 40
                    AddHandler timer.Tick, AddressOf HandleTick
                    Dim state As Object = ::__state
                    Console.WriteLine("initial=" & CStr(timer.Enabled))
                    timer.Start()
                    Console.WriteLine("started=" & CStr(timer.Enabled))
                    state.fireTimer()
                    Console.WriteLine("ticks=" & CStr(ticks))
                    timer.Interval = 50
                    Console.WriteLine("active=" & CStr(state.activeTimers()))
                    Console.WriteLine("total=" & CStr(state.totalTimers()))
                    state.fireTimer()
                    Console.WriteLine("ticks=" & CStr(ticks))
                    timer.Stop()
                    Console.WriteLine("stopped=" & CStr(timer.Enabled))
                    state.fireTimer()
                    Console.WriteLine("ticks=" & CStr(ticks))
                End Sub

                Sub HandleTick(sender As Object, e As EventArgs)
                    ticks = ticks + 1
                End Sub
            End Module
            """, setup);

        Assert.That(result.Ok, Is.True, result.Errors);
        Assert.That(result.Output, Is.EqualTo(
            "initial=False\nstarted=True\nticks=1\nactive=1\ntotal=2\nticks=2\nstopped=False\nticks=2"));
    }

    [Test]
    public void StripsAndItems_AttachInDocumentOrderAndRaiseClickedItem()
    {
        const string setup = """
            const __menu = __make("menu", "NAV");
            __menu.className = "vgs-MenuStrip";
            __menu.setAttribute("data-vgs-control", "Docked");
            const __menuList = __make("menuList", "UL");
            __menu.appendChild(__menuList);
            const __file = __make("file", "LI");
            __file.className = "vgs-ToolStripMenuItem";
            __file.setAttribute("data-vgs-control", "Item");
            __file.textContent = "File";
            __menuList.appendChild(__file);
            const __fileList = __make("fileList", "UL");
            __file.appendChild(__fileList);
            const __open = __make("open", "LI");
            __open.className = "vgs-ToolStripMenuItem";
            __open.setAttribute("data-vgs-control", "Item");
            __open.textContent = "Open";
            __fileList.appendChild(__open);
            __form.appendChild(__menu);

            const __tool = __make("tool", "MENU");
            __tool.className = "vgs-ToolStrip";
            __tool.setAttribute("data-vgs-control", "Docked");
            const __save = __make("save", "INPUT");
            __save.type = "button";
            __save.className = "vgs-ToolStripButton";
            __save.setAttribute("data-vgs-control", "Item");
            __save.value = "Save";
            __tool.appendChild(__save);
            __form.appendChild(__tool);

            const __status = __make("status", "FOOTER");
            __status.className = "vgs-StatusStrip";
            __status.setAttribute("data-vgs-control", "Docked");
            const __ready = __make("ready", "SPAN");
            __ready.className = "vgs-ToolStripStatusLabel";
            __ready.setAttribute("data-vgs-control", "Item");
            __ready.textContent = "Ready";
            __status.appendChild(__ready);
            __form.appendChild(__status);
            """;

        var result = PortableControlNodeTests.Run("""
            Using System
            Using System.Windows.Forms
            Module Program
                Sub Main()
                    Dim f As New Form()

                    Dim menu As New MenuStrip()
                    menu.Name = "menu"
                    f.Controls.Add(menu)
                    Dim file As New ToolStripMenuItem()
                    file.Name = "file"
                    menu.Items.Add(file)
                    Dim open As New ToolStripMenuItem()
                    open.Name = "open"
                    file.DropDownItems.Add(open)

                    Dim tool As New ToolStrip()
                    tool.Name = "tool"
                    f.Controls.Add(tool)
                    Dim save As New ToolStripButton()
                    save.Name = "save"
                    tool.Items.Add(save)

                    Dim status As New StatusStrip()
                    status.Name = "status"
                    f.Controls.Add(status)
                    Dim ready As New ToolStripStatusLabel()
                    ready.Name = "ready"
                    status.Items.Add(ready)

                    AddHandler file.Click, AddressOf HandleFile
                    AddHandler open.Click, AddressOf HandleOpen
                    AddHandler save.Click, AddressOf HandleSave
                    AddHandler ready.Click, AddressOf HandleReady
                    AddHandler menu.ItemClicked, AddressOf HandleMenuClicked
                    AddHandler tool.ItemClicked, AddressOf HandleToolClicked
                    AddHandler status.ItemClicked, AddressOf HandleStatusClicked

                    file.Text = "&File"
                    save.Text = "&Save"
                    ready.Text = "Ready now"
                    Console.WriteLine("text=" & file.Text)
                    Console.WriteLine("button=" & save.Text)
                    Console.WriteLine("status=" & ready.Text)
                    Console.WriteLine("order=" & menu.Items.Item(0).Name & "," & file.DropDownItems.Item(0).Name)
                    Console.WriteLine("counts=" & CStr(menu.Items.Count) & "," & CStr(tool.Items.Count) & "," & CStr(status.Items.Count))
                    Dim fileEl As Element = ::document.getElementById("file")
                    Console.WriteLine("submenu=" & fileEl.firstElementChild.tagName)
                    Console.WriteLine("own=" & CStr(fileEl.firstChild.nodeType))

                    Dim openEl As Element = ::document.getElementById("open")
                    openEl.click()
                    ::document.getElementById("file").click()
                    ::document.getElementById("save").click()
                    ::document.getElementById("ready").click()
                End Sub

                Sub HandleFile(sender As Object, e As EventArgs)
                    Console.WriteLine("file")
                End Sub

                Sub HandleOpen(sender As Object, e As EventArgs)
                    Console.WriteLine("open")
                End Sub

                Sub HandleSave(sender As Object, e As EventArgs)
                    Console.WriteLine("save")
                End Sub

                Sub HandleReady(sender As Object, e As EventArgs)
                    Console.WriteLine("ready")
                End Sub

                Sub HandleMenuClicked(sender As Object, e As ToolStripItemClickedEventArgs)
                    Console.WriteLine("menu=" & e.ClickedItem.Name)
                End Sub

                Sub HandleToolClicked(sender As Object, e As ToolStripItemClickedEventArgs)
                    Console.WriteLine("tool=" & e.ClickedItem.Name)
                End Sub

                Sub HandleStatusClicked(sender As Object, e As ToolStripItemClickedEventArgs)
                    Console.WriteLine("status=" & e.ClickedItem.Name)
                End Sub
            End Module
            """, setup);

        Assert.That(result.Ok, Is.True, result.Errors);
        Assert.That(result.Output, Is.EqualTo(
            "text=File\nbutton=Save\nstatus=Ready now\norder=file,open\ncounts=1,1,1\n" +
            "submenu=UL\nown=3\n" +
            "open\nmenu=open\nfile\nmenu=file\nsave\ntool=save\nready\nstatus=ready"));
    }
}
