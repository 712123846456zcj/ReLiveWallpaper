using CommunityToolkit.Mvvm.Input;
using System;

namespace Lively.UI.Shared.ViewModels
{
    /// <summary>
    /// Picture selected for a collage wallpaper.
    /// </summary>
    public class CollageImageViewModel
    {
        public CollageImageViewModel(string path, Action<CollageImageViewModel> remove, Action<CollageImageViewModel, int> move)
        {
            Path = path;
            FileName = System.IO.Path.GetFileName(path);
            RemoveCommand = new RelayCommand(() => remove(this));
            MoveUpCommand = new RelayCommand(() => move(this, -1));
            MoveDownCommand = new RelayCommand(() => move(this, 1));
        }

        public string Path { get; }

        public string FileName { get; }

        public RelayCommand RemoveCommand { get; }

        public RelayCommand MoveUpCommand { get; }

        public RelayCommand MoveDownCommand { get; }
    }
}
