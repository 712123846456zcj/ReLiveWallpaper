using Lively.UI.Shared.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace Lively.UI.WinUI.Views.Pages
{
    public sealed partial class EffectsView : Page
    {
        public EffectsView()
        {
            this.InitializeComponent();
            this.DataContext = App.Services.GetRequiredService<EffectsViewModel>();
        }
    }
}
