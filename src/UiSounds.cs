using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Media;

namespace BDSoftPS2Store
{
    // Plays the fullscreen theme's interface sounds (audio\navigation.wav, audio\activation.wav)
    // at Playnite's "Interface volume", like Playnite does when moving between games.
    public class UiSounds
    {
        private readonly MediaPlayer[] navigation;
        private readonly MediaPlayer activation;
        private int next;

        public UiSounds(string audioDir, string fullscreenConfigPath)
        {
            double volume = ReadInterfaceVolume(fullscreenConfigPath);
            string nav = Path.Combine(audioDir, "navigation.wav");
            string act = Path.Combine(audioDir, "activation.wav");
            if (File.Exists(nav))
            {
                // a few players so fast scrolling does not cut every sound off
                navigation = new MediaPlayer[3];
                for (int i = 0; i < navigation.Length; i++)
                {
                    navigation[i] = Create(nav, volume);
                }
            }
            if (File.Exists(act))
            {
                activation = Create(act, volume);
            }
        }

        private static MediaPlayer Create(string path, double volume)
        {
            var player = new MediaPlayer { Volume = volume };
            player.Open(new Uri(path, UriKind.Absolute));
            return player;
        }

        private static double ReadInterfaceVolume(string configPath)
        {
            try
            {
                if (File.Exists(configPath))
                {
                    var match = Regex.Match(File.ReadAllText(configPath), "\"InterfaceVolume\"\\s*:\\s*([0-9.]+)");
                    double value;
                    if (match.Success && double.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value))
                    {
                        return Math.Max(0, Math.Min(1, value));
                    }
                }
            }
            catch (Exception)
            {
            }
            return 0.5;
        }

        public void Navigate()
        {
            if (navigation == null)
            {
                return;
            }
            var player = navigation[next];
            next = (next + 1) % navigation.Length;
            Play(player);
        }

        public void Activate()
        {
            Play(activation);
        }

        private static void Play(MediaPlayer player)
        {
            if (player == null || player.Volume <= 0)
            {
                return;
            }
            player.Stop();
            player.Position = TimeSpan.Zero;
            player.Play();
        }
    }
}
