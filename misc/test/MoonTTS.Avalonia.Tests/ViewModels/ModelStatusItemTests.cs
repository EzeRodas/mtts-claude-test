using System.ComponentModel;
using MoonTTS.Avalonia.ViewModels;
using MoonTTS.Domain.ValueObjects;

namespace MoonTTS.Avalonia.Tests.ViewModels;

/* Verifies the observable row of the Engine Management tab */
public sealed class ModelStatusItemTests
{
    /* Records the names of the properties the item reports as changed */
    private static List<string?> RecordChanges(ModelStatusItem item)
    {
        var changed = new List<string?>();
        ((INotifyPropertyChanged)item).PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        return changed;
    }

    /* A new row has no model, no name, no progress and is neither installed nor downloading */
    [Fact]
    public void Ctor_Defaults_AreEmpty()
    {
        var item = new ModelStatusItem();

        Assert.Equal((ModelId.Unselected, string.Empty, false, false, 0.0),
            (item.Model, item.Name, item.IsDownloaded, item.IsDownloading, item.Progress));
    }

    /* Changing the model notifies the bindings */
    [Fact]
    public void Model_Changed_RaisesPropertyChanged()
    {
        var item = new ModelStatusItem();
        var changed = RecordChanges(item);

        item.Model = new ModelId("engine", "model");

        Assert.Equal([nameof(ModelStatusItem.Model)], changed);
    }

    /* Changing the name notifies the bindings */
    [Fact]
    public void Name_Changed_RaisesPropertyChanged()
    {
        var item = new ModelStatusItem();
        var changed = RecordChanges(item);

        item.Name = "Kokoro";

        Assert.Equal([nameof(ModelStatusItem.Name)], changed);
    }

    /* Changing the installed flag notifies the bindings (badge and Download/Delete buttons) */
    [Fact]
    public void IsDownloaded_Changed_RaisesPropertyChanged()
    {
        var item = new ModelStatusItem();
        var changed = RecordChanges(item);

        item.IsDownloaded = true;

        Assert.Equal([nameof(ModelStatusItem.IsDownloaded)], changed);
    }

    /* Changing the downloading flag notifies the bindings (progress bar visibility) */
    [Fact]
    public void IsDownloading_Changed_RaisesPropertyChanged()
    {
        var item = new ModelStatusItem();
        var changed = RecordChanges(item);

        item.IsDownloading = true;

        Assert.Equal([nameof(ModelStatusItem.IsDownloading)], changed);
    }

    /* Changing the progress notifies the bindings */
    [Fact]
    public void Progress_Changed_RaisesPropertyChanged()
    {
        var item = new ModelStatusItem();
        var changed = RecordChanges(item);

        item.Progress = 42;

        Assert.Equal([nameof(ModelStatusItem.Progress)], changed);
    }

    /* Assigning the current value again raises nothing */
    [Fact]
    public void Progress_SameValue_RaisesNothing()
    {
        var item = new ModelStatusItem { Progress = 42 };
        var changed = RecordChanges(item);

        item.Progress = 42;

        Assert.Empty(changed);
    }
}
