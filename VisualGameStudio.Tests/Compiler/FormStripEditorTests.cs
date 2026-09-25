using System.Collections.Generic;
using System.Linq;
using BasicLang.Forms;
using Moq;
using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.Core.Events;
using VisualGameStudio.Core.Models;
using VisualGameStudio.Shell.Controls;
using VisualGameStudio.Shell.ViewModels.Documents;

namespace VisualGameStudio.Tests.Compiler;

/// <summary>
/// Task 18 (commit 24c) — driven through the document view model, the same way <c>FormTrayTests</c>
/// drives the tray: an Item kind (spec §1, <c>Place == Item</c>) has no place of its own on the
/// canvas or in the tray, so BOTH of the VM's drop paths must refuse it, naming the "Type Here" slot
/// it actually comes from (spec §6) — never place it, and never say something else instead.
///
/// <para>⛔ Both paths, not one. <c>TrayDropCommand</c> is the tray's own drop; the canvas's is
/// <c>PlaceDroppedControlCommand</c>, which <c>FormCanvasDropTests</c> calls "the seam" the AXAML
/// binds to. An Item kind reaches both today with the wrong outcome in each: <c>TrayDrop</c> already
/// has an Item-shaped guard, but its message is <c>"'{kind}' has a position; drop it on the form, not
/// the tray"</c> — false for an Item, which has no position anywhere — and
/// <c>PlaceDroppedControl</c> has no guard at all, so it SUCCEEDS through the unmodified
/// <see cref="FormPlacement.Place"/>, positioning a control that should not exist as one.</para>
/// </summary>
[TestFixture]
public class FormStripEditorTests
{
    private const string Dir = "/proj/";

    private const string WinFormsDoc = """
        <Form Name="MenuForm" Version="1" Width="640" Height="480" Text="MenuForm">
          <Controls/>
          <Components/>
          <Resources/>
        </Form>
        """;

    private Mock<IEventAggregator> _events = null!;

    [SetUp]
    public void SetUp() => _events = new Mock<IEventAggregator>();

    private CodeEditorDocumentViewModel Open(string text = WinFormsDoc, string fileName = "MenuForm.blform")
    {
        var vm = new CodeEditorDocumentViewModel(new Mock<IFileService>().Object, _events.Object)
        {
            FilePath = Dir + fileName
        };
        vm.SetContent(text);
        return vm;
    }

    [Test]
    public void ATrayDrop_OfAnItemKind_IsRefused_NamingTypeHere_AndPlacesNothing()
    {
        var vm = Open();

        vm.TrayDropCommand.Execute("ToolStripMenuItem");

        Assert.Multiple(() =>
        {
            Assert.That(vm.DesignDocument!.Controls, Is.Empty, "nothing placed");
            Assert.That(vm.Tray.Items, Is.Empty, "not a component either — it has no place at all");
        });

        _events.Verify(e => e.Publish(It.Is<DesignerDiagnosticsEvent>(ev =>
                ev.Diagnostics.Any(d => d.Id == DesignCodes.PlacementRefused && d.Message.Contains("Type Here")))),
            Times.Once,
            "the refusal must name the Type Here slot — today's TrayDrop message ('has a position; " +
            "drop it on the form') is wrong for a kind that has no position anywhere");
    }

    [Test]
    public void ADropOfAnItemKind_OnTheCanvas_IsRefused_NamingTypeHere_AndPlacesNothing()
    {
        var vm = Open();

        vm.PlaceDroppedControlCommand.Execute(new FormControlDropRequest("ToolStripMenuItem", 10, 10));

        Assert.That(vm.DesignDocument!.Controls, Is.Empty,
            "today this SUCCEEDS — FormPlacement.Place has no Item arm yet, so it positions the item " +
            "like an ordinary control");

        _events.Verify(e => e.Publish(It.Is<DesignerDiagnosticsEvent>(ev =>
                ev.Diagnostics.Any(d => d.Id == DesignCodes.PlacementRefused && d.Message.Contains("Type Here")))),
            Times.Once);
    }

