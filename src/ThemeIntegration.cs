using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace BDSoftPS2Store
{
    // Wires the store into the PS5ish fullscreen theme (menu: "PS5ish temasına entegre et").
    // Every change is idempotent, each file is validated as XML before it is written, and the
    // original theme files are backed up once so they can be restored.
    public static class ThemeIntegration
    {
        public const string ThemeFolder = "PS5ish_676e10ec-adfe-48d8-a1bd-4d5771b5a2ca";
        private const string DkgTileName = "DKGThemeModifier_PS5ish_StoreButton";
        private const string StoreTileName = "BDSoftPS2Store_StoreTile";

        public static string GetThemeDir(string configurationPath)
        {
            return Path.Combine(configurationPath, "Themes", "Fullscreen", ThemeFolder);
        }

        public static bool IsThemeInstalled(string configurationPath)
        {
            return File.Exists(Path.Combine(GetThemeDir(configurationPath), "Views", "Main.xaml"));
        }

        public static bool IsApplied(string configurationPath)
        {
            string main = Path.Combine(GetThemeDir(configurationPath), "Views", "Main.xaml");
            return File.Exists(main) && File.ReadAllText(main).Contains("BDSoftPS2Store_StorePreview");
        }

        // Returns a short report of what was changed.
        public static string Apply(string configurationPath, string backupDir)
        {
            string themeDir = GetThemeDir(configurationPath);
            if (!IsThemeInstalled(configurationPath))
            {
                throw new InvalidOperationException("PS5ish teması bulunamadı: " + themeDir);
            }

            var files = Directory.GetFiles(themeDir, "*.xaml", SearchOption.AllDirectories);
            var changes = new List<string>();
            var pending = new Dictionary<string, string>();

            foreach (string file in files)
            {
                string original = File.ReadAllText(file, Encoding.UTF8);
                string text = original;

                // 1) the Store tile becomes the plugin's tile (stays inside Playnite, no browser)
                text = text.Replace(DkgTileName, StoreTileName);

                string name = Path.GetFileName(file);
                if (name.Equals("Constants.xaml", StringComparison.OrdinalIgnoreCase))
                {
                    // 2) show the tile slot instead of the static store icon
                    text = Regex.Replace(text, "(x:Key=\"StoreButtonTrueFalse\">)false(<)", "${1}true${2}");
                }

                if (name.Equals("Main.xaml", StringComparison.OrdinalIgnoreCase))
                {
                    // 3) live store page on top of the theme's static store picture
                    if (!text.Contains("BDSoftPS2Store_StorePreview"))
                    {
                        text = Regex.Replace(text,
                            "(<Image Stretch=\"Fill\" Width=\"2580\" Source=\"\\{ThemeFile 'Images/StoreBackground\\.jpg'\\}\"\\s*>[\\s\\S]*?</Image>)(\\s*</StackPanel>)",
                            m => "<Grid Width=\"2580\">\r\n" + m.Groups[1].Value +
                                 "\r\n<!--BD Soft PS2 Store live store page-->\r\n<ContentControl x:Name=\"BDSoftPS2Store_StorePreview\" HorizontalAlignment=\"Stretch\" VerticalAlignment=\"Stretch\"/>\r\n</Grid>" +
                                 m.Groups[2].Value, RegexOptions.None);
                    }

                    // 4) the top-bar search button searches the store
                    if (!text.Contains("PS2StoreSearchButton"))
                    {
                        text = text.Replace("<ButtonEx x:Name=\"PART_ButtonSearch\" VerticalAlignment=\"Bottom\"",
                                            "<ButtonEx x:Name=\"PS2StoreSearchButton\" VerticalAlignment=\"Bottom\" Command=\"{PluginSettings Plugin=BDSoftPS2Store, Path=OpenSearchCommand}\"");
                        text = text.Replace("ElementName=PART_ButtonSearch", "ElementName=PS2StoreSearchButton");
                    }

                    // 5) hide the theme's "PlayStation Store" label (it overlaps the game row; the page has its own title)
                    text = Regex.Replace(text,
                        "(<Grid Margin=\"-20,0,0,30\">\\s*<Grid.Style>\\s*<Style TargetType=\"Grid\">\\s*<Setter Property=\"Visibility\" Value=\"Collapsed\"/>\\s*<Style.Triggers>\\s*<DataTrigger Binding=\"\\{Binding ElementName=" + StoreTileName + ", Path=IsKeyboardFocusWithin\\}\" Value=\"True\">\\s*<Setter Property=\"Visibility\" Value=\")Visible(\"/>)",
                        "${1}Collapsed${2}");
                }

                if (text != original)
                {
                    Validate(text, name);
                    pending[file] = text;
                }
            }

            if (pending.Count == 0)
            {
                return "Tema zaten entegre.";
            }

            Backup(themeDir, backupDir, pending.Keys);
            foreach (var item in pending)
            {
                File.WriteAllText(item.Key, item.Value, new UTF8Encoding(false));
                changes.Add(item.Key.Substring(themeDir.Length).TrimStart('\\'));
            }
            return "Değiştirilen dosyalar: " + string.Join(", ", changes);
        }

        public static string Restore(string configurationPath, string backupDir)
        {
            string themeDir = GetThemeDir(configurationPath);
            if (!Directory.Exists(backupDir))
            {
                return "Yedek bulunamadı.";
            }
            int restored = 0;
            foreach (string file in Directory.GetFiles(backupDir, "*.xaml", SearchOption.AllDirectories))
            {
                string relative = file.Substring(backupDir.Length).TrimStart('\\');
                string target = Path.Combine(themeDir, relative);
                if (Directory.Exists(Path.GetDirectoryName(target)))
                {
                    File.Copy(file, target, true);
                    restored++;
                }
            }
            return restored + " tema dosyası geri yüklendi.";
        }

        // Originals are kept from the first integration only (later runs never overwrite them).
        private static void Backup(string themeDir, string backupDir, IEnumerable<string> files)
        {
            foreach (string file in files)
            {
                string relative = file.Substring(themeDir.Length).TrimStart('\\');
                string target = Path.Combine(backupDir, relative);
                if (!File.Exists(target))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    File.Copy(file, target);
                }
            }
        }

        private static void Validate(string xaml, string name)
        {
            try
            {
                var doc = new XmlDocument();
                doc.LoadXml(xaml);
            }
            catch (XmlException e)
            {
                throw new InvalidOperationException(name + " düzenlenemedi (XML hatası), hiçbir dosya değiştirilmedi: " + e.Message);
            }
        }
    }
}
