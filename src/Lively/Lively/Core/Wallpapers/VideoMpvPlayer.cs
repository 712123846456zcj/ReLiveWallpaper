using ImageMagick;
using Lively.Common;
using Lively.Common.Exceptions;
using Lively.Common.Extensions;
using Lively.Common.Helpers;
using Lively.Common.Helpers.IPC;
using Lively.Models;
using Lively.Models.Enums;
using Lively.Models.LivelyControls;
using Lively.Models.Message;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Interop;

namespace Lively.Core.Wallpapers
{
    /// <summary>
    /// Mpv video player
    /// <br>References:</br>
    /// <br> https://github.com/mpv-player/mpv/blob/master/DOCS/man/ipc.rst </br>
    /// <br> https://mpv.io/manual/master/  </br>
    /// </summary>
    public class VideoMpvPlayer : IWallpaper
    {
        /// <summary>
        /// Mpv player json ipc command.
        /// </summary>
        private class MpvCommand
        {
            [JsonProperty("command")]
            public List<object> Command { get; } = new List<object>();
        }

        /// <summary>
        /// Background fill controls are handled by this player, mpv has no matching properties.
        /// </summary>
        private const string BackgroundColorProperty = "backgroundColor";
        private const string BackgroundAutoProperty = "backgroundColorAuto";
        private const string DefaultBackgroundColor = "#000000";

        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
        private readonly CancellationTokenSource ctsProcessWait = new();
        private string userBackgroundColor = DefaultBackgroundColor;
        private bool isBackgroundAuto = true;
        private string sampledBackgroundColor;
        private bool isBackgroundColorSampled;
        private Task<IntPtr> processWaitTask;
        private readonly Process process;
        private readonly int timeOut;
        private readonly string ipcServerName;
        private bool isVideoStopped;
        private static int globalCount;
        private readonly int uniqueId;
        private int? exitCode;

        public event EventHandler Exited;
        public event EventHandler Loaded;

        public string LivelyPropertyCopyPath { get; }

        public bool IsLoaded { get; private set; } = false;

        public int? Pid { get; private set; } = null;

        public WallpaperType Category => Model.LivelyInfo.Type;

        public LibraryModel Model { get; }

        public IntPtr Handle { get; private set; }

        public IntPtr InputHandle => IntPtr.Zero;

        public DisplayMonitor Screen { get; set; }

        public bool IsExited { get; private set; }

        public VideoMpvPlayer(string path,
            LibraryModel model,
            DisplayMonitor display,
            string livelyPropertyPath,
            bool isHwAccel = true,
            bool isWindowed = false,
            TargetColorspaceHintMode colorSpaceMode = TargetColorspaceHintMode.target)
        {
            LivelyPropertyCopyPath = livelyPropertyPath;

            ipcServerName = "mpvsocket" + Path.GetRandomFileName();
            var configDir = GetConfigDir();

            var cmdArgs = new StringBuilder();
            // Startup volume will be 0
            cmdArgs.Append("--volume=0 ");
            // Disable progress message, ref: https://mpv.io/manual/master/#options-msg-level
            cmdArgs.Append("--msg-level=all=info ");
            // Alternative: --loop-file=inf
            cmdArgs.Append("--loop-file ");
            // Do not close after media end
            cmdArgs.Append("--keep-open ");
            // Disable SystemMediaTransportControls, Jul 2024 change: https://github.com/mpv-player/mpv/pull/14338
            cmdArgs.Append("--media-controls=no ");
            //Open window at (-9999,0)
            cmdArgs.Append("--geometry=-9999:0 ");
            // Always create gui window
            cmdArgs.Append("--force-window=yes ");
            // Don't move the window when clicking
            cmdArgs.Append("--no-window-dragging ");
            // Don't hide cursor after sometime.
            cmdArgs.Append("--cursor-autohide=no ");
            // Start without focused
            cmdArgs.Append("--window-minimized=yes ");
            // Allow windows screensaver
            cmdArgs.Append("--stop-screensaver=no ");
            // Disable mpv default (built-in) key bindings
            cmdArgs.Append("--input-default-bindings=no ");
            // Win11 24H2 and new mpv builds alignment fix, ref: https://github.com/rocksdanister/lively/issues/2415
            cmdArgs.Append(!isWindowed ? "--no-border " : "--border=yes ");
            // Permit mpv to receive pointer events reported by the video output driver. Necessary to use the OSC, or to select the buttons in DVD menus. 
            cmdArgs.Append("--input-cursor=no ");
            // On-screen-controller visibility
            cmdArgs.Append("--no-osc ");
            // Alternative: --input-ipc-server=\\.\pipe\
            cmdArgs.Append("--input-ipc-server=" + ipcServerName + " ");
            // Integer scaler for sharpness
            cmdArgs.Append(model.LivelyInfo.Type == WallpaperType.gif ? "--scale=nearest " : " ");
            // Paint the area not covered by the media with --background-color instead of leaving it black.
            cmdArgs.Append("--background=color ");
            // GPU decode preference
            cmdArgs.Append(isHwAccel ? "--hwdec=auto-safe " : "--hwdec=no ");
            // Select which metadata to use for the --target-colorspace-hint, requires gpu-next vo.
            cmdArgs.Append($"--target-colorspace-hint-mode={GetMpvTargetColorSpace(colorSpaceMode)} ");
            // Avoid global config file %APPDATA%\mpv\mpv.conf
            cmdArgs.Append(configDir is not null ? "--config-dir=" + "\"" + configDir + "\" " : "--no-config ");
            // File or online video stream path
            cmdArgs.Append("\"" + path + "\"");

            this.process = new Process()
            {
                EnableRaisingEvents = true,
                StartInfo = new ProcessStartInfo
                {
                    FileName = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Constants.PlayerPartialPaths.MpvPath),
                    UseShellExecute = false,
                    RedirectStandardError = false,
                    RedirectStandardInput = false,
                    RedirectStandardOutput = true,
                    WorkingDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Constants.PlayerPartialPaths.MpvDir),
                    Arguments = cmdArgs.ToString(),
                },
            };
            this.Model = model;
            this.Screen = display;
            this.timeOut = 20000;

