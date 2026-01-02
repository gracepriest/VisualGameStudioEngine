using AvaloniaEdit.Document;
using NUnit.Framework;
using VisualGameStudio.Editor.TextMarkers;

namespace VisualGameStudio.Tests.Editor;

[TestFixture]
public class TextMarkerServiceTests
{
    private TextDocument _document = null!;
    private TextMarkerService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _document = new TextDocument("Hello World\nSecond Line\nThird Line");
        _service = new TextMarkerService(_document);
    }

    [Test]
    public void Create_AddsMarkerToList()
    {
        var marker = _service.Create(0, 5, TextMarkerType.Error, "Test error");

        Assert.That(_service.Markers, Has.Count.EqualTo(1));
        Assert.That(_service.Markers[0], Is.SameAs(marker));
    }

    [Test]
    public void Create_ReturnsMarkerWithCorrectProperties()
    {
        var marker = _service.Create(10, 5, TextMarkerType.Warning, "Test warning");

        Assert.That(marker.StartOffset, Is.EqualTo(10));
        Assert.That(marker.Length, Is.EqualTo(5));
        Assert.That(marker.EndOffset, Is.EqualTo(15));
        Assert.That(marker.Type, Is.EqualTo(TextMarkerType.Warning));
        Assert.That(marker.Message, Is.EqualTo("Test warning"));
    }

    [Test]
    public void Create_MultipleMarkers_AddsAll()
    {
        _service.Create(0, 5, TextMarkerType.Error);
        _service.Create(6, 5, TextMarkerType.Warning);
        _service.Create(12, 5, TextMarkerType.Info);

        Assert.That(_service.Markers, Has.Count.EqualTo(3));
    }

    [Test]
    public void Clear_RemovesAllMarkers()
    {
        _service.Create(0, 5, TextMarkerType.Error);
        _service.Create(6, 5, TextMarkerType.Warning);

        _service.Clear();

        Assert.That(_service.Markers, Is.Empty);
    }

    [Test]
    public void Remove_RemovesSpecificMarker()
    {
        var marker1 = _service.Create(0, 5, TextMarkerType.Error);
        var marker2 = _service.Create(6, 5, TextMarkerType.Warning);

        _service.Remove(marker1);

        Assert.That(_service.Markers, Has.Count.EqualTo(1));
        Assert.That(_service.Markers[0], Is.SameAs(marker2));
    }

    [Test]
    public void Remove_NonExistentMarker_DoesNotThrow()
    {
        var marker1 = _service.Create(0, 5, TextMarkerType.Error);
        var marker2 = new TextMarker(10, 5, TextMarkerType.Warning);

        Assert.DoesNotThrow(() => _service.Remove(marker2));
        Assert.That(_service.Markers, Has.Count.EqualTo(1));
    }

    [Test]
    public void RemoveAll_RemovesMatchingMarkers()
    {
        _service.Create(0, 5, TextMarkerType.Error);
        _service.Create(6, 5, TextMarkerType.Error);
        _service.Create(12, 5, TextMarkerType.Warning);

        _service.RemoveAll(m => m.Type == TextMarkerType.Error);

        Assert.That(_service.Markers, Has.Count.EqualTo(1));
        Assert.That(_service.Markers[0].Type, Is.EqualTo(TextMarkerType.Warning));
    }

    [Test]
    public void RemoveAll_NoMatches_RemovesNothing()
    {
        _service.Create(0, 5, TextMarkerType.Error);
        _service.Create(6, 5, TextMarkerType.Warning);

        _service.RemoveAll(m => m.Type == TextMarkerType.Info);

        Assert.That(_service.Markers, Has.Count.EqualTo(2));
    }

    [Test]
    public void GetMarkersAtOffset_ReturnsMarkersContainingOffset()
    {
        _service.Create(0, 10, TextMarkerType.Error);
        _service.Create(5, 10, TextMarkerType.Warning);
        _service.Create(20, 5, TextMarkerType.Info);

        var markersAt7 = _service.GetMarkersAtOffset(7).ToList();

        Assert.That(markersAt7, Has.Count.EqualTo(2));
    }

    [Test]
    public void GetMarkersAtOffset_AtStartOfMarker_ReturnsMarker()
    {
        _service.Create(5, 10, TextMarkerType.Error);

        var markers = _service.GetMarkersAtOffset(5).ToList();

        Assert.That(markers, Has.Count.EqualTo(1));
    }

    [Test]
    public void GetMarkersAtOffset_AtEndOfMarker_ReturnsMarker()
    {
        _service.Create(5, 10, TextMarkerType.Error);

        var markers = _service.GetMarkersAtOffset(15).ToList();

        Assert.That(markers, Has.Count.EqualTo(1));
    }

    [Test]
    public void GetMarkersAtOffset_OutsideAllMarkers_ReturnsEmpty()
    {
        _service.Create(0, 5, TextMarkerType.Error);
        _service.Create(10, 5, TextMarkerType.Warning);

        var markers = _service.GetMarkersAtOffset(7).ToList();

        Assert.That(markers, Is.Empty);
    }

    [Test]
    public void GetMarkersAtOffset_EmptyService_ReturnsEmpty()
    {
        var markers = _service.GetMarkersAtOffset(5).ToList();

        Assert.That(markers, Is.Empty);
    }

    [Test]
    public void Markers_ReturnsReadOnlyList()
    {
        _service.Create(0, 5, TextMarkerType.Error);

        var markers = _service.Markers;

        Assert.That(markers, Is.InstanceOf<IReadOnlyList<TextMarker>>());
    }

    [Test]
    public void Create_WithNullMessage_CreatesMarkerWithNullMessage()
    {
        var marker = _service.Create(0, 5, TextMarkerType.Error, null);

        Assert.That(marker.Message, Is.Null);
    }

    [Test]
    public void Create_WithEmptyMessage_CreatesMarkerWithEmptyMessage()
    {
        var marker = _service.Create(0, 5, TextMarkerType.Error, "");

        Assert.That(marker.Message, Is.EqualTo(""));
    }

    [Test]
    public void TextMarker_EndOffset_CalculatesCorrectly()
    {
        var marker = new TextMarker(10, 15, TextMarkerType.Error);

        Assert.That(marker.EndOffset, Is.EqualTo(25));
    }

    [Test]
    public void TextMarker_ZeroLength_HasSameStartAndEnd()
    {
        var marker = new TextMarker(10, 0, TextMarkerType.Error);

        Assert.That(marker.EndOffset, Is.EqualTo(10));
    }
}

