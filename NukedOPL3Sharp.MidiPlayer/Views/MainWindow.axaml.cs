using Avalonia.Controls;
using Avalonia.Input;
using NukedOPL3Sharp.MidiPlayer.ViewModels;

namespace NukedOPL3Sharp.MidiPlayer.Views;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void TracksList_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        if (sender is not ListBox lb || lb.SelectedItem is null)
        {
            return;
        }

        if (vm.PlayCommand.CanExecute(null))
        {
            vm.PlayCommand.Execute(null);
        }
    }
}