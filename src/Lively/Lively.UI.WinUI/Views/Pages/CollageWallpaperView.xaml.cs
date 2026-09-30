using Lively.UI.Shared.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Lively.UI.WinUI.Views.Pages
{
    /// <summary>
    /// Content of the multi picture collage dialog.
    /// </summary>
    public sealed partial class CollageWallpaperView : Page
    {
        public CollageWallpaperView(CollageWallpaperViewModel vm)
        {
            this.InitializeComponent();
            this.DataContext = vm;
        }
    }
}
