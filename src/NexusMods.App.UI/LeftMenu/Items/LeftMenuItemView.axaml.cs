using System.Reactive.Disposables;
using System.Reactive.Linq;
using Avalonia.Controls;
using Avalonia.ReactiveUI;
using ReactiveUI;

namespace NexusMods.App.UI.LeftMenu.Items;

public partial class LeftMenuItemView : ReactiveUserControl<ILeftMenuItemViewModel>
{
    public LeftMenuItemView()
    {
        InitializeComponent();

        this.WhenActivated(d =>
            {
                
                this.OneWayBind(ViewModel, vm => vm.Text.Value, view => view.LabelTextBlock.Text)
                    .DisposeWith(d);
                
                this.WhenAnyValue(view => view.ViewModel!)
                    .Where(vm => vm.IsToggleVisible)
                    .Subscribe(vm => { ToolTip.SetTip(this, vm.Text); })
                    .DisposeWith(d);
                
                this.OneWayBind(ViewModel, vm => vm.Icon, view => view.LeftIcon.Value)
                    .DisposeWith(d);
                
                this.BindCommand(ViewModel, vm => vm.NavigateCommand, view => view.NavButton)
                    .DisposeWith(d);
                
                this.OneWayBind(ViewModel, vm => vm.IsActive, view => view.NavButton.IsActive)
                    .DisposeWith(d);
                
                this.OneWayBind(ViewModel, vm => vm.IsSelected, view => view.NavButton.IsSelected)
                    .DisposeWith(d);
                
                this.OneWayBind(ViewModel, vm => vm.IsToggleVisible, view => view.ToggleSwitch.IsVisible)
                    .DisposeWith(d);
                
                this.OneWayBind(ViewModel, vm => vm.IsEnabled, view => view.ToggleSwitch.IsChecked)
                    .DisposeWith(d);
                
                this.BindCommand(ViewModel, vm => vm.ToggleIsEnabledCommand, view => view.ToggleSwitch)
                    .DisposeWith(d);
            }
        );
    }
}

