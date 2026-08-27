using System;
using System.Windows;

namespace Shipwright.Ui
{
    /// <summary>
    /// The settings window's entry point.
    ///
    /// Compile Pal runs this with the map it is being asked about and waits for it to close. It
    /// publishes nothing and uploads nothing: everything it does ends in one text file beside the
    /// map, which the compile step reads later. That separation is deliberate - the window can be
    /// closed, cancelled or crashed at any point and the worst outcome is a binding that was not
    /// saved.
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            Theme.Apply(Resources);

            UiOptions options;

            try
            {
                options = UiOptions.Parse(e.Args);
            }
            catch (ArgumentException ex)
            {
                MessageBox.Show(ex.Message + "\n\n" + UiOptions.Usage,
                    "Shipwright", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown(1);
                return;
            }

            var window = new BindingWindow(options);

            // Before Show: an owner cannot be given to a window that is already on screen.
            TitleBarTheme.AttachToHost(window);

            MainWindow = window;
            window.Show();
        }
    }
}
