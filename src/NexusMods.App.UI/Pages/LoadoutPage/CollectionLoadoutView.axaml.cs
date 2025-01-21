using System.Reactive.Disposables;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.ReactiveUI;
using NexusMods.App.UI.Controls;
using NexusMods.MnemonicDB.Abstractions;
using R3;
using ReactiveUI;

namespace NexusMods.App.UI.Pages.LoadoutPage;

public partial class CollectionLoadoutView : ReactiveUserControl<ICollectionLoadoutViewModel>
{
    public CollectionLoadoutView()
    {
        InitializeComponent();

        TreeDataGridViewHelper.SetupTreeDataGridAdapter<CollectionLoadoutView, ICollectionLoadoutViewModel, LoadoutItemModel, EntityId>(this, TreeDataGrid, vm => vm.Adapter);

        this.WhenActivated(disposables =>
        {
            this.OneWayBind(ViewModel, vm => vm.Adapter.Source.Value, view => view.TreeDataGrid.Source)
                .AddTo(disposables);
            this.OneWayBind(ViewModel, vm => vm.Name, view => view.CollectionName.Text)
                .AddTo(disposables);
            this.OneWayBind(ViewModel, vm => vm.TileImage, view => view.CollectionImage.Source)
                .AddTo(disposables);
            
            this.WhenAnyValue(view => view.ViewModel!.TileImage)
                .Subscribe(image =>
                {
                    CollectionImageBorder.IsVisible = image != null;
                    CollectionImage.Source = image;
                })
                .DisposeWith(disposables);
            
            // this.OneWayBind(ViewModel, vm => vm.BackgroundImage, view => view.BackgroundImage.Source)
            //     .AddTo(disposables);
            this.OneWayBind(ViewModel, vm => vm.AuthorAvatar, view => view.AuthorAvatar.Source)
                .AddTo(disposables);
            this.OneWayBind(ViewModel, vm => vm.RevisionNumber, view => view.Revision.Text, revision => $"Revision {revision}")
                .DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.AuthorName, view => view.AuthorName.Text)
                .AddTo(disposables);
            this.BindCommand(ViewModel, vm => vm.CommandToggle, view => view.CollectionToggle)
                .AddTo(disposables);
            this.OneWayBind(ViewModel, vm => vm.IsCollectionEnabled, view => view.CollectionToggle.IsChecked)
                .AddTo(disposables);
            
            this.WhenAnyValue(view => view.ViewModel!.BackgroundImage)
                .WhereNotNull()
                .SubscribeWithErrorLogging(image => HeaderBorderBackground.Background = new ImageBrush { Source = image, Stretch = Stretch.UniformToFill, AlignmentY = AlignmentY.Top})
                .DisposeWith(disposables);
        });
    }
}
