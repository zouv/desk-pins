using System;
using System.IO;
using System.Text;
using DeskPins.Core;

namespace DeskPins.App
{
    public static class DataLocation
    {
        public static string DefaultDirectory { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeskPins"); } }
        public static string PointerFile { get { return Path.Combine(DefaultDirectory, "data-directory.txt"); } }

        public static string Resolve(string explicitDirectory = null, string pointerFile = null)
        {
            if (!string.IsNullOrWhiteSpace(explicitDirectory)) return Path.GetFullPath(explicitDirectory);
            pointerFile = pointerFile ?? PointerFile;
            try
            {
                if (File.Exists(pointerFile))
                {
                    var custom = File.ReadAllText(pointerFile, Encoding.UTF8).Trim();
                    if (custom.Length > 0) return Path.GetFullPath(custom);
                }
            }
            catch (IOException) { } catch (UnauthorizedAccessException) { } catch (ArgumentException) { }
            return DefaultDirectory;
        }

        public static void SetCustom(string directory, string pointerFile = null)
        {
            pointerFile = pointerFile ?? PointerFile;
            var full = Path.GetFullPath(directory);
            if (Same(full, DefaultDirectory))
            {
                if (File.Exists(pointerFile)) File.Delete(pointerFile);
                return;
            }
            var parent = Path.GetDirectoryName(pointerFile);
            if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);
            AtomicFile.Write(pointerFile, new UTF8Encoding(false).GetBytes(full));
        }

        public static bool Same(string left, string right)
        {
            return string.Equals(Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
        }
    }
}
