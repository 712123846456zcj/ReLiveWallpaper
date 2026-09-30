using Lively.UI.Shared.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lively.UI.WinUI.Views.Pages
{
    /// <summary>
    /// Content of the create/rename library folder dialog.
    /// </summary>
    public sealed partial class FolderEditView : Page
    {
        public FolderEditView(FolderEditViewModel vm)
        {
            this.InitializeComponent();
            this.DataContext = vm;

            // Renaming should not require clearing the existing name first.
            this.Loaded += (sender, e) => DispatcherQueue.TryEnqueue(() =>
            {
                folderNameBox.Focus(FocusState.Programmatic);
                folderNameBox.SelectAll();
            });
        }
    }
}
