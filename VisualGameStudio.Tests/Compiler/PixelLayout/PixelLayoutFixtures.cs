using System.Text;
using BasicLang.Forms;
using BasicLang.Forms.Serialization;
using NUnit.Framework;

namespace VisualGameStudio.Tests.Compiler.PixelLayout;

/// <summary>
/// The Canvas pages both reference harnesses lay out — Task 12 in a real WinForms window, Task 13 in Edge — so the
/// two are measured on the SAME documents. Each builder takes the design size, so a test can ask the model what a
/// form looks like at another size (a docked control re-docks at every size, in WinForms and on the page alike).
/// ⚠ Every form name is distinct: each becomes a class in one driver program.
/// </summary>
internal static class PixelLayoutFixtures
{
    public static FormDocument Read(string name, int width, int height, string controls)
    {
        var xml = $"""
            <WebForm Name="{name}" Version="1" Width="{width}" Height="{height}">
              <Layout Kind="Canvas"/>
              <Controls>
            {controls}
              </Controls>
              <Components/>
              <Resources/>
            </WebForm>
            """;

        var file = FormDocumentReader.Read(Path.Combine(Path.GetTempPath(), name + ".blwebform"), xml);
        Assert.That(file.IsRefused, Is.False,
            $"fixture {name} was refused: " + string.Join("; ", file.Diagnostics.Select(d => d.Format())));
        return file.Model;
    }

    /// <summary>The harness's own proof: one Top|Left control and one anchored Right.</summary>
    public static FormDocument SelfTest(int width = 400, int height = 300) => Read("SelfTest", width, height, """
        <Panel Id="p1" X="20" Y="20" Width="100" Height="50"/>
        <Panel Id="p2" X="280" Y="20" Width="100" Height="50" Anchor="Right"/>
        """);

    /// <summary>
    /// Every Anchor combination: control <c>a{n}</c> has the AnchorStyles flags <c>n</c> (Top 1, Bottom 2, Left 4,
    /// Right 8; 0 is None), 80×60 on a 4×4 grid.
    /// </summary>
    public static FormDocument Anchors(int width = 400, int height = 300)
    {
        var controls = new StringBuilder();
        for (var n = 0; n < 16; n++)
        {
            var (x, y, _, _) = AnchorCell(n);
            controls.AppendLine($"""<Panel Id="a{n}" X="{x}" Y="{y}" Width="80" Height="60" Anchor="{AnchorText(n)}"/>""");
        }

        return Read("Anchors", width, height, controls.ToString());
    }

    /// <summary>Control <c>a{n}</c>'s stored rectangle in <see cref="Anchors"/>.</summary>
    public static (int X, int Y, int Width, int Height) AnchorCell(int n) => (10 + (n % 4) * 95, 10 + (n / 4) * 70, 80, 60);

    public static FormAnchorEdges AnchorFlags(int n) => (FormAnchorEdges)n;

    public static string AnchorText(int n) =>
        n == 0
            ? "None"
            : string.Join(",", new[] { FormAnchorEdges.Top, FormAnchorEdges.Bottom, FormAnchorEdges.Left, FormAnchorEdges.Right }
                .Where(e => ((FormAnchorEdges)n).HasFlag(e)));

    /// <summary>A Fill between a MenuStrip and a StatusStrip (strips first in the document).</summary>
    public static FormDocument DockStrips(int width = 400, int height = 300) => Read("DockStrips", width, height, """
        <MenuStrip Id="menu" Dock="Top"><ToolStripMenuItem Id="mnuFile" Text="File"/></MenuStrip>
        <StatusStrip Id="status" Dock="Bottom"><ToolStripStatusLabel Id="lblReady" Text="Ready"/></StatusStrip>
        <Panel Id="fill" X="0" Y="0" Width="100" Height="100" Dock="Fill"/>
        """);

    /// <summary>A Dock=Top Panel BEFORE the MenuStrip in the document: it docks first, so the menu sits below it.</summary>
    public static FormDocument TopBeforeMenu(int width = 400, int height = 300) => Read("TopBeforeMenu", width, height, """
        <Panel Id="band" X="0" Y="0" Width="400" Height="40" Dock="Top"/>
        <MenuStrip Id="menu" Dock="Top"><ToolStripMenuItem Id="mnuFile" Text="File"/></MenuStrip>
        <Panel Id="below" X="20" Y="100" Width="100" Height="50"/>
        """);

    /// <summary>S9: an overflowing Dock=Top, then a Dock=Bottom, then a Fill.</summary>
    public static FormDocument OverflowV(int width = 400, int height = 300) => Read("OverflowV", width, height, """
        <Panel Id="top" X="0" Y="0" Width="400" Height="400" Dock="Top"/>
        <Panel Id="bottom" X="0" Y="0" Width="400" Height="50" Dock="Bottom"/>
        <Panel Id="fill" X="0" Y="0" Width="100" Height="100" Dock="Fill"/>
        """);

    /// <summary>S9: an overflowing Dock=Left, then a Dock=Right, then a Fill.</summary>
    public static FormDocument OverflowH(int width = 400, int height = 300) => Read("OverflowH", width, height, """
        <Panel Id="left" X="0" Y="0" Width="500" Height="300" Dock="Left"/>
        <Panel Id="right" X="0" Y="0" Width="50" Height="300" Dock="Right"/>
        <Panel Id="fill" X="0" Y="0" Width="100" Height="100" Dock="Fill"/>
        """);