    // ======================================================================================
    // Task 23 (commit 24d) — StripEditor: Begin/Commit/Cancel, the selection leave-rule, paste
    // into a host. See the "PRE-FLIGHT CORRECTIONS" block at the head of the plan's 24d
    // section — Blockers 1, 2 and 3 are all pinned below. Every test drives the document VM,
    // never FormStripEditorViewModel in isolation: a VM whose commands never ran would pass an
    // isolated VM test and be unreachable in the IDE (the AddNewFormCommand lesson).
    // ======================================================================================

    private const string MenuStripDoc = """
        <Form Name="MenuForm" Version="1" Width="640" Height="480" Text="MenuForm">
          <Controls>
            <MenuStrip Id="menuStrip1" Dock="Top"/>
            <StatusStrip Id="statusStrip1" Dock="Bottom"/>
            <Button Id="btnGo" Text="Go" X="16" Y="80" Width="75" Height="23" TabIndex="0"/>
          </Controls>
          <Components/>
          <Resources/>
        </Form>
        """;

    private static FormControl MenuStrip(CodeEditorDocumentViewModel vm) =>
        vm.DesignDocument!.Controls.Single(c => c.Id == "menuStrip1");

    private static FormControl StatusStrip(CodeEditorDocumentViewModel vm) =>
        vm.DesignDocument!.Controls.Single(c => c.Id == "statusStrip1");

    private static FormControl Button(CodeEditorDocumentViewModel vm) =>
        vm.DesignDocument!.Controls.Single(c => c.Id == "btnGo");

    /// <summary>Catches: Begin not selecting the host, or not activating the editor.</summary>
    [Test]
    public void BeginTypeHere_ActivatesTheEditor_AndSelectsTheHost()
    {
        var vm = Open(MenuStripDoc);
        var strip = MenuStrip(vm);

        vm.BeginTypeHere(strip);

        Assert.Multiple(() =>
        {
            Assert.That(vm.StripEditor.IsActive, Is.True);
            Assert.That(vm.StripEditor.Host, Is.SameAs(strip));
            Assert.That(vm.Selection.Primary, Is.SameAs(strip),
                "the slot exists only for the selected strip/item's path");
        });
    }

    /// <summary>Catches: Commit not calling PlaceItem, not selecting the result, not writing the
    /// document, or ending the "type a whole menu in one run" flow by deactivating/reparenting.</summary>
    [Test]
    public void CommitTypeHere_AddsAnItem_SelectsIt_WritesTheDocument_AndStaysActiveOnTheSameHost()
    {
        var vm = Open(MenuStripDoc);
        var strip = MenuStrip(vm);
        vm.BeginTypeHere(strip);

        vm.CommitTypeHere("&File");

        var file = strip.Children.SingleOrDefault(c => c.Id == "fileToolStripMenuItem");
        Assert.Multiple(() =>
        {
            Assert.That(file, Is.Not.Null);
            Assert.That(file!.Properties.GetValueOrDefault("Text"), Is.EqualTo("&File"));
            Assert.That(vm.Selection.Primary, Is.SameAs(file), "selected through Selection, so the grid follows");
            Assert.That(vm.Text, Does.Contain("fileToolStripMenuItem"), "reached the document — undoable");
            Assert.That(vm.StripEditor.IsActive, Is.True, "type a whole menu in one run");
            Assert.That(vm.StripEditor.Host, Is.SameAs(strip), "the parent stays the host, not the new item");
        });
    }

    /// <summary>Catches: Begin-on-an-item not re-hosting the editor, "-" not mapping to
    /// ToolStripSeparator, or the three commits landing in the wrong order.</summary>
    [Test]
    public void BeginOnAnItem_ThenCommitThreeTimes_NestsThemInOrder_OpenSeparatorExit()
    {
        var vm = Open(MenuStripDoc);
        var strip = MenuStrip(vm);
        vm.BeginTypeHere(strip);
        vm.CommitTypeHere("&File");
        var file = strip.Children.Single();

        vm.BeginTypeHere(file);
        vm.CommitTypeHere("&Open...");
        vm.CommitTypeHere("-");
        vm.CommitTypeHere("E&xit");

        Assert.That(file.Children.Select(c => c.Id), Is.EqualTo(new[]
        {
            "openToolStripMenuItem", "toolStripSeparator1", "exitToolStripMenuItem"
        }));
        Assert.That(file.Children[1].Kind, Is.EqualTo("ToolStripSeparator"));
    }

