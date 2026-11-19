using System.Diagnostics;
using System.Runtime.CompilerServices;
using Maniacal.Scumm.Game;
using Maniacal.Scumm.Sound;

namespace Maniacal.Scumm.Tests;

/// <summary>
/// Records a test's play as a video, picture and sound, where the folder <c>MANIACAL_RECORDING</c>
/// names, if any: every step's screen with the cursor, held for as long as the step's game time,
/// and the sound the steps made. A test names its recording with <see cref="Begin"/> before it
/// boots the game; the first world stepped after that is recorded, until <see cref="Finish"/>.
/// The frames go to ffmpeg (on the path, or where <c>MANIACAL_FFMPEG</c> says) as they come, into
/// a lossless video beside the WAV; at the end the two become an MP4 scaled up to 4:3 at 1440x1080,
/// the pixels kept sharp, and are deleted.
/// </summary>
internal static class Recording
{
    // a frame for each sixtieth of a second, the DOS clock's jiffy
    private const int Fps = 60;

    private static string? Folder => Environment.GetEnvironmentVariable("MANIACAL_RECORDING") is { Length: > 0 } folder ? folder : null;

    private static readonly AsyncLocal<string?> Pending = new();

    private static readonly ConditionalWeakTable<GameWorld, Recorder> Recorders = [];

    /// <summary>Records the next world stepped, as the name given, when recordings are asked for.</summary>
    public static void Begin(string name)
    {
        if (Folder is not null)
        {
            Pending.Value = name;
        }
    }

    /// <summary>A step of the world, recorded if it is being recorded.</summary>
    public static void Step(GameWorld world)
    {
        world.Step();
        Stepped(world);
    }

    /// <summary>Records the step the world has just made, if it is being recorded.</summary>
    public static void Stepped(GameWorld world)
    {
        if (!Recorders.TryGetValue(world, out var recorder))
        {
            if (Pending.Value is not { } name || Folder is not { } folder)
            {
                return;
            }

            Pending.Value = null;
            recorder = new Recorder(world, Path.Combine(folder, name));
            Recorders.Add(world, recorder);
        }

        recorder.Frame();
    }

    /// <summary>Ends the world's recording, if any, and makes its MP4; ends a recording asked for that never began.</summary>
    public static void Finish(GameWorld? world)
    {
        Pending.Value = null;
        if (world is not null && Recorders.TryGetValue(world, out var recorder))
        {
            Recorders.Remove(world);
            recorder.Finish();
        }
    }

    /// <summary>Whether the world is being recorded.</summary>
    public static bool IsRecording(GameWorld world) => Recorders.TryGetValue(world, out _);

    private sealed class Recorder
    {
        private readonly GameWorld _world;
        private readonly string _path;
        private readonly Process _ffmpeg;
        private readonly Stream _frames;
        private readonly byte[] _rgb;
        private readonly int _width, _height;
        private readonly List<short> _audio = [];
        private long _written;

        public Recorder(GameWorld world, string path)
        {
            _world = world;
            _path = path;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var screen = world.Screen();
            (_width, _height) = (screen.Width, screen.Height);
            _rgb = new byte[_width * _height * 3];
            _ffmpeg = Run(["-f", "rawvideo", "-pix_fmt", "rgb24", "-s", $"{_width}x{_height}", "-r", $"{Fps}", "-i", "-",
                "-c:v", "ffv1", $"{path}.video.mkv"], input: true);
            _frames = _ffmpeg.StandardInput.BaseStream;
        }

        // the screen as the step left it, for as many frames as bring the video up to the game's time
        public void Frame()
        {
            _audio.AddRange(_world.ReadAudio());
            long due = (long)Math.Round(_world.Time.TotalSeconds * Fps);
            if (due <= _written)
            {
                return;
            }

            var screen = _world.Screen();
            _world.DrawCursor(screen);
            var palette = _world.Palette.Span;
            for (int i = 0; i < screen.Pixels.Length; i++)
            {
                var colour = palette[screen.Pixels[i]];
                _rgb[i * 3] = colour.R;
                _rgb[i * 3 + 1] = colour.G;
                _rgb[i * 3 + 2] = colour.B;
            }

            for (; _written < due; _written++)
            {
                _frames.Write(_rgb);
            }
        }

        public void Finish()
        {
            _frames.Close();
            _ffmpeg.WaitForExit();
            Check(_ffmpeg, "the lossless video");
            File.WriteAllBytes($"{_path}.wav", WavWriter.Encode(_audio.ToArray(), _world.SampleRate, 2));

            // each pixel a whole number of pixels first, so they stay square and sharp, then to 4:3
            int whole = 1080 / _height;
            var encode = Run(["-i", $"{_path}.video.mkv", "-i", $"{_path}.wav",
                "-vf", $"scale=iw*{whole}:ih*{whole}:flags=neighbor,scale=1440:1080:flags=lanczos,setsar=1",
                "-c:v", "libx264", "-preset", "slow", "-crf", "16", "-pix_fmt", "yuv420p", "-r", $"{Fps}",
                "-c:a", "aac", "-b:a", "192k", "-movflags", "+faststart", "-shortest", $"{_path}.mp4"], input: false);
            encode.WaitForExit();
            Check(encode, "the MP4");

            // the lossless video runs to gigabytes; the MP4 holds what is needed
            File.Delete($"{_path}.video.mkv");
            File.Delete($"{_path}.wav");
        }

        private static Process Run(IEnumerable<string> arguments, bool input)
        {
            var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("MANIACAL_FFMPEG") is { Length: > 0 } ffmpeg ? ffmpeg : "ffmpeg")
            {
                RedirectStandardInput = input,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (string argument in new[] { "-y", "-hide_banner", "-loglevel", "error" }.Concat(arguments))
            {
                start.ArgumentList.Add(argument);
            }

            var process = Process.Start(start) ?? throw new InvalidOperationException("ffmpeg did not start");
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                {
                    lock (process)
                    {
                        Errors.GetOrCreateValue(process).Add(e.Data);
                    }
                }
            };
            process.BeginErrorReadLine();
            return process;
        }

        private static readonly ConditionalWeakTable<Process, List<string>> Errors = [];

        private static void Check(Process process, string what)
        {
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"ffmpeg could not make {what}: {string.Join(" ", Errors.GetOrCreateValue(process))}");
            }
        }
    }
}
