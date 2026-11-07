using System;
using System.IO;

namespace DeskPins.Core
{
    public static class AtomicFile
    {
        public static void Write(string path, byte[] bytes)
        {
            path = Path.GetFullPath(path);
            string temporary = Path.Combine(Path.GetDirectoryName(path), ".deskpins-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
