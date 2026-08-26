namespace Shipwright.Cli
{
    /// <summary>
    /// The shipwright executable.
    ///
    /// A thin shell over the Shipwright library, which holds everything real. The split is what lets
    /// the same code be a command line and a library at once: Compile Pal runs this executable as an
    /// ordinary compile step, and the tests reference the library without dragging an entry point
    /// along with them.
    /// </summary>
    internal static class Entry
    {
        private static int Main(string[] args) => Shipwright.Program.Main(args);
    }
}