    /// <summary>Catches: PlaceItem's rule check not being reached, or the refusal not naming the
    /// kind (BL8019, "the item could not be placed" alone would pass a weaker assertion).</summary>
    [Test]
    public void CommitTypeHere_ASeparator_OnAStatusStrip_IsRefused_NamingSeparator_AndPlacesNothing()
    {
        var vm = Open(MenuStripDoc);
        var status = StatusStrip(vm);
        vm.BeginTypeHere(status);

        vm.CommitTypeHere("-");

        Assert.That(status.Children, Is.Empty);
        _events.Verify(e => e.Publish(It.Is<DesignerDiagnosticsEvent>(ev =>
                ev.Diagnostics.Any(d => d.Id == DesignCodes.PlacementRefused && d.Message.Contains("Separator")))),
            Times.Once);
    }

    /// <summary>Catches: Delete not walking into a strip's Children (ListContaining), and the
    /// leave-rule not firing when the selection is cleared to null.</summary>
    [Test]
    public void DeletingAnItem_RemovesItFromTheHost_AndFromTheText_AndCancelsTheEditor()
    {
        var vm = Open(MenuStripDoc);
        var strip = MenuStrip(vm);
        vm.BeginTypeHere(strip);
        vm.CommitTypeHere("&File");
        var file = strip.Children.Single();

        vm.DeleteControlCommand.Execute(file);

        Assert.Multiple(() =>
        {
            Assert.That(strip.Children, Is.Empty);
            Assert.That(vm.Text, Does.Not.Contain("fileToolStripMenuItem"));
            Assert.That(vm.StripEditor.IsActive, Is.False,
                "delete clears the selection, and the leave-rule must cancel the editor");
        });
    }

    /// <summary>
    /// Catches: Cancel not clearing IsActive/Host.
    ///
    /// <para>⚠ State is set DIRECTLY on <c>StripEditor</c> rather than through
    /// <c>BeginTypeHere</c>: while Begin is a no-op stub, going through it would make this test
    /// pass vacuously (IsActive is already False, so "still False" proves nothing about Cancel).
    /// Pre-setting makes the assertion depend on Cancel actually running.</para>
    /// </summary>
    [Test]
    public void CancelTypeHere_DeactivatesTheEditor()
    {
        var vm = Open(MenuStripDoc);
        var strip = MenuStrip(vm);
        vm.StripEditor.IsActive = true;
        vm.StripEditor.Host = strip;

        vm.CancelTypeHere();

        Assert.Multiple(() =>
        {
            Assert.That(vm.StripEditor.IsActive, Is.False);
            Assert.That(vm.StripEditor.Host, Is.Null);
        });
    }

    /// <summary>
    /// The leave-rule, half 1: selecting OUTSIDE the host cancels. Catches: the Selection.Changed
    /// handler not calling IsInside/CancelTypeHere at all.
    ///
    /// <para>⚠ Same reason as above: <c>StripEditor.IsActive</c> is set directly rather than via
    /// the (currently no-op) <c>BeginTypeHere</c>, so the assertion cannot pass just because
    /// Begin never ran.</para>
    /// </summary>
    [Test]
    public void SelectingAControlOutsideTheHost_CancelsTheEditor()
    {
        var vm = Open(MenuStripDoc);
        var strip = MenuStrip(vm);
        var button = Button(vm);
        vm.StripEditor.IsActive = true;
        vm.StripEditor.Host = strip;

        vm.Selection.Set(button);

        Assert.That(vm.StripEditor.IsActive, Is.False);
    }

    /// <summary>The leave-rule, half 2: selecting something INSIDE the host does not cancel.
    /// Catches: an IsInside that only compares by reference to the host itself, never walking
    /// ancestors, which would cancel on every child selection including the host's own new item.</summary>
    [Test]
    public void SelectingAControlInsideTheHost_LeavesTheEditorActive()
    {
        var vm = Open(MenuStripDoc);
        var strip = MenuStrip(vm);
        vm.BeginTypeHere(strip);
        vm.CommitTypeHere("&File");
        var file = strip.Children.Single();

        vm.Selection.Set(file);

        Assert.That(vm.StripEditor.IsActive, Is.True);
    }

