using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace OpenPatro.Infrastructure;

/// <summary>
/// An <see cref="ObservableCollection{T}"/> that can swap its entire contents with a
/// single change notification. The stock Clear()+Add-loop pattern fires one UI reset
/// per item (a 42-cell month rebuild re-layouts bound controls 43 times); building a
/// plain list and calling <see cref="ReplaceAll"/> collapses that to one layout pass.
/// </summary>
public sealed class BulkObservableCollection<T> : ObservableCollection<T>
{
    public BulkObservableCollection()
    {
    }

    public BulkObservableCollection(IEnumerable<T> items)
        : base(items)
    {
    }

    /// <summary>
    /// Replaces the entire contents and raises a single Reset notification plus the
    /// standard Count/Item[] property notifications. Bound controls rebuild once.
    /// Must be called on the UI thread, like any ObservableCollection mutation.
    /// </summary>
    public void ReplaceAll(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
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
