using Lively.Common.JsonConverters;
using Lively.Models.Enums;
using Lively.Models.LivelyControls;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Lively.Common.Helpers
{
    public static class LivelyPropertyUtil
    {
        public delegate Task ExecuteScriptDelegate(string key, object value);

        public static async Task LoadProperty(string propertyPath, string rootDir, ExecuteScriptDelegate execute)
        {
            if (!File.Exists(propertyPath))
                return;

            var controls = GetControls(propertyPath);
            foreach (var control in controls.Values) 
            {
                // Skip, user interaction only.
                if (control is ButtonModel || control is LabelModel)
                    continue;

                object value = control switch
                {
                    SliderModel slider => slider.Value,
                    DropdownModel dropdown => dropdown.Value,
                    ScalerDropdownModel scalerDropdown => scalerDropdown.Value,
                    FolderDropdownModel folderDropdown => GetFolderDropdownValue(folderDropdown, rootDir),
                    CheckboxModel checkbox => checkbox.Value,
                    TextboxModel textbox => textbox.Value,
                    ColorPickerModel colorPicker => colorPicker.Value,
                    _ => throw new NotSupportedException($"Unsupported control type: {control.Type}")
                };

                await execute(control.Name, value);
            }
        }

        public static void LoadProperty(string propertyPath, Action<ControlModel> execute)
        {
            if (!File.Exists(propertyPath))
                return;

            var controls = GetControls(propertyPath);
            foreach (var control in controls.Values)
            {
                // Skip, user interaction only.
                if (control is ButtonModel || control is LabelModel)
                    continue;

                execute(control);
            }
        }

        public static Dictionary<string, ControlModel> GetControls(string propertyPath)
        {
            var jsonSerializerSettings = new JsonSerializerSettings { Converters = new List<JsonConverter> { new LivelyControlModelConverter() } };
            return JsonConvert.DeserializeObject<Dictionary<string, ControlModel>>(File.ReadAllText(propertyPath), jsonSerializerSettings);
        }

        public static void LocalizeControls(string locPath, IDictionary<string, ControlModel> controls, string languageCode = "")
        {
            if (!File.Exists(locPath))
                return;

            LocalizationFile loc;
            try
            {
                loc = JsonConvert.DeserializeObject<LocalizationFile>(File.ReadAllText(locPath));
            }
            catch {
                return;
            }

            if (loc?.Languages is null)
                return;

            // ApplicationLanguages.PrimaryLanguageOverride is empty when not set / use system default.
            languageCode = string.IsNullOrEmpty(languageCode) ? CultureInfo.CurrentUICulture.Name : languageCode;
            // Try exact match first, eg: zh-CN
            if (!loc.Languages.TryGetValue(languageCode, out var lang))
            {
                // Try base language fallback, eg: zh
                var baseLang = languageCode.Split('-')[0];
                if (!loc.Languages.TryGetValue(baseLang, out lang))
                    return;
            }

            // This is faster than iterating over all controls when some controls are not localized.
            foreach (var localized in lang)
            {
                if (!controls.TryGetValue(localized.Key, out var control))
                    continue;

                var value = localized.Value;
                switch (control.Type)
                {
                    case "dropdown":
                    case "scalerDropdown":
                        {
                            if (control is IDropdownItem dropdown && value.Items != null)
                            {
                                var count = Math.Min(dropdown.Items.Length, value.Items.Length);
                                for (int i = 0; i < count; i++)
                                    dropdown.Items[i] = value.Items[i];
                            }
                        }
                        break;
                    case "label":
                        {
                            if (!string.IsNullOrWhiteSpace(value.Value))
                                ((LabelModel)control).Value = value.Value;
                        }
                        break;
                    case "button":
                        {
                            if (!string.IsNullOrWhiteSpace(value.Value))
                                ((ButtonModel)control).Value = value.Value;
                        }
                        break;
                }
                control.Text = string.IsNullOrWhiteSpace(value.Text) ? control.Text : value.Text;
                control.Help = string.IsNullOrWhiteSpace(value.Help) ? control.Help : value.Help;
            }
        }

        /// <summary>
        /// Path of the user editable copy of a wallpaper properties file.
        /// </summary>
        public static string GetPropertyCopyPath(string wallpaperSettingsDir, string wallpaperFolderName, WallpaperArrangement arrangement, int displayIndex)
        {
            return arrangement switch
            {
                WallpaperArrangement.per => Path.Combine(wallpaperSettingsDir, wallpaperFolderName, displayIndex.ToString()),
                WallpaperArrangement.span => Path.Combine(wallpaperSettingsDir, wallpaperFolderName, "span"),
                WallpaperArrangement.duplicate => Path.Combine(wallpaperSettingsDir, wallpaperFolderName, "duplicate"),
                _ => null,
            };
        }

        /// <summary>
        /// Creates the user editable copy of a properties file inside the given directory, or merges in the
        /// entries an existing copy is missing. Values already stored in the copy are never overwritten.
        /// </summary>
        /// <returns>Path of the copy file, null when it does not exist.</returns>
        public static string SyncPropertyFile(string sourcePath, string destinationDirectoryPath, string fileName = "LivelyProperties.json")
        {
            var destinationPath = string.IsNullOrEmpty(destinationDirectoryPath) ? null : Path.Combine(destinationDirectoryPath, fileName);
            try
            {
                if (string.IsNullOrEmpty(sourcePath) || string.IsNullOrEmpty(destinationPath) || !File.Exists(sourcePath))
                    return null;

                if (!File.Exists(destinationPath))
                {
                    Directory.CreateDirectory(destinationDirectoryPath);
                    File.Copy(sourcePath, destinationPath);
                    return destinationPath;
                }

                var source = JObject.Parse(File.ReadAllText(sourcePath));
                var destination = JObject.Parse(File.ReadAllText(destinationPath));
                // Null when the copy is already up to date.
                var merged = MergeEntries(source, destination);
                if (merged is not null)
                    File.WriteAllText(destinationPath, merged.ToString(Formatting.Indented));
            }
            catch { /* Properties file related issue, not fatal. */ }

            return File.Exists(destinationPath) ? destinationPath : null;
        }

        /// <summary>
        /// Adds the entries of source the destination is missing, values the destination already has are kept.
        /// The source order is used, arrays and values are not merged.
        /// </summary>
        /// <returns>Null when the destination is already up to date.</returns>
        private static JObject MergeEntries(JObject source, JObject destination)
        {
            var merged = new JObject();
            foreach (var property in source)
            {
                if (!destination.TryGetValue(property.Key, out var existing))
                {
                    merged.Add(property.Key, property.Value?.DeepClone());
                    continue;
                }

                var value = property.Value is JObject sourceChild && existing is JObject destinationChild ?
                    (JToken)(MergeEntries(sourceChild, destinationChild) ?? existing) : existing;
                merged.Add(property.Key, value.DeepClone());
            }

            // Entries the source does not know about are kept.
            foreach (var property in destination)
            {
                if (!merged.ContainsKey(property.Key))
                    merged.Add(property.Key, property.Value?.DeepClone());
            }

            return IsIdentical(merged, destination) ? null : merged;
        }

        private static bool IsIdentical(JObject left, JObject right) =>
            left.Count == right.Count &&
            left.Properties().Select(x => x.Name).SequenceEqual(right.Properties().Select(x => x.Name)) &&
            JToken.DeepEquals(left, right);

        private static string GetFolderDropdownValue(FolderDropdownModel fd, string rootPath)
        {
            // It is null when no item is selected or file missing.
            var relativeFilePath = fd.Value is null || fd.Folder is null ? null : Path.Combine(fd.Folder, fd.Value);
            var filePath =  relativeFilePath is null ? null : Path.Combine(rootPath, relativeFilePath);
            return File.Exists(filePath) ? relativeFilePath : null;
        }
    }
}