    /// <summary>Catches: undo not re-parsing (a stale FormControl reference would still show the
    /// item), or the editor not noticing its host/item no longer resolves in the rebuilt model.</summary>
    [Test]
    public void UndoAfterACommit_RemovesTheItem_AndCancelsTheEditor()
    {
        var vm = Open(MenuStripDoc);
        var strip = MenuStrip(vm);
        vm.BeginTypeHere(strip);
        vm.CommitTypeHere("&File");

        vm.UndoDesignerEditCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(MenuStrip(vm).Children, Is.Empty);
            Assert.That(vm.StripEditor.IsActive, Is.False);
        });
    }

    /// <summary>Catches: PasteControls' Item branch not checking the host's FormItemRule, or not
    /// appending into host.Children at all.</summary>
    [Test]
    public void PastingAnItem_IntoASelectedStrip_AppendsIt_AndSelectsIt()
    {
        var vm = Open(MenuStripDoc);
        var strip = MenuStrip(vm);
        vm.BeginTypeHere(strip);
        vm.CommitTypeHere("&File");
        var file = strip.Children.Single();

        vm.Selection.Set(file);
        vm.CopyControlsCommand.Execute(null);
        vm.Selection.Set(strip);
        vm.PasteControlsCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(strip.Children, Has.Count.EqualTo(2), "the original plus the paste");
            Assert.That(vm.Selection.Primary, Is.SameAs(strip.Children[^1]));
        });
    }

    /// <summary>Catches: a pasted Docked-row control being offset or given a TabIndex/geometry it
    /// must not have — today's PasteControls happens to already do this for any geometry-less
    /// non-component, so this pin is what stops a LATER edit regressing it silently.</summary>
    [Test]
    public void PastingAStrip_AppendsToControls_WithNoGeometry_KeepingDock_TabIndexZero_AndSelectsIt()
    {
        var vm = Open(MenuStripDoc);
        var strip = MenuStrip(vm);

        vm.Selection.Set(strip);
        vm.CopyControlsCommand.Execute(null);
        vm.PasteControlsCommand.Execute(null);

        var pasted = vm.DesignDocument!.Controls.Last();
        Assert.Multiple(() =>
        {
            Assert.That(pasted, Is.Not.SameAs(strip));
            Assert.That(pasted.Geometry, Is.Null);
            Assert.That(pasted.Properties.GetValueOrDefault("Dock"), Is.EqualTo("Top"));
            Assert.That(pasted.TabIndex, Is.EqualTo(0));
            Assert.That(vm.Selection.Primary, Is.SameAs(pasted));
        });
    }

    /// <summary>Catches: a refused item paste adding the control anywhere reachable, or moving
    /// the selection despite refusing (Blocker 3's SetRange([]) hazard).</summary>
    [Test]
    public void PastingAnItem_WithAPositionedControlSelected_IsRefused_AndTheSelectionIsUnchanged()
    {
        var vm = Open(MenuStripDoc);
        var strip = MenuStrip(vm);
        vm.BeginTypeHere(strip);
        vm.CommitTypeHere("&File");
        var file = strip.Children.Single();

        vm.Selection.Set(file);
        vm.CopyControlsCommand.Execute(null);
        var button = Button(vm);
        vm.Selection.Set(button);

        vm.PasteControlsCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(strip.Children, Has.Count.EqualTo(1), "nothing added anywhere reachable");
            Assert.That(vm.Selection.Primary, Is.SameAs(button), "selection unchanged");
        });
    }

    /// <summary>
    /// ⛔⛔ BLOCKER 1 pin. <c>[ObservableProperty] bool _isActive; FormControl? _host; string
    /// _text = "";</c> is THREE field declarations — the attribute binds only the first. Task 24's
    /// bindings to <c>StripEditor.Host</c> and <c>StripEditor.Text</c> would then bind once at
    /// attach and never update again (a 0x0 focused TextBox is the measured, shipped consequence).
    /// This is the ONLY kind of test that can see the difference between a bound property and a
    /// plain one — nothing that merely reads <c>.Host</c>/<c>.Text</c> afterwards can.
    /// </summary>
    [Test]
    public void StripEditor_RaisesPropertyChanged_ForHostOnBegin_TextOnCommit_AndIsActive()
    {
        var vm = Open(MenuStripDoc);
        var strip = MenuStrip(vm);
        var raised = new List<string?>();
        vm.StripEditor.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.BeginTypeHere(strip);

        Assert.That(raised, Does.Contain(nameof(vm.StripEditor.Host)),
            "Host must be an [ObservableProperty] — as stubbed today it is a plain property and " +
            "raises nothing (Blocker 1)");
        Assert.That(raised, Does.Contain(nameof(vm.StripEditor.IsActive)));

        // ⚠ Model what the overlay's TwoWay binding does BEFORE Enter: it has written the typed text
        // into StripEditor.Text. Without this, Begin's clear leaves Text at "" and Commit's clear sets
        // "" to "" — no change, so no notification, and NO correct implementation could pass.
        vm.StripEditor.Text = "&File";
        raised.Clear();
        vm.CommitTypeHere("&File");

        Assert.That(raised, Does.Contain(nameof(vm.StripEditor.Text)),
            "Text must be an [ObservableProperty], or the overlay's TwoWay binding never clears " +
            "after a commit (Blocker 1)");
    }

    /// <summary>
    /// ⛔⛔ BLOCKER 2 pin. This repo has NO compiled bindings (no
    /// <c>AvaloniaUseCompiledBindingsByDefault</c>, no <c>Directory.Build.props</c>), so
    /// <c>{Binding BeginTypeHereCommand}</c> in the AXAML binds successfully to nothing if the
    /// toolkit never generates that command — exactly the <c>AddNewFormCommand</c> failure, where
    /// an intervening doc comment made <c>[RelayCommand]</c> bind to the wrong declaration. Every
    /// other test here calls the METHODS; this one is the only one that proves the GENERATED
    /// members exist and drive the same outcomes.
    /// </summary>
    [Test]
    public void TheGeneratedCommands_DriveTheSameOutcomes_AsTheMethodsDo()
    {
        var vm = Open(MenuStripDoc);
        var strip = MenuStrip(vm);

        Assert.That(vm.BeginTypeHereCommand, Is.Not.Null);
        vm.BeginTypeHereCommand.Execute(strip);
        Assert.That(vm.StripEditor.IsActive, Is.True);

        Assert.That(vm.CommitTypeHereCommand, Is.Not.Null);
        vm.CommitTypeHereCommand.Execute("&File");
        Assert.That(strip.Children.Any(c => c.Id == "fileToolStripMenuItem"), Is.True);

        Assert.That(vm.CancelTypeHereCommand, Is.Not.Null);
        vm.CancelTypeHereCommand.Execute(null);
        Assert.That(vm.StripEditor.IsActive, Is.False);
    }

    /// <summary>
    /// ⛔⛔ BLOCKER 3 pin, half 1. The plan's Item-paste snippet puts <c>continue</c> only in the
    /// <c>else</c>; the loop's last statement is the UNCONDITIONAL
    /// <c>(… ? Components : Controls).Add(control)</c>, so a "successful" item paste falls
    /// through and lands in <c>document.Controls</c> too — <c>AllControls()</c> then yields it
    /// TWICE, the writer emits it twice, and the next Delete removes the wrong copy.
    /// </summary>
    [Test]
    public void PastingAnItem_NeverAddsItToDocumentControls_OnlyToItsHost()
    {
        var vm = Open(MenuStripDoc);
        var strip = MenuStrip(vm);
        vm.BeginTypeHere(strip);
        vm.CommitTypeHere("&File");
        var file = strip.Children.Single();

        vm.Selection.Set(file);
        vm.CopyControlsCommand.Execute(null);
        vm.Selection.Set(strip);
        vm.PasteControlsCommand.Execute(null);

        var pasted = strip.Children.Last();
        Assert.Multiple(() =>
        {
            Assert.That(vm.DesignDocument!.Controls, Does.Not.Contain(pasted),
                "an item belongs only in its host's Children, never in document.Controls too");
            Assert.That(vm.DesignDocument!.AllControls().Count(c => ReferenceEquals(c, pasted)), Is.EqualTo(1));
        });
    }

    /// <summary>
    /// ⛔⛔ BLOCKER 3 pin, half 2. <c>Selection.SetRange</c> with an EMPTY list clears the
    /// selection (<c>FormSelection.cs</c>), which — with the leave-rule this same task adds —
    /// would also cancel the editor and contradicts "a refused paste leaves the selection
    /// unchanged". The fix is to return before reaching <c>SetRange</c>/the renumber/the write
    /// when nothing was actually added.
    /// </summary>
    [Test]
    public void ARefusedPaste_LeavesSelectionAndEditorAndRevisionUnchanged()
    {
        var vm = Open(MenuStripDoc);
        var strip = MenuStrip(vm);
        vm.BeginTypeHere(strip);
        vm.CommitTypeHere("&File");
        var file = strip.Children.Single();

        vm.Selection.Set(file);
        vm.CopyControlsCommand.Execute(null);
        // ⛔ Open the editor on a host that REFUSES the pasted kind. The first version selected a
        // Button and then called BeginTypeHere(strip) — but Begin must select its host (see
        // BeginTypeHere_ActivatesTheEditor_AndSelectsTheHost), so the MenuStrip became the target,
        // accepted the item, and there was no refusal to test. It passed only when Begin was broken.
        var status = StatusStrip(vm);
        vm.BeginTypeHere(status);
        Assert.That(vm.Selection.Primary, Is.SameAs(status), "precondition: Begin selected the StatusStrip");
        var revisionBefore = vm.DesignModelRevision;

        vm.PasteControlsCommand.Execute(null);

        Assert.Multiple(() =>
        {
            Assert.That(vm.Selection.Primary, Is.SameAs(status), "selection unchanged by a refused paste");
            Assert.That(vm.StripEditor.IsActive, Is.True,
                "an emptied selection would fail the leave-rule's IsInside check and cancel the editor");
            Assert.That(vm.DesignModelRevision, Is.EqualTo(revisionBefore), "nothing was written");
        });
    }

    // ======================================================================================
    // "Can't edit a menu item once it is entered" — VS's second-click / F2 gesture (owner's
    // report, 2026-09-23). BeginEditItem opens the SAME StripEditor/overlay on an EXISTING
    // item's own cell, pre-filled with its current Text; committing renames it in place and
    // closes the editor (unlike BeginTypeHere/CommitTypeHere, which append a NEW item and stay
    // open). Contract: FormStripEditorViewModel.EditTarget is non-null only in this mode; today
    // BeginEditItem is a NO-OP STUB (see CodeEditorDocumentViewModel.BeginEditItem) so every
    // test below is measured red against that stub, not against a compile failure.
    // ======================================================================================

    /// <summary>Catches: BeginEditItem not activating the editor, not seeding Text from the
    /// item's current caption, or not keeping the item itself selected (VS never changes the
    /// selection to start a rename).</summary>
    [Test]
    public void BeginEditItem_OnAnExistingItem_ActivatesTheEditor_SeededWithItsText_AndKeepsItSelected()
    {
        var vm = Open(MenuStripDoc);
        var strip = MenuStrip(vm);
        vm.BeginTypeHere(strip);
        vm.CommitTypeHere("&Open...");
        var open = strip.Children.Single();
        vm.Selection.Set(open);

        vm.BeginEditItem(open);

        Assert.Multiple(() =>
        {
            Assert.That(vm.StripEditor.IsActive, Is.True);
            Assert.That(vm.StripEditor.EditTarget, Is.SameAs(open),
                "EditTarget distinguishes 'renaming an existing item' from BeginTypeHere's " +
                "'appending a new one' — today's stub never sets it");
            Assert.That(vm.StripEditor.Text, Is.EqualTo("&Open..."),
                "the editor must be pre-filled with the item's CURRENT Text, not started empty " +
                "the way a new Type Here slot is");
            Assert.That(vm.Selection.Primary, Is.SameAs(open),
                "a rename must not change what is selected");
        });
    }

    /// <summary>Catches: BeginEditItem not checking that the target actually HAS a Text
    /// property — a separator has none, and the spec says F2/second-click on one does nothing.</summary>
    [Test]
    public void BeginEditItem_OnASeparator_DoesNothing()
    {
        var vm = Open(MenuStripDoc);
        var strip = MenuStrip(vm);
        vm.BeginTypeHere(strip);
        vm.CommitTypeHere("&File");
        var file = strip.Children.Single();
        vm.BeginTypeHere(file);
        vm.CommitTypeHere("-");
        var separator = file.Children.Single();
        vm.Selection.Set(separator);

        vm.BeginEditItem(separator);

        Assert.Multiple(() =>
        {
            Assert.That(vm.StripEditor.IsActive, Is.False);
            Assert.That(vm.StripEditor.EditTarget, Is.Null);
        });
    }

    /// <summary>Catches: commit not writing item.Properties["Text"], changing its Id/bindings,
    /// losing the selection, leaving the editor open (unlike the create flow), or not reaching
    /// the document text at all (so Undo could not restore it).</summary>
    [Test]
    public void CommitAfterBeginEditItem_RenamesTheItemInPlace_KeepsIdAndSelection_ClosesTheEditor_AndIsUndoable()
    {
        var vm = Open(MenuStripDoc);
        var strip = MenuStrip(vm);
        vm.BeginTypeHere(strip);
        vm.CommitTypeHere("&Save");
        var item = strip.Children.Single();
        var originalId = item.Id;
        vm.Selection.Set(item);
        vm.BeginEditItem(item);

        vm.CommitTypeHere("&Save As...");

        Assert.Multiple(() =>
        {
            Assert.That(item.Properties.GetValueOrDefault("Text"), Is.EqualTo("&Save As..."));
            Assert.That(item.Id, Is.EqualTo(originalId), "a rename must never touch the Id — event bindings key on it");
            Assert.That(vm.Selection.Primary, Is.SameAs(item), "still selected after the rename");
            Assert.That(vm.StripEditor.IsActive, Is.False,
                "a rename CLOSES the editor — unlike CommitTypeHere's create flow, which stays open");
            Assert.That(vm.Text, Does.Contain("Save As"), "reached the document — undoable");
        });

        vm.UndoDesignerEditCommand.Execute(null);
        Assert.That(MenuStrip(vm).Children.Single().Properties.GetValueOrDefault("Text"),
            Is.EqualTo("&Save"), "undo must restore the PRE-rename text");
    }

    /// <summary>
    /// Catches: Escape (CancelTypeHere) not clearing EditTarget, or leaving the item's Text
    /// changed from whatever was typed before cancelling.
    ///
    /// <para>⚠ State is set DIRECTLY on <c>StripEditor</c>, exactly like
    /// <c>CancelTypeHere_DeactivatesTheEditor</c> above: <c>BeginEditItem</c> is still a no-op
    /// stub, so routing through it would make "IsActive ends False" pass because it never became
    /// True — proving nothing about Cancel. Pre-seeding makes every assertion depend on Cancel
    /// actually running.</para>
    /// </summary>
    [Test]
    public void EscapeAfterBeginEditItem_LeavesTheItemsTextUnchanged_AndClearsEditTarget()
    {
        var vm = Open(MenuStripDoc);
        var strip = MenuStrip(vm);
        vm.BeginTypeHere(strip);
        vm.CommitTypeHere("&Save");
        var item = strip.Children.Single();
        vm.Selection.Set(item);
        vm.StripEditor.IsActive = true;
        vm.StripEditor.Host = item;
        vm.StripEditor.EditTarget = item;
        vm.StripEditor.Text = "garbage the user typed and then abandoned";

        vm.CancelTypeHere();

        Assert.Multiple(() =>
        {
            Assert.That(item.Properties.GetValueOrDefault("Text"), Is.EqualTo("&Save"), "unchanged");
            Assert.That(vm.StripEditor.IsActive, Is.False);
            Assert.That(vm.StripEditor.EditTarget, Is.Null,
                "CancelTypeHere as written clears IsActive/Host/Text only — EditTarget is a NEW " +
                "field it does not yet know about, so an edit-in-progress reads as cancelled but " +
                "still names its target");
        });
    }

    /// <summary>
    /// A whitespace-only commit must be treated as a cancel (spec: "treat as cancel") — and must
    /// especially never fall through to CommitTypeHere's CREATE path, which would otherwise nest a
    /// new child item under the one being renamed (an item usually accepts children too — see
    /// <c>BeginOnAnItem_ThenCommitThreeTimes...</c> above, which nests into exactly this kind of
    /// host). The shared whitespace guard already short-circuits before that branch, so this
    /// passes today as a genuine (not stub-dependent) regression pin for reusing CommitTypeHere in
    /// edit mode.
    /// </summary>
    [Test]
    public void CommittingWhitespaceAfterBeginEditItem_ChangesNothing_AndNestsNoChildItem()
    {
        var vm = Open(MenuStripDoc);
        var strip = MenuStrip(vm);
        vm.BeginTypeHere(strip);
        vm.CommitTypeHere("&Save");
        var item = strip.Children.Single();
        vm.Selection.Set(item);
        vm.StripEditor.IsActive = true;
        vm.StripEditor.Host = item;
        vm.StripEditor.EditTarget = item;

        vm.CommitTypeHere("   ");

        Assert.Multiple(() =>
        {
            Assert.That(item.Properties.GetValueOrDefault("Text"), Is.EqualTo("&Save"));
            Assert.That(item.Children, Is.Empty,
                "a whitespace commit must never create a nested item under the one being renamed");
        });
    }

    /// <summary>
    /// The leave-rule applies to a rename too: selecting anything other than the item being
    /// edited must cancel it. Catches: the Selection.Changed handler's IsInside check not covering
    /// a rename, or CancelTypeHere leaving EditTarget set once the leave-rule fires it.
    ///
    /// <para>⚠ Pre-seeded directly, for
    /// <see cref="EscapeAfterBeginEditItem_LeavesTheItemsTextUnchanged_AndClearsEditTarget"/>'s
    /// reason.</para>
    /// </summary>
    [Test]
    public void SelectingAnotherControl_WhileEditingAnItemsText_CancelsTheEdit()
    {
        var vm = Open(MenuStripDoc);
        var strip = MenuStrip(vm);
        vm.BeginTypeHere(strip);
        vm.CommitTypeHere("&Save");
        var item = strip.Children.Single();
        var button = Button(vm);
        vm.Selection.Set(item);
        vm.StripEditor.IsActive = true;
        vm.StripEditor.Host = item;
        vm.StripEditor.EditTarget = item;

        vm.Selection.Set(button);

        Assert.Multiple(() =>
        {
            Assert.That(vm.StripEditor.IsActive, Is.False);
            Assert.That(vm.StripEditor.EditTarget, Is.Null,
                "the same EditTarget-not-cleared gap as the Escape case, reached through the leave-rule instead");
        });
    }

    /// <summary>⛔⛔ BLOCKER-1-shaped pin for the NEW property: <c>EditTarget</c> must be its own
    /// <c>[ObservableProperty]</c> declaration, not folded onto an existing field's line, or a
    /// view bound to it would read once at attach and never update (the shipped Host/Text/IsActive
    /// defect, recurring for a fourth field).</summary>
    [Test]
    public void StripEditor_RaisesPropertyChanged_ForEditTarget_OnBeginEditItem()
    {
        var vm = Open(MenuStripDoc);
        var strip = MenuStrip(vm);
        vm.BeginTypeHere(strip);
        vm.CommitTypeHere("&Save");
        var item = strip.Children.Single();
        vm.Selection.Set(item);
        var raised = new List<string?>();
        vm.StripEditor.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.BeginEditItem(item);

        Assert.That(raised, Does.Contain(nameof(vm.StripEditor.EditTarget)),
            "EditTarget must be an [ObservableProperty] — a plain property raises nothing");
    }

    /// <summary>Proves the GENERATED command drives the same outcome as the method — the
    /// AddNewFormCommand failure mode (an attribute bound to the wrong declaration compiles
    /// clean and generates nothing reachable).</summary>
    [Test]
    public void TheGeneratedBeginEditItemCommand_DrivesTheSameOutcomeAsTheMethod()
    {
        var vm = Open(MenuStripDoc);
        var strip = MenuStrip(vm);
        vm.BeginTypeHere(strip);
        vm.CommitTypeHere("&Save");
        var item = strip.Children.Single();
        vm.Selection.Set(item);

        Assert.That(vm.BeginEditItemCommand, Is.Not.Null);
        vm.BeginEditItemCommand.Execute(item);

        Assert.Multiple(() =>
        {
            Assert.That(vm.StripEditor.IsActive, Is.True);
            Assert.That(vm.StripEditor.EditTarget, Is.SameAs(item));
        });
    }
}
