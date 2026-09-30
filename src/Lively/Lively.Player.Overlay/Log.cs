using System;
using System.IO;

namespace Lively.Player.Overlay
{
    /// <summary>Minimal append only log, used since the core only parses stdout json.</summary>
    internal static class Log
    {
        private static readonly object sync = new object();

        public static string LogPath { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Lively Wallpaper", "logs", "overlay.log");

        public static void Info(string message) => Write("INFO ", message);

        public static void Error(string message) => Write("ERROR", message);

        public static void Error(Exception exception) => Write("ERROR", exception?.ToString());

        private static void Write(string level, string message)
        {
            try
            {
                lock (sync)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
                    File.AppendAllText(LogPath,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " [" + level + "] " + message + Environment.NewLine);
                }
            }
            catch
            {
            }
        }
    }
}
