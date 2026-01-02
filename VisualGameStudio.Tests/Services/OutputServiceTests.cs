using NUnit.Framework;
using VisualGameStudio.Core.Abstractions.Services;
using VisualGameStudio.ProjectSystem.Services;

namespace VisualGameStudio.Tests.Services;

[TestFixture]
public class OutputServiceTests
{
    private OutputService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _service = new OutputService();
    }

    [Test]
    public void Constructor_InitializesAllCategories()
    {
        foreach (OutputCategory category in Enum.GetValues<OutputCategory>())
        {
            var messages = _service.GetMessages(category);
            Assert.That(messages, Is.Not.Null);
            Assert.That(messages, Is.Empty);
        }
    }

    [Test]
    public void WriteLine_AddsMessageToCategory()
    {
        _service.WriteLine("Test message", OutputCategory.Build);

        var messages = _service.GetMessages(OutputCategory.Build);
        Assert.That(messages, Has.Count.EqualTo(1));
        Assert.That(messages[0], Does.EndWith(Environment.NewLine));
    }

    [Test]
    public void Write_AddsMessageWithoutNewLine()
    {
        _service.Write("Test", OutputCategory.General);

        var messages = _service.GetMessages(OutputCategory.General);
        Assert.That(messages, Has.Count.EqualTo(1));
        Assert.That(messages[0], Is.EqualTo("Test"));
    }

    [Test]
    public void WriteError_PrefixesMessageWithError()
    {
        _service.WriteError("Something went wrong", OutputCategory.Build);

        var messages = _service.GetMessages(OutputCategory.Build);
        Assert.That(messages, Has.Count.EqualTo(1));
        Assert.That(messages[0], Does.StartWith("[ERROR]"));
    }

    [Test]
    public void Clear_RemovesMessagesFromCategory()
    {
        _service.WriteLine("Message 1", OutputCategory.Build);
        _service.WriteLine("Message 2", OutputCategory.Build);
        _service.WriteLine("Message 3", OutputCategory.Debug);

        _service.Clear(OutputCategory.Build);

        Assert.That(_service.GetMessages(OutputCategory.Build), Is.Empty);
        Assert.That(_service.GetMessages(OutputCategory.Debug), Has.Count.EqualTo(1));
    }

    [Test]
    public void ClearAll_RemovesAllMessages()
    {
        _service.WriteLine("Message 1", OutputCategory.Build);
        _service.WriteLine("Message 2", OutputCategory.Debug);
        _service.WriteLine("Message 3", OutputCategory.General);

        _service.ClearAll();

        foreach (OutputCategory category in Enum.GetValues<OutputCategory>())
        {
            Assert.That(_service.GetMessages(category), Is.Empty);
        }
    }

    [Test]
    public void GetMessages_ReturnsReadOnlyList()
    {
        _service.WriteLine("Test", OutputCategory.General);

        var messages = _service.GetMessages(OutputCategory.General);

        Assert.That(messages, Is.InstanceOf<IReadOnlyList<string>>());
    }

    [Test]
    public void OutputReceived_FiresOnWrite()
    {
        OutputEventArgs? receivedArgs = null;
        _service.OutputReceived += (s, e) => receivedArgs = e;

        _service.Write("Test message", OutputCategory.Build);

        Assert.That(receivedArgs, Is.Not.Null);
        Assert.That(receivedArgs!.Message, Is.EqualTo("Test message"));
        Assert.That(receivedArgs.Category, Is.EqualTo(OutputCategory.Build));
        Assert.That(receivedArgs.IsError, Is.False);
    }

    [Test]
    public void OutputReceived_FiresOnWriteLine()
    {
        OutputEventArgs? receivedArgs = null;
        _service.OutputReceived += (s, e) => receivedArgs = e;

        _service.WriteLine("Test message", OutputCategory.Debug);

        Assert.That(receivedArgs, Is.Not.Null);
        Assert.That(receivedArgs!.Category, Is.EqualTo(OutputCategory.Debug));
    }

    [Test]
    public void OutputReceived_FiresOnWriteError()
    {
        OutputEventArgs? receivedArgs = null;
        _service.OutputReceived += (s, e) => receivedArgs = e;

        _service.WriteError("Error message", OutputCategory.Build);

        Assert.That(receivedArgs, Is.Not.Null);
        Assert.That(receivedArgs!.IsError, Is.True);
    }

    [Test]
    public void Activate_DoesNotThrow()
    {
        Assert.DoesNotThrow(() => _service.Activate(OutputCategory.Build));
    }

    [Test]
    public void MultipleWrites_MaintainsOrder()
    {
        _service.Write("First", OutputCategory.General);
        _service.Write("Second", OutputCategory.General);
        _service.Write("Third", OutputCategory.General);

        var messages = _service.GetMessages(OutputCategory.General);

        Assert.That(messages[0], Is.EqualTo("First"));
        Assert.That(messages[1], Is.EqualTo("Second"));
        Assert.That(messages[2], Is.EqualTo("Third"));
    }

    [Test]
    public void DifferentCategories_MaintainSeparation()
    {
        _service.WriteLine("Build message", OutputCategory.Build);
        _service.WriteLine("Debug message", OutputCategory.Debug);

        Assert.That(_service.GetMessages(OutputCategory.Build), Has.Count.EqualTo(1));
        Assert.That(_service.GetMessages(OutputCategory.Debug), Has.Count.EqualTo(1));
        Assert.That(_service.GetMessages(OutputCategory.General), Is.Empty);
    }
}

[TestFixture]
public class OutputEventArgsTests
{
    [Test]
    public void Constructor_SetsProperties()
    {
        var args = new OutputEventArgs("Test", OutputCategory.Build, true);

        Assert.That(args.Message, Is.EqualTo("Test"));
        Assert.That(args.Category, Is.EqualTo(OutputCategory.Build));
        Assert.That(args.IsError, Is.True);
    }

    [Test]
    public void Constructor_DefaultIsErrorFalse()
    {
        var args = new OutputEventArgs("Test", OutputCategory.General);

        Assert.That(args.IsError, Is.False);
    }
}
