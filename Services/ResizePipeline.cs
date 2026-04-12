using System;
using Microsoft.UI.Dispatching;

namespace OpenPatro.Services;

/// <summary>
/// Debounced, single-pipeline resize handler.  Multiple rapid resize events
/// (e.g. window drag, maximize/restore transitions) are coalesced into a single
/// invocation of the registered callback, avoiding redundant layout recalculations.
/// </summary>
public sealed class ResizePipeline : IDisposable
{
    /// <summary>Default debounce interval in milliseconds.</summary>
    private const int DefaultDebounceMs = 30;

    private readonly DispatcherQueueTimer _timer;
    private readonly Action _callback;

    /// <summary>
    /// Creates a new resize pipeline that invokes <paramref name="callback"/>
    /// after resize events have settled for <paramref name="debounceMs"/> milliseconds.
    /// Must be created on the UI thread.
    /// </summary>
    public ResizePipeline(DispatcherQueue dispatcherQueue, Action callback, int debounceMs = DefaultDebounceMs)
    {
        _callback = callback ?? throw new ArgumentNullException(nameof(callback));
        _timer = dispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(debounceMs);
        _timer.IsRepeating = false;
        _timer.Tick += OnTimerTick;
    }

    /// <summary>
    /// Call this from every resize-related event.  The actual callback will only run
    /// once after the events stop firing for the debounce duration.
    /// </summary>
    public void RequestLayout()
    {
        // Restart the timer each time a new resize event arrives.
        _timer.Stop();
        _timer.Start();
    }

    private void OnTimerTick(DispatcherQueueTimer sender, object args)
    {
        _timer.Stop();
        _callback();
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= OnTimerTick;
    }
}
