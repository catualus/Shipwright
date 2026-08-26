using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace Shipwright.Ui
{
    /// <summary>
    /// Binds one map to one Workshop item.
    ///
    /// WHAT IT WILL NOT DO
    ///
    /// It does not upload, and it does not create the item. Both of those happen during a compile,
    /// where the log records them and the guards apply. This window writes a text file - which item,
    /// and what a new one should be called - and that is the whole of it. Closing it at any point,
    /// including in the middle of a lookup, loses nothing but the lookup.
    ///
    /// WHY IT SHOWS SO MUCH BEFORE BINDING
    ///
    /// The mistake this exists to prevent is binding a map to the wrong item, which replaces someone
    /// else's map - or your own, older one - for everyone subscribed to it, with no undo. A number
    /// on its own cannot be checked by eye. A title, a picture and a date can, so the item is
    /// fetched and shown before the button that binds it is usable at all.
    /// </summary>
    public partial class BindingWindow : Window
    {
        private readonly UiOptions options;
        private readonly WorkshopState state;
        private readonly List<CheckBox> tagBoxes = new();

        private ItemDetails? lookedUp;
        private ulong lookedUpId;
        private bool mineLoaded;

        /// <summary>A blank line inside a message box.</summary>
        private static readonly string NewLines = Environment.NewLine + Environment.NewLine;

        public BindingWindow(UiOptions options)
        {
            this.options = options;

            InitializeComponent();

            state = WorkshopState.Load(options.StatePath);

            MapNameText.Text = Path.GetFileName(options.MapSource);
            Title = $"Workshop target — {options.MapName}";

            BuildTagBoxes();
            LoadStateIntoFields();
            ShowBinding();

            SourceInitialized += (_, _) => MatchTitleBarToTheme();
        }

        /// <summary>
        /// Asks Windows to paint the title bar dark when the rest of the window is.
        ///
        /// WPF does not do this on its own: a dark window opened from a dark application still gets
        /// a white caption bar, which is the one part of it that looks like a mistake. Compile Pal
        /// has a whole theming library for the same problem; this is the one line of it that matters
        /// for a single window.
        ///
        /// Best effort. The attribute is only honoured from Windows 10 1809 onwards and the call
        /// simply fails elsewhere, which leaves the light title bar it would have had anyway.
        /// </summary>
        private void MatchTitleBarToTheme()
        {
            try
            {
                var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                int dark = Theme.IsDark ? 1 : 0;

                DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref dark, sizeof(int));
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }

        private const int DwmUseImmersiveDarkMode = 20;

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

        private void BuildTagBoxes()
        {
            var chosen = new HashSet<string>(
                state.Tags ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);

            foreach (string tag in AddonManifest.AllowedTags)
            {
                var box = new CheckBox { Content = tag, IsChecked = chosen.Contains(tag) };
                box.Checked += TagBox_Checked;

                tagBoxes.Add(box);
                TagPanel.Children.Add(box);
            }
        }

        /// <summary>
        /// Two tags is Steam's limit, so the third click turns the oldest one off rather than being
        /// silently dropped later by gmad, where nobody would connect the two.
        /// </summary>
        private void TagBox_Checked(object sender, RoutedEventArgs e)
        {
            var ticked = tagBoxes.Where(b => b.IsChecked == true).ToList();

            if (ticked.Count <= AddonManifest.MaxTags)
                return;

            foreach (var box in ticked.Where(b => !ReferenceEquals(b, sender)).Take(ticked.Count - AddonManifest.MaxTags))
                box.IsChecked = false;
        }

        private void LoadStateIntoFields()
        {
            TitleBox.Text = string.IsNullOrWhiteSpace(state.Title) ? options.MapName : state.Title;

            if (state.IconPath is { } icon)
            {
                IconBox.Text = icon;
                ShowIconVerdict(icon);
            }
            else
            {
                IconVerdictText.Text = "No icon chosen. One is required to create a new item.";
            }
        }

        private void ShowBinding()
        {
            UnbindButton.IsEnabled = state.WorkshopId != null;

            if (state.WorkshopId is not { } id)
            {
                BoundTitleText.Text = "Nothing yet";
                BoundDetailText.Style = (Style)FindResource("Soft");
                BoundDetailText.Text = "The next compile will create an item, if the step is allowed to.";
                return;
            }

            BoundTitleText.Text = string.IsNullOrWhiteSpace(state.Title) ? "(untitled)" : state.Title;

            // The ID is a number and reads as one; the sentence shown when nothing is bound is not.
            BoundDetailText.Style = (Style)FindResource("Mono");
            BoundDetailText.Text = state.LastPublished is { } published
                ? $"{id} · last published {published.ToLocalTime():yyyy-MM-dd HH:mm}"
                : $"{id} · never published from this machine";
        }

        /// <summary>
        /// Loads what this account has published, the first time that tab is looked at.
        ///
        /// Not on window open: it starts gmpublish, which talks to Steam, and someone who came here
        /// to paste a link should not wait for that - or watch it fail - on their way to a tab that
        /// needs neither.
        /// </summary>
        private async void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (mineLoaded || !MineTab.IsSelected)
                return;

            mineLoaded = true;
            await LoadMineAsync();
        }

        private async void RefreshMineButton_Click(object sender, RoutedEventArgs e) => await LoadMineAsync();

        private async Task LoadMineAsync()
        {
            RefreshMineButton.IsEnabled = false;
            MineList.ItemsSource = null;
            MineMessage.Text = "Asking Steam...";

            try
            {
                var steam = await Task.Run(SteamState.Check);

                if (!steam.CanPublish)
                {
                    // Worth spelling out here. gmpublish reports every one of these the same way -
                    // "Couldn't initialize Steam! Make sure it is running!" - including the case
                    // where it is running, is signed in, and only its registration is stale.
                    MineMessage.Text = steam.Message + " You can still bind by pasting a link.";
                    return;
                }

                if (GmodTools.Find("gmpublish.exe", options.BinFolder) is not { } gmpublish)
                {
                    MineMessage.Text = "gmpublish.exe was not found in Garry's Mod's bin folder.";
                    return;
                }

                var (result, items) = await Task.Run(() => GmodTools.List(gmpublish));

                if (!result.Ok)
                {
                    MineMessage.Text = $"gmpublish could not list your items (exit code {result.ExitCode}).";
                    return;
                }

                MineList.ItemsSource = items;

                MineMessage.Text = items.Count switch
                {
                    0 => "This account has published nothing yet.",
                    1 => "1 published item.",
                    _ => $"{items.Count} published items.",
                };
            }
            finally
            {
                RefreshMineButton.IsEnabled = true;
            }
        }

        private void BindMineButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is GmodTools.PublishedItem item)
                BindTo(item.Id, item.Title);
        }

        private async void LookUpButton_Click(object sender, RoutedEventArgs e)
        {
            LookUpMessage.Visibility = Visibility.Collapsed;
            ResultPanel.Visibility = Visibility.Collapsed;
            lookedUp = null;

            if (!WorkshopLink.TryParse(LinkBox.Text, out ulong id))
            {
                Fail("That is not a Workshop address or ID. It looks like " +
                     "https://steamcommunity.com/sharedfiles/filedetails/?id=1234567890");
                return;
            }

            LookUpButton.IsEnabled = false;
            StatusText.Text = "Asking Steam…";

            try
            {
                var details = await Task.Run(() => WorkshopLookup.Describe(id, TimeSpan.FromSeconds(10)));

                if (!details.Found)
                {
                    Fail($"Item {id} {details.Message}");
                    return;
                }

                lookedUp = details;
                lookedUpId = id;

                ResultTitleText.Text = details.Title.Length > 0 ? details.Title : "(untitled)";
                ResultDetailText.Text = Describe(details);
                ResultCreatorText.Text = details.Creator.Length > 0 ? $"creator {details.Creator}" : "";

                bool rightGame = details.ConsumerAppId == WorkshopLookup.GarrysModAppId;

                ResultWarningText.Visibility = rightGame ? Visibility.Collapsed : Visibility.Visible;
                ResultWarningText.Text = rightGame
                    ? ""
                    : $"This item belongs to app {details.ConsumerAppId}, not Garry's Mod. It cannot be published to from here.";

                BindButton.IsEnabled = rightGame;
                ResultPanel.Visibility = Visibility.Visible;

                await ShowPreviewAsync(details.PreviewUrl);
            }
            finally
            {
                LookUpButton.IsEnabled = true;
                StatusText.Text = "";
            }
        }

        private static string Describe(ItemDetails details)
        {
            var parts = new List<string> { $"Garry's Mod item {details.ConsumerAppId}" };

            if (details.Updated is { } updated)
                parts.Add($"updated {updated.ToLocalTime():yyyy-MM-dd}");

            if (details.SizeBytes > 0)
                parts.Add($"{details.SizeBytes / 1024f / 1024f:F1} MB");

            return string.Join(" · ", parts);
        }

        /// <summary>
        /// Fetches the item's preview picture.
        ///
        /// The URL was checked when the response was read - https, on one of Steam's own image hosts
        /// - so this is a request to Steam and not to wherever a response felt like pointing. Loaded
        /// from bytes rather than handed to WPF as a URI so a slow or dead image cannot leave the
        /// window waiting on it, and a failure here is nothing: the item is already identified by
        /// name.
        /// </summary>
        private async Task ShowPreviewAsync(string url)
        {
            PreviewImage.Source = null;

            if (url.Length == 0)
                return;

            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
                byte[] bytes = await client.GetByteArrayAsync(url);

                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = new MemoryStream(bytes);
                image.EndInit();
                image.Freeze();

                PreviewImage.Source = image;
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or NotSupportedException or ArgumentException)
            {
                // No picture. Everything that identifies the item is text anyway.
            }
        }

        private void Fail(string message)
        {
            LookUpMessage.Text = message;
            LookUpMessage.Visibility = Visibility.Visible;
            StatusText.Text = "";
        }

        private void BindButton_Click(object sender, RoutedEventArgs e)
        {
            if (lookedUp is { } details)
                BindTo(lookedUpId, details.Title);
        }

        /// <summary>
        /// Points this map at an item, once someone has confirmed which item that is.
        ///
        /// The confirmation names the item rather than asking "are you sure", because the mistake it
        /// guards against is not carelessness - it is picking the row above the one you meant, which
        /// looks exactly like success until a compile replaces the wrong map.
        /// </summary>
        private void BindTo(ulong id, string title)
        {
            string name = title.Length > 0 ? title : id.ToString();

            var confirm = MessageBox.Show(
                $"Publishing {options.MapName} will replace:{NewLines}{name}{NewLines}" +
                "on the Workshop, for everyone subscribed to it. Bind it?",
                "Workshop target", MessageBoxButton.OKCancel, MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.OK)
                return;

            state.WorkshopId = id.ToString();
            state.Title = title;

            /*
             * The hash of what was published last time belongs to the item that was published to.
             * Carrying it across to a different item would make the first publish look like a repeat
             * of one that never happened, and skip it.
             */
            state.GmaSha256 = null;
            state.LastPublished = null;

            if (Save())
            {
                ShowBinding();
                StatusText.Text = $"Bound to {name}.";
            }
        }

        private void ChooseIconButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Choose the item's icon",
                Filter = "JPEG images (*.jpg;*.jpeg)|*.jpg;*.jpeg|All files (*.*)|*.*",
            };

            if (dialog.ShowDialog(this) != true)
                return;

            IconBox.Text = dialog.FileName;
            ShowIconVerdict(dialog.FileName);
        }

        /// <summary>
        /// Says whether gmpublish will take this icon, here, where fixing it costs nothing.
        ///
        /// The alternative is finding out during a publish: gmpublish rejects a bad icon partway
        /// through creating the item, which leaves an item that may or may not exist and an ID
        /// nothing recorded.
        /// </summary>
        private void ShowIconVerdict(string path)
        {
            var verdict = IconCheck.Inspect(path);

            IconVerdictText.Text = verdict.Acceptable
                ? $"Looks right: {verdict.Message}"
                : $"This will be refused: it {verdict.Message}";

            IconVerdictText.Foreground = (System.Windows.Media.Brush)FindResource(
                verdict.Acceptable ? "Good" : "Bad");
        }

        private void SaveNewButton_Click(object sender, RoutedEventArgs e)
        {
            string title = Sanitize.Title(TitleBox.Text);

            if (title.Length == 0)
            {
                StatusText.Text = "A new item needs a title.";
                return;
            }

            state.Title = title;
            state.Tags = tagBoxes.Where(b => b.IsChecked == true).Select(b => (string)b.Content).ToArray();
            state.IconPath = IconBox.Text.Length > 0 ? IconBox.Text : null;

            if (!Save())
                return;

            TitleBox.Text = title;
            ShowBinding();

            StatusText.Text = state.WorkshopId is null
                ? "Saved. The next compile can create the item."
                : "Saved. This map is still bound to its existing item.";
        }

        private void UnbindButton_Click(object sender, RoutedEventArgs e)
        {
            var confirm = MessageBox.Show(
                $"{options.MapName} will no longer publish to item {state.WorkshopId}.\n\n" +
                "The item itself is untouched. Remove the binding?",
                "Workshop target", MessageBoxButton.OKCancel, MessageBoxImage.Question);

            if (confirm != MessageBoxResult.OK)
                return;

            state.WorkshopId = null;
            state.GmaSha256 = null;
            state.LastPublished = null;

            if (!Save())
                return;

            ShowBinding();
            StatusText.Text = "Binding removed.";
        }

        private bool Save()
        {
            try
            {
                state.Save(options.StatePath);
                return true;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                MessageBox.Show(
                    $"{options.StatePath} could not be written:\n\n{e.Message}\n\n" +
                    "Nothing was changed. A read-only map folder is the usual cause.",
                    "Workshop target", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    }
}
