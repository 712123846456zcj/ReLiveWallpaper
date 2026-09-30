using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lively.Common.Services;
using System.Threading.Tasks;

namespace Lively.UI.Shared.ViewModels
{
    /// <summary>
    /// Create or edit library folder dialog.
    /// </summary>
    public partial class FolderEditViewModel : ObservableObject
    {
        private static readonly string[] CoverImageExtensions = [".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp", ".tif", ".tiff"];

        private readonly IResourceService i18n;
        private readonly IFileService fileService;

        public FolderEditViewModel(IResourceService i18n, IFileService fileService)
        {
            this.i18n = i18n;
            this.fileService = fileService;
        }

        private string _name;
        public string Name
        {
            get => _name;
            set
            {
                if (SetProperty(ref _name, value))
                    OnPropertyChanged(nameof(CanSave));
            }
        }

        private string _coverImage;
        public string CoverImage
        {
            get => _coverImage;
            set
            {
                if (SetProperty(ref _coverImage, value))
                    OnPropertyChanged(nameof(HasCoverImage));
            }
        }

        public bool CanSave => !string.IsNullOrWhiteSpace(Name);

        public bool HasCoverImage => !string.IsNullOrWhiteSpace(CoverImage);

        [RelayCommand]
        private async Task BrowseCoverImage()
        {
            var files = await fileService.PickFileAsync([(i18n.GetString("TextPicture"), CoverImageExtensions)]);
            if (files.Count != 0)
                CoverImage = files[0];
        }

        [RelayCommand]
        private void ClearCoverImage()
        {
            CoverImage = null;
        }
    }
}
