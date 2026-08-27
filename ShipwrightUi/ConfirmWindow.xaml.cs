using System.Windows;

namespace Shipwright.Ui
{
    /// <summary>
    /// Asks a yes-or-no question in the window's own colours.
    ///
    /// The stock message box cannot be themed: on a dark window it appears as a light grey dialog
    /// with a system icon, which reads as an error from somewhere else rather than this window
    /// asking something. The questions it asks here are the ones that matter most - replacing a map
    /// on the Workshop, unbinding one - so they should look like they came from the thing that is
    /// about to do it.
    /// </summary>
    public partial class ConfirmWindow : Window
    {
        public bool Confirmed { get; private set; }

        public ConfirmWindow(Window owner, string headline, string body, string confirmLabel)
        {
            InitializeComponent();

            Owner = owner;
            HeadlineText.Text = headline;
            BodyText.Text = body;
            ConfirmButton.Content = confirmLabel;

            SourceInitialized += (_, _) => TitleBarTheme.Apply(this);
        }

        /// <summary>Shows the question and returns whether it was answered yes.</summary>
        public static bool Ask(Window owner, string headline, string body, string confirmLabel)
        {
            var window = new ConfirmWindow(owner, headline, body, confirmLabel);
            window.ShowDialog();

            return window.Confirmed;
        }

        /// <summary>Says something that needs no answer, with one button.</summary>
        public static void Tell(Window owner, string headline, string body)
        {
            var window = new ConfirmWindow(owner, headline, body, "OK");
            window.CancelButton.Visibility = Visibility.Collapsed;
            window.ShowDialog();
        }

        private void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            Confirmed = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();
    }
}