    /// <summary>
    /// A docked Panel hidden at startup before a second docked Panel, and two ANCHORED siblings that must not move
    /// when it is shown at run time.
    /// </summary>
    public static FormDocument HiddenDock(bool pnlAVisible = false, int width = 400, int height = 300) =>
        Read("HiddenDock", width, height, $"""
            <Panel Id="pnlA" X="0" Y="0" Width="400" Height="40" Dock="Top" Visible="{(pnlAVisible ? "true" : "false")}"/>
            <Panel Id="pnlB" X="0" Y="0" Width="400" Height="60" Dock="Top"/>
            <Panel Id="pnlC" X="10" Y="150" Width="80" Height="40" Anchor="Top,Left"/>
            <Panel Id="pnlD" X="300" Y="240" Width="80" Height="40" Anchor="Bottom,Right"/>
            """);

    /// <summary>
    /// A borderless, a FixedSingle and a Fixed3D Panel and a GroupBox, each holding a Dock=Top child and a
    /// positioned child — the containers whose client area is NOT their bounds (plan spec-claims #11).
    /// </summary>
    public static FormDocument Bordered(int width = 400, int height = 300) => Read("Bordered", width, height, """
        <Panel Id="pNone" X="10" Y="10" Width="180" Height="100">
          <Panel Id="pNoneTop" X="0" Y="0" Width="180" Height="20" Dock="Top"/>
          <Panel Id="pNoneSub" X="10" Y="40" Width="50" Height="30"/>
        </Panel>
        <Panel Id="pSingle" X="200" Y="10" Width="180" Height="100" BorderStyle="FixedSingle">
          <Panel Id="pSingleTop" X="0" Y="0" Width="180" Height="20" Dock="Top"/>
          <Panel Id="pSingleSub" X="10" Y="40" Width="50" Height="30"/>
        </Panel>
        <Panel Id="p3D" X="10" Y="130" Width="180" Height="100" BorderStyle="Fixed3D">
          <Panel Id="p3DTop" X="0" Y="0" Width="180" Height="20" Dock="Top"/>
          <Panel Id="p3DSub" X="10" Y="40" Width="50" Height="30"/>
        </Panel>
        <GroupBox Id="grp" X="200" Y="130" Width="180" Height="100" Text="Group">
          <Panel Id="grpTop" X="0" Y="0" Width="180" Height="20" Dock="Top"/>
          <Panel Id="grpSub" X="10" Y="40" Width="50" Height="30"/>
        </GroupBox>
        """);

    /// <summary>
    /// A container hidden at startup holding a child: the child is not visible either (review I-1 — a model that
    /// took visibility from the child alone would show it).
    /// </summary>
    public static FormDocument HiddenBox(int width = 400, int height = 300) => Read("HiddenBox", width, height, """
        <Panel Id="box" X="20" Y="20" Width="150" Height="100" Visible="false">
          <Panel Id="boxKid" X="10" Y="10" Width="40" Height="30"/>
        </Panel>
        <Panel Id="shownBox" X="200" Y="20" Width="150" Height="100">
          <Panel Id="shownKid" X="10" Y="10" Width="40" Height="30"/>
        </Panel>
        """);

    /// <summary>
    /// A Dock=Left container whose STORED X/Y (200,100) are nowhere near where it docks (0,0): its children must be
    /// offset by the docked rectangle (review I-1). Holds a positioned (Top,Left) child and a Dock=Top child.
    /// </summary>
    public static FormDocument DockedBox(int width = 400, int height = 300) => Read("DockedBox", width, height, """
        <Panel Id="side" X="200" Y="100" Width="120" Height="50" Dock="Left">
          <Panel Id="sideTop" X="0" Y="0" Width="120" Height="20" Dock="Top"/>
          <Panel Id="sideSub" X="10" Y="40" Width="50" Height="30"/>
        </Panel>
        """);

    /// <summary>
    /// ⚠ OPEN (Task 12 review round, measured): a Bottom,Right child in a Dock=Left container whose STORED height
    /// (50) is not its docked height (300). WinForms captures the child's anchor distances against the container's
    /// size when the child is ADDED — the stored 120×50, since the container is docked only when it is itself added
    /// to the form — so the child lands at Y = 300 − (50 − 250 − 30) − 30 = 500. The model (and the page, which
    /// anchors against <c>ClientSizeOf</c> = the docked bounds) says 250.
    /// </summary>
    public static FormDocument DockedAnchor(int width = 400, int height = 300) => Read("DockedAnchor", width, height, """
        <Panel Id="side" X="200" Y="100" Width="120" Height="50" Dock="Left">
          <Panel Id="sideBR" X="60" Y="250" Width="50" Height="30" Anchor="Bottom,Right"/>
        </Panel>
        """);

    /// <summary>All three strips, each with one item, and a Fill — measured pinned and auto-sized.</summary>
    public static FormDocument Strips(string name, int width = 400, int height = 300) => Read(name, width, height, """
        <MenuStrip Id="menu" Dock="Top"><ToolStripMenuItem Id="mnuFile" Text="File"/></MenuStrip>
        <ToolStrip Id="tools" Dock="Top"><ToolStripButton Id="btnNew" Text="New"/></ToolStrip>
        <StatusStrip Id="status" Dock="Bottom"><ToolStripStatusLabel Id="lblReady" Text="Ready"/></StatusStrip>
        <Panel Id="fill" X="0" Y="0" Width="100" Height="100" Dock="Fill"/>
        """);
}
