using NUnit.Framework;
using VisualGameStudio.Core.Events;
using VisualGameStudio.Core.Models;

namespace VisualGameStudio.Tests.Core;

// Test event classes
public record TestEvent(string Message);
public record AnotherTestEvent(int Value);

[TestFixture]
public class EventAggregatorTests
{
    private EventAggregator _aggregator = null!;

    [SetUp]
    public void SetUp()
    {
        _aggregator = new EventAggregator();
    }

    [Test]
    public void Subscribe_AndPublish_HandlerReceivesEvent()
    {
        TestEvent? receivedEvent = null;
        _aggregator.Subscribe<TestEvent>(e => receivedEvent = e);

        _aggregator.Publish(new TestEvent("Hello"));

        Assert.That(receivedEvent, Is.Not.Null);
        Assert.That(receivedEvent!.Message, Is.EqualTo("Hello"));
    }

    [Test]
    public void Subscribe_MultipleHandlers_AllReceiveEvent()
    {
        int callCount = 0;
        _aggregator.Subscribe<TestEvent>(_ => callCount++);
        _aggregator.Subscribe<TestEvent>(_ => callCount++);
        _aggregator.Subscribe<TestEvent>(_ => callCount++);

        _aggregator.Publish(new TestEvent("Test"));

        Assert.That(callCount, Is.EqualTo(3));
    }

    [Test]
    public void Publish_NoSubscribers_DoesNotThrow()
    {
        Assert.DoesNotThrow(() => _aggregator.Publish(new TestEvent("Test")));
    }

    [Test]
    public void Publish_NullEvent_DoesNotInvokeHandlers()
    {
        int callCount = 0;
        _aggregator.Subscribe<TestEvent>(_ => callCount++);

        _aggregator.Publish<TestEvent>(null!);

        Assert.That(callCount, Is.EqualTo(0));
    }

    [Test]
    public void Unsubscribe_RemovesHandler()
    {
        int callCount = 0;
        Action<TestEvent> handler = _ => callCount++;

        _aggregator.Subscribe(handler);
        _aggregator.Publish(new TestEvent("First"));
        Assert.That(callCount, Is.EqualTo(1));

        _aggregator.Unsubscribe(handler);
        _aggregator.Publish(new TestEvent("Second"));
        Assert.That(callCount, Is.EqualTo(1)); // Still 1, not 2
    }

    [Test]
    public void Unsubscribe_NonExistentHandler_DoesNotThrow()
    {
        Action<TestEvent> handler = _ => { };

        Assert.DoesNotThrow(() => _aggregator.Unsubscribe(handler));
    }

    [Test]
    public void Subscribe_ReturnsDisposable_ThatUnsubscribes()
    {
        int callCount = 0;
        var subscription = _aggregator.Subscribe<TestEvent>(_ => callCount++);

        _aggregator.Publish(new TestEvent("First"));
        Assert.That(callCount, Is.EqualTo(1));

        subscription.Dispose();
        _aggregator.Publish(new TestEvent("Second"));
        Assert.That(callCount, Is.EqualTo(1)); // Still 1, not 2
    }

    [Test]
    public void Subscribe_DisposeTwice_DoesNotThrow()
    {
        var subscription = _aggregator.Subscribe<TestEvent>(_ => { });

        Assert.DoesNotThrow(() =>
        {
            subscription.Dispose();
            subscription.Dispose();
        });
    }

    [Test]
    public void Publish_DifferentEventTypes_OnlyMatchingHandlersCalled()
    {
        TestEvent? testEvent = null;
        AnotherTestEvent? anotherEvent = null;

        _aggregator.Subscribe<TestEvent>(e => testEvent = e);
        _aggregator.Subscribe<AnotherTestEvent>(e => anotherEvent = e);

        _aggregator.Publish(new TestEvent("Hello"));

        Assert.That(testEvent, Is.Not.Null);
        Assert.That(anotherEvent, Is.Null);
    }

    [Test]
    public void Publish_HandlerThrows_OtherHandlersStillCalled()
    {
        int callCount = 0;
        _aggregator.Subscribe<TestEvent>(_ => throw new Exception("Handler error"));
        _aggregator.Subscribe<TestEvent>(_ => callCount++);
        _aggregator.Subscribe<TestEvent>(_ => callCount++);

        // Should not throw and other handlers should be called
        Assert.DoesNotThrow(() => _aggregator.Publish(new TestEvent("Test")));
        Assert.That(callCount, Is.EqualTo(2));
    }

    [Test]
    public void Subscribe_SameHandlerTwice_CalledTwice()
    {
        int callCount = 0;
        Action<TestEvent> handler = _ => callCount++;

        _aggregator.Subscribe(handler);
        _aggregator.Subscribe(handler);

        _aggregator.Publish(new TestEvent("Test"));

        Assert.That(callCount, Is.EqualTo(2));
    }

    [Test]
    public void Unsubscribe_OnlyRemovesOneInstance()
    {
        int callCount = 0;
        Action<TestEvent> handler = _ => callCount++;

        _aggregator.Subscribe(handler);
        _aggregator.Subscribe(handler);
        _aggregator.Unsubscribe(handler);

        _aggregator.Publish(new TestEvent("Test"));

        Assert.That(callCount, Is.EqualTo(1));
    }