            //for logging purpose
            uniqueId = globalCount++;
        }

        public async void Close()
        {
            if (IsExited)
                return;

            ctsProcessWait.TaskWaitCancel();
            while (!processWaitTask.IsTaskWaitCompleted())
                await Task.Delay(1);

            // Proc.CloseMainWindow() does not work?
            SendMessage("{\"command\":[\"quit\"]}\n");
        }

        public void Play()
        {
            if (isVideoStopped)
            {
                isVideoStopped = false;
                //is this always the correct channel for main video?
                SendMessage("{\"command\":[\"set_property\",\"vid\",1]}\n");
            }
            SendMessage("{\"command\":[\"set_property\",\"pause\",false]}\n");
        }

        public void Pause()
        {
            SendMessage("{\"command\":[\"set_property\",\"pause\",true]}\n");
        }

        //private void Stop()
        //{
        //    isVideoStopped = true;
        //    //video=no disable video but audio can still be played,
        //    //which is useful for 'play audio only' option in the future.
        //    SendMessage("{\"command\":[\"set_property\",\"vid\",\"no\"]}\n");
        //    Pause();
        //}

        public void SetVolume(int volume)
        {
            SendMessage("{\"command\":[\"set_property\",\"volume\"," + JsonConvert.SerializeObject(volume) + "]}\n");
        }

        public void SetMute(bool mute)
        {
            // Assume default track is 1.
            // We use mute as part of LivelyProperties, so disable track instead.
            if (mute)
                SendMessage("{\"command\":[\"set_property\",\"aid\",\"no\"]}\n");
            else
                SendMessage("{\"command\":[\"set_property\",\"aid\",\"1\"]}\n");
        }

        public void SetPlaybackPos(float pos, PlaybackPosType type)
        {
            if (Category != WallpaperType.picture)
            {
                var posStr = JsonConvert.SerializeObject(pos);
                switch (type)
                {
                    case PlaybackPosType.absolutePercent:
                        SendMessage("{\"command\":[\"seek\"," + posStr + ",\"absolute-percent\"]}\n");
                        break;
                    case PlaybackPosType.relativePercent:
                        SendMessage("{\"command\":[\"seek\"," + posStr + ",\"relative-percent\"]}\n");
                        break;
                }
            }
        }

        private void SetLivelyProperties(string propertyPath)
        {
            try
            {
                LivelyPropertyUtil.LoadProperty(propertyPath, (control) =>
                {
                    if (TryApplyBackgroundProperty(control))
                        return;

                    switch (control)
                    {
                        case SliderModel sliderModel:
                            // Mpv is strongly typed; sending decimal value for integer commands fails.
                            var isFraction = (sliderModel.Step % 1) != 0;
                            SendMessage(GetMpvCommand("set_property", 
                                sliderModel.Name,
                                isFraction ? sliderModel.Value : Convert.ToInt32(sliderModel.Value)));
                            break;
                        case CheckboxModel checkbox:
                            SendMessage(GetMpvCommand("set_property", checkbox.Name, checkbox.Value));
                            break;
                        case ScalerDropdownModel scalerDropdown:
                            var scaler = (WallpaperScaler)scalerDropdown.Value;
                            UpdateScaler(scaler);
                            break;
                    }
                });
            }
            catch (Exception ex)
            {
                Logger.Error(ex);
            }
        }

        public async Task ScreenCapture(string filePath)
        {
            if (Category == WallpaperType.gif)
            {
                await Task.Run(() =>
                {
                    // Read first frame of gif image
                    using var image = new MagickImage(Model.FilePath);
                    if (image.Width < 1920)
                    {
                        // If the image is too small then resize to min: 1080p using integer scaling for sharpness.
                        image.FilterType = FilterType.Point;
                        image.Thumbnail(new Percentage(100 * 1920 / image.Width));
                    }
                    image.Write(Path.GetExtension(filePath) != ".jpg" ? filePath + ".jpg" : filePath);
                });
            }
            else
            {
                var tcs = new TaskCompletionSource();
                void LocalOutputDataReceived(object sender, DataReceivedEventArgs e)
                {
                    if (string.IsNullOrEmpty(e.Data))
                    {
                        tcs.TrySetException(new InvalidOperationException("Process exited unexpectedly."));
                    }        
                    else if (e.Data.Contains("Screenshot:"))
                    {
                        // Screenshot: 'path'
                        var match = Regex.Match(e.Data, @"Screenshot: '([^']+)'");
                        if (match.Success && match.Groups[1].Value.Equals(filePath, StringComparison.OrdinalIgnoreCase))
                        {
                            process.OutputDataReceived -= LocalOutputDataReceived;
                            tcs.TrySetResult();
                        }
                    }
                }
                process.OutputDataReceived += LocalOutputDataReceived;

                Logger.Info($"Mpv{uniqueId}: Taking screenshot: {filePath}");
                SendMessage(GetMpvCommand("screenshot-to-file", filePath));

                // Timeout
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                using (cts.Token.Register(() => 
                {
                    if (!IsExited)
                        process.OutputDataReceived -= LocalOutputDataReceived;

                    tcs.TrySetException(new TimeoutException($"Screenshot timed out."));
                }))

                await tcs.Task;
            }
        }

        public async Task ShowAsync()
        {
            if (process is null)
                return;

            try
            {
                process.Exited += Proc_Exited;
                process.OutputDataReceived += Proc_OutputDataReceived;
                process.Start();
                Pid = process.Id;
                process.BeginOutputReadLine();

                processWaitTask = process.WaitForProcesWindow(timeOut, ctsProcessWait.Token, true);
                this.Handle = await processWaitTask;

                if (Handle.Equals(IntPtr.Zero))
                    throw new InvalidOperationException("Process window handle is null.");

                //Program ready!
                //TaskView crash fix
                WindowUtil.BorderlessWinStyle(Handle);
                WindowUtil.RemoveWindowFromTaskbar(Handle);

                //Restore livelyproperties.json settings
                SetLivelyProperties(LivelyPropertyCopyPath);
                //Wait a bit for properties to apply.
                //Todo: check ipc mgs and do this properly.
                await Task.Delay(69);

                IsLoaded = true;
                Loaded?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception)
            {
                if (IsExited) {
                    throw GetMpvException(exitCode);
                }
                else 
                {
                    Terminate();

                    throw;
                }
            }
        }

        private void Proc_Exited(object sender, EventArgs e)
        {
            exitCode = process?.ExitCode;
            Logger.Info($"Mpv{uniqueId}: Process exited with exit code: {exitCode}");
            process.OutputDataReceived -= Proc_OutputDataReceived;
            process?.Dispose();
            IsExited = true;
            Exited?.Invoke(this, EventArgs.Empty);
        }

        private void Proc_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                Logger.Info($"Mpv{uniqueId}: {e.Data}");
            }
        }

        public void Terminate()
        {
            if (IsExited)
                return;

            try
            {
                process.Kill();
            }
            catch { }
        }

        private void SendMessage(string msg)
        {
            if (IsExited)
                return;

            try
            {
                PipeClient.SendMessage(ipcServerName, msg);
            }
            catch { }
        }

        public void SendMessage(IpcMessage obj)
        {
            // TODO: 
            // Test and see if what all Lively controls are required based on available options: https://mpv.io/manual/master/
            // Maybe block some commands? like a blacklist
            try
            {
                string msg = null;
                switch (obj.Type)
                {
                    case MessageType.lp_slider:
                        {
                            var sl = (LivelySlider)obj;
                            // Mpv is strongly typed; sending decimal value for integer commands fails.
                            var isFraction = (sl.Step % 1) != 0;
                            msg = GetMpvCommand("set_property", sl.Name, isFraction ? sl.Value : Convert.ToInt32(sl.Value));
                        }
                        break;
                    case MessageType.lp_chekbox:
                        {
                            var chk = (LivelyCheckbox)obj;
                            if (TryApplyBackgroundProperty(chk.Name, chk.Value))
                                break;
                            msg = GetMpvCommand("set_property", chk.Name, chk.Value);
                        }
                        break;
                    case MessageType.lp_button:
                        {
                            var btn = (LivelyButton)obj;
                            if (btn.IsDefault)
                            {
                                SetLivelyProperties(LivelyPropertyCopyPath);
                            }
                            else { } //unused
                        }
                        break;
                    case MessageType.lp_dropdown:
                        //todo
                        break;
                    case MessageType.lp_textbox:
                        //todo
                        break;
                    case MessageType.lp_cpicker:
                        {
                            var picker = (LivelyColorPicker)obj;
                            TryApplyBackgroundProperty(picker.Name, picker.Value);
                        }
                        break;
                    case MessageType.lp_fdropdown:
                        //todo
                        break;
                    case MessageType.lp_dropdown_scaler:
                        {
                            var sl = (LivelyDropdownScaler)obj;
                            var scaler = (WallpaperScaler)sl.Value;
                            UpdateScaler(scaler);
                        }
                        break;
                }

                if (msg != null)
                {
                    SendMessage(msg);
                }
            }
            catch (OverflowException)
            {
                Logger.Error("Mpv{0}: Slider double -> int overlow", uniqueId); 
            }
            catch { }
        }

        public void Dispose()
        {
            // Process object is disposed in Exit event.
            Terminate();
        }

        /// <summary>
        /// Applies the background fill controls, they are not mpv properties and are resolved here.
        /// </summary>
        /// <returns>True when the control was consumed by the player.</returns>
        private bool TryApplyBackgroundProperty(ControlModel control) => TryApplyBackgroundProperty(control.Name, control switch
        {
            ColorPickerModel colorPicker => colorPicker.Value,
            CheckboxModel checkbox => checkbox.Value,
            _ => null,
        });

        private bool TryApplyBackgroundProperty(string name, object value)
        {
            switch (name)
            {
                case BackgroundColorProperty:
                    userBackgroundColor = value as string ?? DefaultBackgroundColor;
                    break;
                case BackgroundAutoProperty:
                    isBackgroundAuto = value is true;
                    break;
                default:
                    return false;
            }

            UpdateBackgroundColor();
            return true;
        }

        /// <summary>
        /// Paints the area not covered by the media, black by default.
        /// </summary>
        private void UpdateBackgroundColor()
        {
            var color = isBackgroundAuto ? GetSampledBackgroundColor() ?? userBackgroundColor : userBackgroundColor;
            SendMessage(GetMpvCommand("set_property", "background-color",
                string.IsNullOrWhiteSpace(color) ? DefaultBackgroundColor : color));
        }

        /// <summary>
        /// Average color of the picture, sampled once, null for media it makes no sense for.
        /// </summary>
        private string GetSampledBackgroundColor()
        {
            if (isBackgroundColorSampled)
                return sampledBackgroundColor;

            isBackgroundColorSampled = true;
            if (Category is not (WallpaperType.picture or WallpaperType.gif))
                return null;

            try
            {
                using var image = new MagickImage(Model.FilePath);
                image.HasAlpha = false;
                // Downscaling to a single pixel is the average color of the image.
                image.Resize(new MagickGeometry(1, 1) { IgnoreAspectRatio = true });
                // Read the pixel through the collection, the byte format respects the source bit depth.
                var rgb = image.GetPixels().ToByteArray(0, 0, 1, 1, "RGB");
                if (rgb.Length >= 3)
                    sampledBackgroundColor = $"#{rgb[0]:X2}{rgb[1]:X2}{rgb[2]:X2}";
            }
            catch (Exception e)
            {
                Logger.Error($"Mpv{uniqueId}: Failed to sample the background color: {e.Message}");
            }

            return sampledBackgroundColor;
        }

        // Ref: https://github.com/rocksdanister/lively/issues/2194
        private void UpdateScaler(WallpaperScaler scaler)
        {
            switch (scaler)
            {
                case WallpaperScaler.none:
                    SendMessage(GetMpvCommand("set_property", "keepaspect", "yes"));
                    SendMessage(GetMpvCommand("set_property", "video-unscaled", "yes"));
                    break;
                case WallpaperScaler.fill:
                    SendMessage(GetMpvCommand("set_property", "video-unscaled", "no"));
                    SendMessage(GetMpvCommand("set_property", "keepaspect", "no"));
                    break;
                case WallpaperScaler.uniform:
                    SendMessage(GetMpvCommand("set_property", "panscan", "0.0"));
                    SendMessage(GetMpvCommand("set_property", "video-unscaled", "no"));
                    SendMessage(GetMpvCommand("set_property", "keepaspect", "yes"));
                    break;
                case WallpaperScaler.uniformFill:
                    SendMessage(GetMpvCommand("set_property", "video-unscaled", "no"));
                    SendMessage(GetMpvCommand("set_property", "keepaspect", "yes"));
                    SendMessage(GetMpvCommand("set_property", "panscan", "1.0"));
                    break;
            }
        }

        #region mpv util

        /*                                      - BenchmarkDotNet -
         *|        Method     |     Mean |     Error |    StdDev |  Gen 0   | Gen 1 | Gen 2 | Allocated |
         *|------------------:|---------:|----------:|----------:|---------:|------:|------:|----------:|
         *| GetMpvCommand     | 1.493 us | 0.0085 us | 0.0080 us | 0.5741   |     - |     - |      2 KB |
         *| GetMpvCommandStrb | 1.551 us | 0.0148 us | 0.0138 us | 1.7033   |     - |     - |      5 KB |
         */

        /// <summary>
        /// Creates serialized mpv ipc json string.
        /// </summary>
        /// <param name="parameters"></param>
        /// <returns></returns>
        private static string GetMpvCommand(params object[] parameters)
        {
            var obj = new MpvCommand();
            obj.Command.AddRange(parameters);
            return JsonConvert.SerializeObject(obj) + Environment.NewLine;
        }

        /// <summary>
        /// Creates serialized mpv ipc json string.
        /// </summary>
        /// <param name="parameters"></param>
        /// <returns></returns>
        private static string GetMpvCommandStrb(params object[] parameters)
        {
            var script = new StringBuilder();
            script.Append("{\"command\":[");
            for (int i = 0; i < parameters.Length; i++)
            {
                script.Append(JsonConvert.SerializeObject(parameters[i]));
                if (i < parameters.Length - 1)
                {
                    script.Append(", ");
                }
            }
            script.Append("]}\n");
            return script.ToString();
        }

        private static string GetConfigDir()
        {
            //Priority list of configuration directories
            string[] dirs = {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins", "mpv", "portable_config"),
                Path.Combine(Constants.CommonPaths.TempVideoDir, "portable_config")
            };
            return dirs.FirstOrDefault(x => Directory.Exists(x));
        }

        // Ref: https://mpv.io/manual/master/#options-target-colorspace-hint-mode
        private static string GetMpvTargetColorSpace(TargetColorspaceHintMode color)
        {
            return color switch
            {
                TargetColorspaceHintMode.target => "target",
                TargetColorspaceHintMode.source => "source",
                TargetColorspaceHintMode.sourceDynamic => "source-dynamic",
                _ => throw new ArgumentOutOfRangeException(nameof(color), $"Unsupported color space target: {color}")
            };
        }

        // Ref: https://mpv.io/manual/master/#exit-codes
        private static Exception GetMpvException(int? exitCode)
        {
            return exitCode switch
            {
                1 => new WallpaperPluginException("Error initializing mpv. This is also returned if unknown options are passed to mpv."),
                2 or 3 => new WallpaperFileException("The file passed to mpv couldn't be played."),
                _ => new InvalidOperationException(Properties.Resources.LivelyExceptionGeneral),
            };
        }

        #endregion //mpv util
    }
}