[TestFixture]
public class TextMarkerTests
{
    [Test]
    public void Constructor_SetsPropertiesCorrectly()
    {
        var marker = new TextMarker(10, 20, TextMarkerType.Warning, "Test message");

        Assert.That(marker.StartOffset, Is.EqualTo(10));
        Assert.That(marker.Length, Is.EqualTo(20));
        Assert.That(marker.EndOffset, Is.EqualTo(30));
        Assert.That(marker.Type, Is.EqualTo(TextMarkerType.Warning));
        Assert.That(marker.Message, Is.EqualTo("Test message"));
    }

    [Test]
    public void Type_Error_IsCorrect()
    {
        var marker = new TextMarker(0, 10, TextMarkerType.Error);
        Assert.That(marker.Type, Is.EqualTo(TextMarkerType.Error));
    }

    [Test]
    public void Type_Warning_IsCorrect()
    {
        var marker = new TextMarker(0, 10, TextMarkerType.Warning);
        Assert.That(marker.Type, Is.EqualTo(TextMarkerType.Warning));
    }

    [Test]
    public void Type_Info_IsCorrect()
    {
        var marker = new TextMarker(0, 10, TextMarkerType.Info);
        Assert.That(marker.Type, Is.EqualTo(TextMarkerType.Info));
    }

    [Test]
    public void Type_Hint_IsCorrect()
    {
        var marker = new TextMarker(0, 10, TextMarkerType.Hint);
        Assert.That(marker.Type, Is.EqualTo(TextMarkerType.Hint));
    }
}