    [Test]
    public void Subscribe_ManyEvents_AllHandlersReceiveCorrectEvents()
    {
        var testEvents = new List<TestEvent>();
        var anotherEvents = new List<AnotherTestEvent>();

        _aggregator.Subscribe<TestEvent>(e => testEvents.Add(e));
        _aggregator.Subscribe<AnotherTestEvent>(e => anotherEvents.Add(e));

        _aggregator.Publish(new TestEvent("A"));
        _aggregator.Publish(new AnotherTestEvent(1));
        _aggregator.Publish(new TestEvent("B"));
        _aggregator.Publish(new AnotherTestEvent(2));

        Assert.That(testEvents, Has.Count.EqualTo(2));
        Assert.That(anotherEvents, Has.Count.EqualTo(2));
        Assert.That(testEvents[0].Message, Is.EqualTo("A"));
        Assert.That(testEvents[1].Message, Is.EqualTo("B"));
        Assert.That(anotherEvents[0].Value, Is.EqualTo(1));
        Assert.That(anotherEvents[1].Value, Is.EqualTo(2));
    }

    [Test]
    public void ImplementsIEventAggregator()
    {
        Assert.That(_aggregator, Is.InstanceOf<IEventAggregator>());
    }
}

[TestFixture]
public class EventRecordTests
{
    [Test]
    public void FileOpenedEvent_HasCorrectProperties()
    {
        var evt = new FileOpenedEvent("/path/file.bas", "content");

        Assert.That(evt.FilePath, Is.EqualTo("/path/file.bas"));
        Assert.That(evt.Content, Is.EqualTo("content"));
    }

    [Test]
    public void FileSavedEvent_HasCorrectProperties()
    {
        var evt = new FileSavedEvent("/path/file.bas");

        Assert.That(evt.FilePath, Is.EqualTo("/path/file.bas"));
    }

    [Test]
    public void FileClosedEvent_HasCorrectProperties()
    {
        var evt = new FileClosedEvent("/path/file.bas");

        Assert.That(evt.FilePath, Is.EqualTo("/path/file.bas"));
    }

    [Test]
    public void ProjectOpenedEvent_HasCorrectProperties()
    {
        var project = new BasicLangProject { Name = "Test" };
        var evt = new ProjectOpenedEvent(project);

        Assert.That(evt.Project, Is.SameAs(project));
    }

    [Test]
    public void ProjectClosedEvent_HasCorrectProperties()
    {
        var project = new BasicLangProject { Name = "Test" };
        var evt = new ProjectClosedEvent(project);

        Assert.That(evt.Project, Is.SameAs(project));
    }

    [Test]
    public void ProjectChangedEvent_HasCorrectProperties()
    {
        var project = new BasicLangProject { Name = "Test" };
        var evt = new ProjectChangedEvent(project);

        Assert.That(evt.Project, Is.SameAs(project));
    }

    [Test]
    public void SolutionOpenedEvent_HasCorrectProperties()
    {
        var solution = new BasicLangSolution { Name = "TestSolution" };
        var evt = new SolutionOpenedEvent(solution);

        Assert.That(evt.Solution, Is.SameAs(solution));
    }

    [Test]
    public void SolutionClosedEvent_HasCorrectProperties()
    {
        var solution = new BasicLangSolution { Name = "TestSolution" };
        var evt = new SolutionClosedEvent(solution);

        Assert.That(evt.Solution, Is.SameAs(solution));
    }

    [Test]
    public void BuildStartedEvent_HasCorrectProperties()
    {
        var project = new BasicLangProject { Name = "Test" };
        var evt = new BuildStartedEvent(project);

        Assert.That(evt.Project, Is.SameAs(project));
    }

    [Test]
    public void BuildCompletedEvent_HasCorrectProperties()
    {
        var result = new BuildResult { Success = true };
        var evt = new BuildCompletedEvent(result);

        Assert.That(evt.Result, Is.SameAs(result));
    }

    [Test]
    public void BuildProgressEvent_HasCorrectProperties()
    {
        var evt = new BuildProgressEvent("Compiling...", 50);

        Assert.That(evt.Message, Is.EqualTo("Compiling..."));
        Assert.That(evt.PercentComplete, Is.EqualTo(50));
    }

    [Test]
    public void BuildProgressEvent_PercentCanBeNull()
    {
        var evt = new BuildProgressEvent("Starting...", null);

        Assert.That(evt.PercentComplete, Is.Null);
    }

    [Test]
    public void DiagnosticsUpdatedEvent_HasCorrectProperties()
    {
        var diagnostics = new List<DiagnosticItem>
        {
            new() { Id = "BC001", Message = "Error" }
        };
        var evt = new DiagnosticsUpdatedEvent("/path/file.bas", diagnostics);

        Assert.That(evt.FilePath, Is.EqualTo("/path/file.bas"));
        Assert.That(evt.Diagnostics, Has.Count.EqualTo(1));
    }

    [Test]
    public void ActiveDocumentChangedEvent_HasCorrectProperties()
    {
        var evt = new ActiveDocumentChangedEvent("/path/file.bas");

        Assert.That(evt.FilePath, Is.EqualTo("/path/file.bas"));
    }

    [Test]
    public void ActiveDocumentChangedEvent_FilePathCanBeNull()
    {
        var evt = new ActiveDocumentChangedEvent(null);

        Assert.That(evt.FilePath, Is.Null);
    }

    [Test]
    public void DocumentDirtyChangedEvent_HasCorrectProperties()
    {
        var evt = new DocumentDirtyChangedEvent("/path/file.bas", true);

        Assert.That(evt.FilePath, Is.EqualTo("/path/file.bas"));
        Assert.That(evt.IsDirty, Is.True);
    }

    [Test]
    public void ThemeChangedEvent_HasCorrectProperties()
    {
        var evt = new ThemeChangedEvent("Dark");

        Assert.That(evt.ThemeName, Is.EqualTo("Dark"));
    }
}
