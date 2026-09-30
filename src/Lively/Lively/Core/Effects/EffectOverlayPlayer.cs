using Lively.Common.Helpers.Storage;
using Lively.Common.JsonConverters;
using Lively.Models;
using Lively.Models.Message;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;

namespace Lively.Core.Effects
{
    /// <summary>
    /// The overlay effect player process, one instance per display. The process is
    /// click through and draws over the desktop window, therefore it does not matter
    /// which wallpaper or wallpaper player is running underneath it.
    /// </summary>
    internal sealed class EffectOverlayPlayer : IDisposable
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();
        private static int globalCount;

        private readonly Process process;
        private readonly int uniqueId;
        private bool isExited;

        public EffectOverlayPlayer(string exePath, string effectId, string propertyPath, DisplayMonitor display)
        {
            EffectId = effectId;
            Screen = display;
            uniqueId = globalCount++;

            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = GetArguments(effectId, propertyPath, display),
                WorkingDirectory = Path.GetDirectoryName(exePath),
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            };

            process = new Process()
            {
                EnableRaisingEvents = true,
                StartInfo = startInfo,
            };
        }

        public string EffectId { get; }

        public DisplayMonitor Screen { get; }

        public IntPtr Handle { get; private set; }

        public int Pid { get; private set; }

        public bool IsExited => isExited;

        public event EventHandler Exited;

        private static string GetArguments(string effectId, string propertyPath, DisplayMonitor display)
        {
            var bounds = display.Bounds;
            var args = string.Format(CultureInfo.InvariantCulture, "--effect \"{0}\" --bounds {1},{2},{3},{4}",
                effectId, bounds.X, bounds.Y, bounds.Width, bounds.Height);

            if (!string.IsNullOrEmpty(display.DeviceId))
                args += string.Format(CultureInfo.InvariantCulture, " --display \"{0}\"", display.DeviceId);

            if (!string.IsNullOrEmpty(propertyPath))
                args += string.Format(CultureInfo.InvariantCulture, " --property \"{0}\"", propertyPath);

            return args;
        }

        public void Show()
        {
            try
            {
                process.Exited += Proc_Exited;
                process.OutputDataReceived += Proc_OutputDataReceived;
                process.Start();
                Pid = process.Id;
                process.BeginOutputReadLine();
                Logger.Info($"Overlay{uniqueId}: Effect '{EffectId}' started on display {Screen.DisplayName} (pid {Pid}).");
            }
            catch (Exception e)
            {
                Logger.Error($"Overlay{uniqueId}: Failed to start effect '{EffectId}': {e.Message}");
                isExited = true;
                Exited?.Invoke(this, EventArgs.Empty);
            }
        }

        public void Send(IpcMessage msg) => Send(JsonUtil.Serialize(msg));

        public void Send(string json)
        {
            if (isExited)
                return;

            try
            {
                process.StandardInput.WriteLine(json);
                process.StandardInput.Flush();
            }
            catch (Exception e)
            {
                Logger.Error($"Overlay{uniqueId}: Failed to send message: {e.Message}");
            }
        }

        /// <summary>Stops the player, the process also exits on its own when this one does.</summary>
        public async Task CloseAsync()
        {
            if (isExited)
                return;

            Send(new LivelyCloseCmd());
            var exited = await Task.Run(() => process.WaitForExit(2000));
            if (!exited)
            {
                Logger.Info($"Overlay{uniqueId}: Effect '{EffectId}' did not exit, terminating.");
                Terminate();
            }

            await Task.Delay(1);
        }

        private void Proc_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(e.Data))
                return;

            try
            {
                // The player announces its window, {"Type":0,"Hwnd":N}.
                var message = JsonConvert.DeserializeObject<IpcMessage>(e.Data,
                    new JsonSerializerSettings() { Converters = { new IpcMessageConverter() } });

                if (message is LivelyMessageHwnd hwnd)
                    Handle = new IntPtr(hwnd.Hwnd);
            }
            catch (Exception ex)
            {
                Logger.Info($"Overlay{uniqueId}: {e.Data} ({ex.Message})");
            }
        }

        private void Proc_Exited(object sender, EventArgs e)
        {
            isExited = true;
            Exited?.Invoke(this, EventArgs.Empty);
        }

        public void Terminate()
        {
            if (isExited)
                return;

            try
            {
                process.Kill();
            }
            catch (Exception e)
            {
                Logger.Error($"Overlay{uniqueId}: Failed to terminate: {e.Message}");
            }
        }

        public void Dispose()
        {
            process.Exited -= Proc_Exited;
            try
            {
                Terminate();
                process.Dispose();
            }
            catch { }
        }
    }
}
