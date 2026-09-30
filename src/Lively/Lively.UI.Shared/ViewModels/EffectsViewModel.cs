using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lively.Common.Helpers;
using Lively.Common.Services;
using Lively.Grpc.Client;
using Lively.Models.LivelyControls;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Lively.UI.Shared.ViewModels
{
    /// <summary>
    /// "Effects" tab: overlay effects drawn on top of whatever wallpaper is running.
    /// </summary>
    public partial class EffectsViewModel : ObservableObject
    {
        private const string PropertyFileName = "LivelyProperties.json";
        private const string LocalizationFileName = "LivelyProperties.loc.json";

        private readonly IDesktopCoreClient desktopCore;
        private readonly IUserSettingsClient userSettings;
        private readonly IResourceService i18n;
        private readonly IDispatcherService dispatcher;
        /// <summary>Maps a rendered control back to the effect it belongs to.</summary>
        private readonly Dictionary<ControlModel, EffectItemViewModel> controlOwners = new Dictionary<ControlModel, EffectItemViewModel>();

        public EffectsViewModel(IDesktopCoreClient desktopCore,
            IUserSettingsClient userSettings,
            IResourceService i18n,
            IDispatcherService dispatcher)
        {
            this.desktopCore = desktopCore;
            this.userSettings = userSettings;
            this.i18n = i18n;
            this.dispatcher = dispatcher;

            _ = InitializeAsync();
        }

        public ObservableCollection<EffectItemViewModel> Effects { get; } = new ObservableCollection<EffectItemViewModel>();

        [ObservableProperty]
        private bool isLoaded;

        [ObservableProperty]
        private string errorText;

        private async Task InitializeAsync()
        {
            try
            {
                var effects = await desktopCore.GetEffects();
                var items = effects
                    .Select(x => new EffectItemViewModel(this, desktopCore, i18n, x, userSettings.Settings.Language))
                    .ToList();

                // Rendered by the effect the item belongs to, controls are not aware of their owner.
                foreach (var item in items)
                {
                    foreach (var control in item.Controls)
                        controlOwners[control] = item;
                }

                _ = dispatcher.TryEnqueue(() =>
                {
                    foreach (var item in items)
                        Effects.Add(item);

                    IsLoaded = true;
                });
            }
            catch (Exception e)
            {
                ErrorText = e.Message;
            }
        }

        internal void RegisterControls(EffectItemViewModel item)
        {
            foreach (var control in item.Controls)
                controlOwners[control] = item;
        }

        [RelayCommand]
        private void SliderValueChanged(ControlModel control) => ApplyProperty(control);

        [RelayCommand]
        private void CheckboxValueChanged(ControlModel control) => ApplyProperty(control);

        [RelayCommand]
        private void TextboxValueChanged(ControlModel control) => ApplyProperty(control);

        [RelayCommand]
        private void DropdownValueChanged(ControlModel control) => ApplyProperty(control);

        private void ApplyProperty(ControlModel control)
        {
            if (control is null || !controlOwners.TryGetValue(control, out var item))
                return;

            object value = control switch
            {
                SliderModel slider => slider.Value,
                CheckboxModel checkbox => checkbox.Value,
                TextboxModel textbox => textbox.Value,
                DropdownModel dropdown => dropdown.Value,
                ColorPickerModel colorPicker => colorPicker.Value,
                _ => null,
            };

            if (value is null)
                return;

            item.SetProperty(control.Name, JsonConvert.SerializeObject(value));
        }
    }

    public partial class EffectItemViewModel : ObservableObject
    {
        private const string LocalizationFileName = "LivelyProperties.loc.json";

        private readonly EffectsViewModel owner;
        private readonly IDesktopCoreClient desktopCore;
        private readonly IResourceService i18n;
        private readonly string language;
        private bool isLoading;

        public EffectItemViewModel(EffectsViewModel owner,
            IDesktopCoreClient desktopCore,
            IResourceService i18n,
            EffectInfo effect,
            string language)
        {
            this.owner = owner;
            this.desktopCore = desktopCore;
            this.i18n = i18n;
            this.language = language;

            Id = effect.Id;
            IsAvailable = effect.IsAvailable;
            PropertyPath = effect.PropertyPath;
            Name = GetName(effect.Id);
            Glyph = GetGlyph(effect.Id);

            LoadControls();

            isLoading = true;
            IsEnabled = effect.IsEnabled;
            isLoading = false;
        }

        public string Id { get; }

        public string Name { get; }

        public string Glyph { get; }

        /// <summary>False for effects that are still on the todo list.</summary>
        public bool IsAvailable { get; }

        public bool IsComingSoon => !IsAvailable;

        public string ComingSoonText => i18n.GetString("ComingSoon/Text");

        public string PropertyPath { get; }

        public ObservableCollection<ControlModel> Controls { get; } = new ObservableCollection<ControlModel>();

        public bool HasControls => Controls.Count != 0;

        [ObservableProperty]
        private bool isEnabled;

        partial void OnIsEnabledChanged(bool value)
        {
            if (isLoading)
                return;

            _ = desktopCore.SetEffect(Id, value);
        }

        public void SetProperty(string key, string value)
        {
            if (string.IsNullOrEmpty(PropertyPath))
                return;

            _ = desktopCore.SetEffectProperty(Id, key, value);
        }

        [RelayCommand]
        private async Task RestoreDefaults()
        {
            if (string.IsNullOrEmpty(PropertyPath))
                return;

            await desktopCore.ResetEffectProperties(Id);
            LoadControls();
            owner.RegisterControls(this);
        }

        /// <summary>Reads the effect controls, also used to refresh after restoring the defaults.</summary>
        private void LoadControls()
        {
            Controls.Clear();

            try
            {
                if (string.IsNullOrEmpty(PropertyPath) || !File.Exists(PropertyPath))
                    return;

                var controls = LivelyPropertyUtil.GetControls(PropertyPath);
                var localizationPath = Path.Combine(Path.GetDirectoryName(PropertyPath), LocalizationFileName);
                LivelyPropertyUtil.LocalizeControls(localizationPath, controls, language ?? string.Empty);

                foreach (var control in controls.Values)
                {
                    // Buttons are not rendered, the card has its own restore button.
                    if (control is ButtonModel)
                        continue;

                    Controls.Add(control);
                }
            }
            catch (Exception)
            {
                // A broken properties file should never take the tab down.
            }

            OnPropertyChanged(nameof(HasControls));
        }

        private string GetName(string effectId)
        {
            var key = "Effect" + effectId.Substring(0, 1).ToUpperInvariant() + effectId.Substring(1);
            var localized = i18n.GetString(key + "/Text");
            return string.IsNullOrWhiteSpace(localized) ? effectId : localized;
        }

        private static string GetGlyph(string effectId)
        {
            return effectId switch
            {
                "rain" => "\uEB42",
                "smoke" => "\uEA37",
                "sparks" => "\uEA38",
                _ => "\uE9E9",
            };
        }
    }
}
