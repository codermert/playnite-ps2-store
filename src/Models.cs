using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Input;
using System.Windows.Media;

namespace BDSoftPS2Store
{
    // One row of catalog.json (built by tools\BuildCatalog.ps1).
    public class CatalogEntry
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Serial { get; set; }
        public string Region { get; set; }
        public int Year { get; set; }
        public List<string> Genres { get; set; }
        public List<string> Categories { get; set; }
        public List<string> Developers { get; set; }
        public List<string> Publishers { get; set; }
        public string Wiki { get; set; }

        // Web page opened by the "Kur" button (filled from games.json; can also be set in catalog.json).
        public string ExternalUrl { get; set; }
    }

    public abstract class ObservableObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string name)
        {
            var handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(name));
            }
        }
    }

    public class RelayCommand : ICommand
    {
        private readonly Action<object> execute;
        private readonly Func<object, bool> canExecute;

        public RelayCommand(Action<object> execute) : this(execute, null)
        {
        }

        public RelayCommand(Action<object> execute, Func<object, bool> canExecute)
        {
            this.execute = execute;
            this.canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object parameter)
        {
            return canExecute == null || canExecute(parameter);
        }

        public void Execute(object parameter)
        {
            execute(parameter);
        }
    }

    public class StoreTab : ObservableObject
    {
        public string Key { get; set; }
        public string Label { get; set; }
    }

    // A catalog game as shown in the store.
    public class StoreGame : ObservableObject
    {
        private ImageSource cover;
        private bool coverRequested;
        private bool isFavorite;
        private Guid libraryGameId;
        private bool isPlayable;
        private string description;

        public StoreGame(CatalogEntry entry)
        {
            Entry = entry;
            NormalizedTitle = TitleNormalizer.Normalize(entry.Title);
            SearchText = (entry.Title + " " + (entry.Serial ?? "")).ToLowerInvariant();
            SortKey = Regex.Replace(entry.Title, "^[^A-Za-z0-9]+", string.Empty).ToLowerInvariant();
            Acronyms = BuildAcronyms(entry.Title);
            IsRich = entry.Year > 0 && !string.IsNullOrEmpty(entry.Serial);
        }

        public string SortKey { get; private set; }

        // Initials of the title and of each part of it, e.g. "Grand Theft Auto: San Andreas" -> "gtasa", "gta", "sa".
        public List<string> Acronyms { get; private set; }

        private static readonly Regex WordSplit = new Regex("[^a-z0-9]+", RegexOptions.Compiled);
        private static readonly Regex PartSplit = new Regex(@"\s*(?::|\s-\s|–|—)\s*", RegexOptions.Compiled);

        private static string WordInitials(string text)
        {
            var sb = new StringBuilder();
            foreach (string word in WordSplit.Split(text.ToLowerInvariant()))
            {
                if (word.Length == 0 || word == "of" || word == "the" || word == "and" || word == "a")
                {
                    continue;
                }
                // numbers and roman numerals are kept whole: "Metal Gear Solid 3" -> "mgs3", "Final Fantasy X" -> "ffx"
                if (char.IsDigit(word[0]) || Regex.IsMatch(word, "^(ii|iii|iv|vi|vii|viii|ix|x|xi|xii)$"))
                {
                    sb.Append(word);
                }
                else
                {
                    sb.Append(word[0]);
                }
            }
            return sb.ToString();
        }

        private static List<string> BuildAcronyms(string title)
        {
            var list = new List<string>();
            string whole = WordInitials(title);
            if (whole.Length > 1)
            {
                list.Add(whole);
            }
            foreach (string part in PartSplit.Split(title))
            {
                string a = WordInitials(part);
                if (a.Length > 1 && !list.Contains(a))
                {
                    list.Add(a);
                }
            }
            // "God of War" keeps "of": also offer "gow"
            string withSmallWords = string.Concat(WordSplit.Split(title.ToLowerInvariant()).Where(w => w.Length > 0).Select(w => char.IsDigit(w[0]) ? w : w[0].ToString()));
            if (withSmallWords.Length > 1 && !list.Contains(withSmallWords))
            {
                list.Add(withSmallWords);
            }
            return list;
        }

        // A search term matches the title text, the serial, or the start of an acronym.
        public bool Matches(string term)
        {
            // short terms only match the start of a word ("kh" should not match "beckham")
            if (term.Length >= 4 ? SearchText.Contains(term) : (SearchText.StartsWith(term) || Regex.IsMatch(SearchText, "[^a-z0-9]" + Regex.Escape(term))))
            {
                return true;
            }
            foreach (string acronym in Acronyms)
            {
                if (acronym.StartsWith(term))
                {
                    return true;
                }
            }
            return false;
        }

        public bool AcronymStartsWith(string term)
        {
            return Acronyms.Any(a => a.StartsWith(term));
        }
        public bool IsRich { get; private set; }

        public CatalogEntry Entry { get; private set; }
        public string NormalizedTitle { get; private set; }
        public string SearchText { get; private set; }
        public Action<StoreGame> CoverLoader { get; set; }

        public string Title { get { return Entry.Title; } }
        public string Platform { get { return "PlayStation 2"; } }
        public string YearText { get { return Entry.Year > 0 ? Entry.Year.ToString() : "—"; } }
        public string Serial { get { return string.IsNullOrEmpty(Entry.Serial) ? "—" : Entry.Serial + (string.IsNullOrEmpty(Entry.Region) ? "" : " (" + Entry.Region + ")"); } }
        public string GenresText { get { return Join(Entry.Genres); } }
        public string DevelopersText { get { return Join(Entry.Developers); } }
        public string PublishersText { get { return Join(Entry.Publishers); } }

        public string CardSubtitle
        {
            get { return Entry.Year > 0 ? "PS2 · " + Entry.Year : "PS2"; }
        }

        public string HeroMeta
        {
            get
            {
                var parts = new List<string> { "PlayStation 2" };
                if (Entry.Year > 0)
                {
                    parts.Add(Entry.Year.ToString());
                }
                if (Entry.Categories != null && Entry.Categories.Count > 0)
                {
                    parts.Add(string.Join(", ", Entry.Categories.Take(3)));
                }
                return string.Join("  ·  ", parts);
            }
        }

        public string Initials
        {
            get
            {
                var words = Regex.Split(Entry.Title, "[^A-Za-z0-9]+").Where(w => w.Length > 0).Take(3);
                return string.Concat(words.Select(w => char.ToUpperInvariant(w[0]).ToString()));
            }
        }

        public bool HasCover { get { return cover != null; } }

        // External URL for the "Kur" button, from games.json (empty when not defined).
        public string ExternalUrl { get; set; }

        private bool coverMissing;

        // Set when no cover could be found anywhere (shows the designed placeholder instead of the loader).
        public bool CoverMissing
        {
            get { return coverMissing; }
            set
            {
                coverMissing = value;
                OnPropertyChanged("CoverMissing");
            }
        }

        public ImageSource Cover
        {
            get
            {
                if (!coverRequested && CoverLoader != null)
                {
                    coverRequested = true;
                    CoverLoader(this);
                }
                return cover;
            }
            set
            {
                cover = value;
                OnPropertyChanged("Cover");
                OnPropertyChanged("HasCover");
            }
        }

        public bool IsFavorite
        {
            get { return isFavorite; }
            set
            {
                isFavorite = value;
                OnPropertyChanged("IsFavorite");
                OnPropertyChanged("FavoriteButtonText");
            }
        }

        public string FavoriteButtonText
        {
            get { return isFavorite ? "★  Favorilerden çıkar" : "☆  Favorilere ekle"; }
        }

        public Guid LibraryGameId
        {
            get { return libraryGameId; }
            set
            {
                libraryGameId = value;
                OnPropertyChanged("LibraryGameId");
                OnPropertyChanged("InLibrary");
                OnPropertyChanged("LibraryButtonText");
                OnAvailabilityChanged();
            }
        }

        public bool InLibrary { get { return libraryGameId != Guid.Empty; } }

        public string LibraryButtonText
        {
            get { return InLibrary ? "✓  Kütüphanede" : "Playnite Kütüphanesine Ekle"; }
        }

        public bool IsPlayable
        {
            get { return isPlayable; }
            set
            {
                isPlayable = value;
                OnPropertyChanged("IsPlayable");
                OnAvailabilityChanged();
            }
        }

        // Is the game on this PC? (a game file is linked in Playnite) / only in the library / not on this PC
        public bool IsOnPc { get { return isPlayable; } }
        public bool IsLibraryOnly { get { return InLibrary && !isPlayable; } }
        public bool IsNotOnPc { get { return !InLibrary; } }

        public string AvailabilityText
        {
            get
            {
                if (isPlayable)
                {
                    return "PC'de var";
                }
                return InLibrary ? "Kütüphanede · dosya yok" : "PC'de yok";
            }
        }

        private void OnAvailabilityChanged()
        {
            OnPropertyChanged("IsOnPc");
            OnPropertyChanged("IsLibraryOnly");
            OnPropertyChanged("IsNotOnPc");
            OnPropertyChanged("AvailabilityText");
            OnPropertyChanged("CardSubtitle");
            OnPropertyChanged("HeroMeta");
        }

        public string Description
        {
            get { return description; }
            set
            {
                description = value;
                OnPropertyChanged("Description");
            }
        }

        public string WikipediaUrl
        {
            get
            {
                return string.IsNullOrEmpty(Entry.Wiki) ? null : "https://en.wikipedia.org/wiki/" + Uri.EscapeDataString(Entry.Wiki.Replace(' ', '_'));
            }
        }

        public bool HasCategory(string category)
        {
            return Entry.Categories != null && Entry.Categories.Contains(category);
        }

        private static string Join(List<string> items)
        {
            return items == null || items.Count == 0 ? "—" : string.Join(", ", items);
        }
    }

    public static class TitleNormalizer
    {
        private static readonly Regex Brackets = new Regex(@"\[[^\]]*\]|\([^\)]*\)", RegexOptions.Compiled);
        private static readonly Regex NonAlnum = new Regex("[^a-z0-9]+", RegexOptions.Compiled);

        public static string Normalize(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return string.Empty;
            }
            string s = name.ToLowerInvariant();
            s = Brackets.Replace(s, " ");
            s = s.Replace("&", " and ");
            s = NonAlnum.Replace(s, " ").Trim();
            if (s.StartsWith("the "))
            {
                s = s.Substring(4);
            }
            return s.Replace(" ", string.Empty);
        }
    }
}
