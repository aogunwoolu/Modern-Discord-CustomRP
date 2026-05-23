using Avalonia.Threading;
using System;

namespace CustomRP.Modern.Services;

/// <summary>
/// Avalonia does not always install <see cref="SynchronizationContext.Current"/>;
/// Discord RPC callbacks must marshal presence work to the UI thread like WinForms Invoke.
/// </summary>
internal static class UiDispatcher
{
    public static void Invoke(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Invoke(action);
    }

    public static void Post(Action action) => Dispatcher.UIThread.Post(action);
}
