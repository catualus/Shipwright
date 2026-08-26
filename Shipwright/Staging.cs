using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Shipwright
{
    /// <summary>One file that will be inside the addon, and where it came from.</summary>
    public sealed record StagedFile(string RelativePath, string Source, long Bytes);

    /// <summary>
    /// Builds the directory that gmad is pointed at.
    ///
    /// THE REASON THIS EXISTS AT ALL
    ///
    /// The obvious implementation of this tool is one line: run gmad against the game's maps folder.
    /// That folder holds every map on the machine - other people's downloads, unfinished work,
    /// private test maps, the .lmp files belonging to maps that are deliberately not being published
    /// - and gmad would pack all of it into one public addon. There is no undo for that; the files
    /// are on Steam's servers and in subscribers' caches before anyone notices.
    ///
    /// So nothing is ever packed in place. A staging directory is built containing exactly the files
    /// that were decided on, copied in by name, and gmad is pointed at that. A file that was not
    /// chosen is not merely excluded by a rule - it is not in the directory being packed.
    ///
    /// WHAT MAY BE IN IT
    ///
    /// gmad's own whitelist allows maps/*.bsp, *.lmp, *.nav, *.ain and maps/thumb/*.png. That
    /// whitelist is a backstop, not the policy: it would happily accept a .lmp that this tool has
    /// decided must not ship, and it has nothing to say about which map's files these are.
    /// </summary>
    public sealed class Staging : IDisposable
    {
        private readonly List<StagedFile> files = new();
        private readonly bool keep;

        public string Root { get; }

        public IReadOnlyList<StagedFile> Files => files;

        public long TotalBytes => files.Sum(f => f.Bytes);

        private Staging(string root, bool keep)
        {
            Root = root;
            this.keep = keep;
        }

        /// <summary>
        /// Creates an empty staging directory under the temp folder.
        ///
        /// A random component in the name, and a fresh directory every run: two Compile Pal
        /// instances compiling two maps at once must not be able to pack each other's files, and a
        /// leftover directory from a previous run must not be able to add a file to this one.
        /// </summary>
        public static Staging Create(string mapName, bool keep = false)
        {
            string safeName = new string(mapName.Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-').ToArray());
            if (safeName.Length == 0)
                safeName = "map";

            string root = Path.Combine(
                Path.GetTempPath(),
                $"shipwright-{safeName}-{Guid.NewGuid():N}");

            Directory.CreateDirectory(root);
            Directory.CreateDirectory(Path.Combine(root, "maps"));

            return new Staging(root, keep);
        }

        /// <summary>
        /// Copies one file in under an explicit relative path.
        ///
        /// Copied, never linked: a link would let the file change between being staged and being
        /// packed, and the point of the staging directory is that what gmad sees is what was
        /// inspected.
        /// </summary>
        public void Add(string source, string relativePath)
        {
            if (!File.Exists(source))
                throw new FileNotFoundException($"Nothing to stage at {source}", source);

            string destination = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            string parent = Path.GetDirectoryName(destination)!;

            /*
             * The relative path is built by this program from a map name, never taken from input,
             * but it ends up as a path join - so it is checked rather than trusted. A staged file
             * landing outside the staging directory would be a file written somewhere on the user's
             * disk under a name gmad never sees.
             */
            string fullRoot = Path.GetFullPath(Root) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(destination).StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Refusing to stage outside the staging directory: {relativePath}");

            Directory.CreateDirectory(parent);
            File.Copy(source, destination, overwrite: true);

            files.Add(new StagedFile(relativePath.Replace('\\', '/'), source, new FileInfo(destination).Length));
        }

        /// <summary>Writes a generated text file, such as addon.json.</summary>
        public void Write(string relativePath, string contents)
        {
            string destination = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.WriteAllText(destination, contents);

            files.Add(new StagedFile(relativePath.Replace('\\', '/'), "(generated)", new FileInfo(destination).Length));
        }

        public void Dispose()
        {
            if (keep)
            {
                Log.Out($"staging directory kept at {Root}");
                return;
            }

            try
            {
                if (Directory.Exists(Root))
                    Directory.Delete(Root, recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // A leftover temp directory is a nuisance. Failing to remove one is not a reason to
                // report a successful publish as a failure.
                Log.Warn($"could not remove the staging directory {Root}: {e.Message}");
            }
        }
    }
}
