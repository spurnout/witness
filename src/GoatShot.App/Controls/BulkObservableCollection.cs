using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace GoatShot.App.Controls;

/// <summary>
/// An observable collection whose contents can be swapped with a single Reset notification,
/// instead of one Clear plus one Add event per item that bound lists must each process.
/// </summary>
public sealed class BulkObservableCollection<T> : ObservableCollection<T>
{
    public void ReplaceAll(IEnumerable<T> items)
    {
        CheckReentrancy();
        Items.Clear();
        foreach (var item in items)
        {
            Items.Add(item);
        }

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
