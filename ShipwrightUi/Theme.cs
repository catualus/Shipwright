using System;
using System.Windows;
using System.Windows.Media;

namespace Shipwright.Ui
{
    /// <summary>
    /// The window's colours, in the theme Compile Pal is currently painting.
    ///
    /// Compile Pal sets COMPILE_PAL_THEME on the process it opens, so a window launched from a dark
    /// application is dark. It is not an approximation of Compile Pal's own palette down to the hex
    /// value - chasing that would break every time the host restyles - but it sits in the same
    /// register: a near-black ground with a blue accent, or a near-white one with the same blue
    /// darkened enough to read on it.
    ///
    /// Everything in the XAML binds these by DynamicResource, so nothing needs to know which theme
    /// it ended up in.
    /// </summary>
    internal static class Theme
    {
        public static bool IsDark { get; private set; } = true;

        public static void Apply(ResourceDictionary resources)
        {
            IsDark = !string.Equals(
                Environment.GetEnvironmentVariable("COMPILE_PAL_THEME"), "light",
                StringComparison.OrdinalIgnoreCase);

            if (IsDark)
            {
                Set(resources, "Ground", "#16191C");
                Set(resources, "Panel", "#1E2327");
                Set(resources, "Row", "#24292E");
                Set(resources, "Line", "#333A41");
                Set(resources, "Ink", "#E6E9EC");
                Set(resources, "InkSoft", "#97A2AC");
                Set(resources, "Accent", "#3B8FE0");
                Set(resources, "AccentInk", "#0C1418");
                Set(resources, "Warn", "#E0A33B");
                Set(resources, "Bad", "#E0655B");
                Set(resources, "Good", "#63B26B");
            }
            else
            {
                Set(resources, "Ground", "#F3F4F6");
                Set(resources, "Panel", "#FFFFFF");
                Set(resources, "Row", "#F0F2F4");
                Set(resources, "Line", "#D6DADE");
                Set(resources, "Ink", "#16191C");
                Set(resources, "InkSoft", "#5B6570");
                Set(resources, "Accent", "#1F6FBF");
                Set(resources, "AccentInk", "#FFFFFF");
                Set(resources, "Warn", "#8A5A00");
                Set(resources, "Bad", "#A32A25");
                Set(resources, "Good", "#2C6B33");
            }
        }

        private static void Set(ResourceDictionary resources, string key, string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            resources[key] = brush;
        }
    }
}
