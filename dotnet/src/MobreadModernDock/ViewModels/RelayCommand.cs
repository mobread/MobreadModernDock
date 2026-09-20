using System;
using System.Windows.Input;

namespace MobreadModernDock.ViewModels;

/// <summary>
/// Simple ICommand implementation for manual command creation.
/// (CommunityToolkit.Mvvm's RelayCommand works with source generators,
/// but we need manual instantiation for the dock item click command.)
/// </summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => _execute(parameter);
    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }
}
