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
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
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

        /// <summary>
        /// Image transform controls, mpv has no matching properties for the pixel offsets.
        /// </summary>
        private const string PositionXProperty = "imagePositionX";
        private const string PositionYProperty = "imagePositionY";
        private const string RotationProperty = "imageRotation";
        private const string RotationDirectionProperty = "imageRotationCounterClockwise";
        private const string FlipHorizontalProperty = "imageFlipHorizontal";
        private const string FlipVerticalProperty = "imageFlipVertical";

        /// <summary>
        /// Mpv properties used to translate the pixel offsets into offsets mpv understands.
        /// </summary>
        private const string VideoParamsProperty = "video-params";
        private const string OsdDimensionsProperty = "osd-dimensions";
        private const int MpvQueryRequestId = 1;

        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
        private readonly CancellationTokenSource ctsProcessWait = new();
        private string userBackgroundColor = DefaultBackgroundColor;
        private bool isBackgroundAuto = true;
        private string sampledBackgroundColor;
        private bool isBackgroundColorSampled;
        private double imagePositionX;
        private double imagePositionY;
        private double imageRotation;
        private bool isRotationCounterClockwise;
        private bool isFlipHorizontal;
        private bool isFlipVertical;
        private WallpaperScaler currentScaler = WallpaperScaler.uniform;
        private (int width, int height)? videoSize;
        // Mpv is not sent the same value twice, slider drags would reconfigure the video output.
        private int appliedRotation = -1;
        private string appliedFilters;
        private double appliedPanX = double.NaN;
        private double appliedPanY = double.NaN;
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
            // The window is sized by the core to cover the display, mpv must not resize it around the video.
            cmdArgs.Append("--keepaspect-window=no ");
            cmdArgs.Append("--auto-window-resize=no ");
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
                    if (TryApplyPlayerProperty(control))
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

                // The core places and sizes the window after the player loaded, the offsets need that size.
                _ = ReapplyImageTransformAsync();
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

        /// <summary>
        /// Re-applies the offset once the core moved the wallpaper window onto the desktop,
        /// the pan value depends on the window size.
        /// </summary>
        private async Task ReapplyImageTransformAsync()
        {
            try
            {
                await Task.Delay(1500);
                if (IsExited || (imagePositionX == 0 && imagePositionY == 0))
                    return;

                appliedPanX = appliedPanY = double.NaN;
                UpdateImageTransform();
            }
            catch { /* Player already gone. */ }
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
                            if (TryApplyPlayerProperty(sl.Name, sl.Value))
                                break;

                            // Mpv is strongly typed; sending decimal value for integer commands fails.
                            var isFraction = (sl.Step % 1) != 0;
                            msg = GetMpvCommand("set_property", sl.Name, isFraction ? sl.Value : Convert.ToInt32(sl.Value));
                        }
                        break;
                    case MessageType.lp_chekbox:
                        {
                            var chk = (LivelyCheckbox)obj;
                            if (TryApplyPlayerProperty(chk.Name, chk.Value))
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
                            TryApplyPlayerProperty(picker.Name, picker.Value);
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
        /// Applies the controls mpv has no matching property for, the player resolves them instead.
        /// </summary>
        /// <returns>True when the control was consumed by the player.</returns>
        private bool TryApplyPlayerProperty(ControlModel control) => TryApplyPlayerProperty(control.Name, control switch
        {
            ColorPickerModel colorPicker => colorPicker.Value,
            CheckboxModel checkbox => checkbox.Value,
            SliderModel slider => slider.Value,
            _ => null,
        });

        private bool TryApplyPlayerProperty(string name, object value)
        {
            switch (name)
            {
                case BackgroundColorProperty:
                    userBackgroundColor = value as string ?? DefaultBackgroundColor;
                    UpdateBackgroundColor();
                    break;
                case BackgroundAutoProperty:
                    isBackgroundAuto = value is true;
                    UpdateBackgroundColor();
                    break;
                case PositionXProperty:
                    imagePositionX = ToNumber(value);
                    UpdateImageTransform();
                    break;
                case PositionYProperty:
                    imagePositionY = ToNumber(value);
                    UpdateImageTransform();
                    break;
                case RotationProperty:
                    imageRotation = ToNumber(value);
                    UpdateImageTransform();
                    break;
                case RotationDirectionProperty:
                    isRotationCounterClockwise = value is true;
                    UpdateImageTransform();
                    break;
                case FlipHorizontalProperty:
                    isFlipHorizontal = value is true;
                    UpdateImageTransform();
                    break;
                case FlipVerticalProperty:
                    isFlipVertical = value is true;
                    UpdateImageTransform();
                    break;
                default:
                    return false;
            }

            return true;
        }

        private static double ToNumber(object value) => value switch
        {
            double number => number,
            bool boolean => boolean ? 1 : 0,
            string text when double.TryParse(text, out var number) => number,
            _ => 0,
        };

        /// <summary>
        /// Applies the image transform controls, mpv resolves them through pan, rotate and filters.
        /// </summary>
        private void UpdateImageTransform()
        {
            // Quarter turns are left to mpv, the rest is rotated by the filter.
            var mpvRotation = GetRotation() % 90 == 0 ? GetRotation() : 0;
            if (mpvRotation != appliedRotation)
            {
                appliedRotation = mpvRotation;
                SendMessage(GetMpvCommand("set_property", "video-rotate", mpvRotation));
            }

            var filters = GetVideoFilters();
            if (!string.Equals(filters, appliedFilters, StringComparison.Ordinal))
            {
                appliedFilters = filters;
                SendMessage(GetMpvCommand("set_property", "vf", filters));
            }

            var panX = GetPanValue(imagePositionX, true);
            if (!Equals(panX, appliedPanX))
            {
                appliedPanX = panX;
                SendMessage(GetMpvCommand("set_property", "video-pan-x", panX));
            }

            var panY = GetPanValue(imagePositionY, false);
            if (!Equals(panY, appliedPanY))
            {
                appliedPanY = panY;
                SendMessage(GetMpvCommand("set_property", "video-pan-y", panY));
            }
        }

        /// <summary>
        /// Size of the frame the player displays, mpv swaps the dimensions on a quarter turn.
        /// <br>The filter rotation keeps the source size, the picture is cropped to it.</br>
        /// </summary>
        private static (double width, double height) GetRotatedSize((int width, int height) size, int rotation) =>
            rotation % 180 == 90 ? (size.height, size.width) : (size.width, size.height);

        /// <summary>
        /// Mpv rotates clockwise, the control can rotate the other way too.
        /// </summary>
        private int GetRotation()
        {
            var angle = Math.Abs((int)Math.Round(imageRotation)) % 360;
            return angle == 0 ? 0 : (isRotationCounterClockwise ? 360 - angle : angle);
        }

        private string GetVideoFilters()
        {
            var filters = new List<string>();
            if (isFlipHorizontal)
                filters.Add("hflip");
            if (isFlipVertical)
                filters.Add("vflip");
            // Mpv fills the corners of its own rotation with black, the filter is painted instead.
            // Quarter turns add no corners, mpv keeps the whole picture for those.
            var rotation = GetRotation();
            if (rotation != 0 && rotation % 90 != 0)
                filters.Add(GetRotationFilter(rotation));
            return string.Join(",", filters);
        }

        /// <summary>
        /// Rotates the frame and paints the uncovered corners with the background color.
        /// <br>A positive angle rotates clockwise, the same direction mpv uses.</br>
        /// <br>The frame keeps the source size, mpv would otherwise stretch the rotated picture.</br>
        /// </summary>
        private string GetRotationFilter(int rotation)
        {
            var radians = (rotation * Math.PI / 180.0).ToString("0.######", CultureInfo.InvariantCulture);
            var color = GetBackgroundColor().TrimStart('#');
            // The filter expects RGB without the alpha mpv uses.
            if (color.Length > 6)
                color = color.Substring(color.Length - 6);
            return $"rotate=angle={radians}:ow=iw:oh=ih:fillcolor=0x{color}";
        }

        /// <summary>
        /// Mpv pans relative to the scaled video size, the pixel offset is converted to it.
        /// <br>Mpv ignores this in the stretch fit mode, there is nothing to pan there.</br>
        /// </summary>
        private double GetPanValue(double pixels, bool isHorizontal)
        {
            if (pixels == 0)
                return 0;

            var video = GetVideoSize();
            var window = GetWindowSize();
            if (video is null || window is null)
                return 0;

            // The rotation keeps the whole frame, the displayed size grows with the angle.
            var (videoWidth, videoHeight) = GetRotatedSize(video.Value, GetRotation());
            if (videoWidth <= 0 || videoHeight <= 0)
                return 0;

            var scale = currentScaler switch
            {
                WallpaperScaler.none => 1.0,
                WallpaperScaler.uniform => Math.Min(window.Value.width / (double)videoWidth, window.Value.height / (double)videoHeight),
                _ => Math.Max(window.Value.width / (double)videoWidth, window.Value.height / (double)videoHeight),
            };
            var size = (isHorizontal ? videoWidth : videoHeight) * scale;
            return size > 0 ? pixels / size : 0;
        }

        /// <summary>
        /// Video size as reported by mpv, queried once, null while the media is not loaded yet.
        /// </summary>
        private (int width, int height)? GetVideoSize()
        {
            if (videoSize is null && QueryMpvProperty(VideoParamsProperty) is { } video)
                videoSize = ReadSize(video);

            return videoSize;
        }

        /// <summary>
        /// Size of the video output window, null while the window is not created yet.
        /// <br>The core resizes the window after the player loads, so the value is not cached.</br>
        /// </summary>
        private (int width, int height)? GetWindowSize() =>
            QueryMpvProperty(OsdDimensionsProperty) is { } window ? ReadSize(window) : null;

        private static (int width, int height)? ReadSize(JObject token)
        {
            var width = token.Value<int?>("w");
            var height = token.Value<int?>("h");
            return width is > 0 && height is > 0 ? (width.Value, height.Value) : null;
        }

        /// <summary>
        /// Reads a property from the player, null when the pipe or the property is unavailable.
        /// </summary>
        private JObject QueryMpvProperty(string name)
        {
            try
            {
                var request = new JObject
                {
                    ["command"] = new JArray { "get_property", name },
                    ["request_id"] = MpvQueryRequestId,
                };
                var response = PipeClient.SendMessageWithResponse(ipcServerName, request.ToString(Formatting.None),
                    string.Format("\"request_id\":{0}", MpvQueryRequestId));
                return JObject.Parse(response ?? string.Empty)["data"] as JObject;
            }
            catch (Exception e)
            {
                Logger.Error($"Mpv{uniqueId}: Failed to read the mpv property {name}: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Paints the area not covered by the media, black by default.
        /// </summary>
        private void UpdateBackgroundColor()
        {
            SendMessage(GetMpvCommand("set_property", "background-color", GetBackgroundColor()));
            // The rotation fills the corners with the same color.
            UpdateImageTransform();
        }

        /// <summary>
        /// Color painted where the media does not reach, the sampled one takes over when sampling is on.
        /// </summary>
        private string GetBackgroundColor()
        {
            var color = isBackgroundAuto ? GetSampledBackgroundColor() ?? userBackgroundColor : userBackgroundColor;
            return string.IsNullOrWhiteSpace(color) ? DefaultBackgroundColor : color;
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
            currentScaler = scaler;
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

            // The same pixel offset maps to a different pan value in every fit mode.
            appliedPanX = appliedPanY = double.NaN;
            UpdateImageTransform();
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
