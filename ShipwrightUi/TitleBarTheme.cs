using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Shipwright.Ui
{
    /// <summary>
    /// Makes a window's title bar match the theme, and lets it belong to Compile Pal.
    ///
    /// Two things WPF will not do on its own. A dark window still gets a white caption bar, which is
    /// the one part of it that looks like a mistake; and a window in another process floats as a
    /// second application - separate taskbar button, able to end up behind the thing that opened it -
    /// unless it is told whose child it is.
    /// </summary>
    internal static class TitleBarTheme
    {
        private const int DwmUseImmersiveDarkMode = 20;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

        /// <summary>
        /// Paints the caption bar to match. Call once the window has a handle.
        ///
        /// Best effort: the attribute is honoured from Windows 10 1809 onwards, and where it is not
        /// the window keeps the light title bar it would have had anyway.
        /// </summary>
        public static void Apply(Window window)
        {
            try
            {
                var handle = new WindowInteropHelper(window).Handle;
                int dark = Theme.IsDark ? 1 : 0;

                DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref dark, sizeof(int));
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }

        /// <summary>
        /// Makes this window owned by the one whose handle Compile Pal passed.
        ///
        /// An owned window stays in front of its owner, minimises and restores with it, and gets no
        /// taskbar button of its own - which is the difference between a settings window and a second
        /// application that happens to be open.
        ///
        /// Must be called before the window is shown. Silently does nothing when there is no handle
        /// to attach to, which is the case when this is run from a terminal.
        /// </summary>
        public static void AttachToHost(Window window)
        {
            string? raw = Environment.GetEnvironmentVariable("COMPILE_PAL_HWND");

            if (!long.TryParse(raw, out long handle) || handle == 0)
                return;

            try
            {
                new WindowInteropHelper(window).Owner = new IntPtr(handle);
                window.ShowInTaskbar = false;
            }
            catch (Exception e) when (e is InvalidOperationException or ArgumentException)
            {
                // A handle that is no longer a window. Nothing is lost but the attachment.
            }
        }
    }
}
