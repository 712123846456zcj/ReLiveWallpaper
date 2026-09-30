using CommandLine;

namespace Lively.Player.Overlay
{
    /// <summary>
    /// Commandline arguments passed by the Lively core.
    /// </summary>
    public class StartArgs
    {
        [Option("effect",
            Required = false,
            HelpText = "Effect id to render, ex: rain")]
        public string Effect { get; set; } = "rain";

        [Option("property",
            Required = false,
            HelpText = "Path to the effect property json file.")]
        public string PropertyPath { get; set; }

        [Option("bounds",
            Required = false,
            HelpText = "Overlay window bounds in pixels, format: x,y,width,height")]
        public string Bounds { get; set; }

        [Option("display",
            Required = false,
            HelpText = "Display device id this overlay is created for.")]
        public string Display { get; set; }

        [Option("snapshot",
            Required = false,
            HelpText = "Debug: render a single frame to the given path and exit.")]
        public string SnapshotPath { get; set; }

        [Option("snapshot-time",
            Required = false,
            Default = 3.0,
            HelpText = "Debug: seconds of animation simulated before the snapshot.")]
        public double SnapshotTime { get; set; }
    }
}
