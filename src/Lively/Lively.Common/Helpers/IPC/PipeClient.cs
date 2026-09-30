using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading.Tasks;

namespace Lively.Common.Helpers.IPC
{
    public class PipeClient
    {
        public static void SendMessage(string channelName, string msg)
        {
            using var pipeClient = new NamedPipeClientStream(".", channelName, PipeDirection.Out);
            pipeClient.Connect(0);
            var writer = new StreamWriter(pipeClient) { AutoFlush = true };
            writer.Write(msg);
            writer.Flush();
            writer.Close();
            pipeClient.Dispose();
        }

        /// <summary>
        /// Sends a message and reads the reply, messages not containing the given token are skipped.
        /// </summary>
        /// <returns>Reply line, null when the peer did not reply in time.</returns>
        public static string SendMessageWithResponse(string channelName, string msg, string replyToken = null, int timeoutMs = 500)
        {
            try
            {
                using var pipeClient = new NamedPipeClientStream(".", channelName, PipeDirection.InOut);
                pipeClient.Connect(timeoutMs);
                // Both wrappers leave the pipe open: disposing them must not flush into a closed stream.
                using var writer = new StreamWriter(pipeClient, new UTF8Encoding(false), 1024, true) { AutoFlush = true };
                using var reader = new StreamReader(pipeClient, Encoding.UTF8, true, 1024, true);
                writer.WriteLine(msg);

                var stopwatch = Stopwatch.StartNew();
                while (stopwatch.ElapsedMilliseconds < timeoutMs)
                {
                    var line = ReadLine(reader, (int)Math.Max(1, timeoutMs - stopwatch.ElapsedMilliseconds));
                    if (line is null)
                        break;
                    if (replyToken is null || line.Contains(replyToken))
                        return line;
                }
            }
            catch { /* Peer not available, the caller deals with a missing reply. */ }

            return null;
        }

        private static string ReadLine(StreamReader reader, int timeoutMs)
        {
            // The stream has no read timeout, the read is dropped instead of blocking the caller.
            var readTask = Task.Run(() =>
            {
                try
                {
                    return reader.ReadLine();
                }
                catch
                {
                    return null;
                }
            });
            return readTask.Wait(timeoutMs) ? readTask.Result : null;
        }
    }
}
