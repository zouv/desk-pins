using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace DeskPins.Core
{
    public sealed class Sqlite : IDisposable
    {
        private IntPtr db;
        public readonly string FilePath;
        private readonly object gate = new object();
        public Sqlite(string path, bool readOnly = false)
        {
            FilePath = Path.GetFullPath(path); if (!readOnly) Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            int rc = sqlite3_open_v2(Utf8(FilePath), out db, readOnly ? 1 : 2 | 4 | 65536, IntPtr.Zero);
            if (rc != 0) { string message = Error(); Dispose(); throw new IOException(message); }
            sqlite3_busy_timeout(db, 5000);
            if (!readOnly) Execute("PRAGMA foreign_keys=ON; PRAGMA journal_mode=DELETE; PRAGMA synchronous=FULL; PRAGMA secure_delete=ON;");
        }
        public void Execute(string sql)
        {
            lock (gate) { IntPtr error; int rc = sqlite3_exec(db, Utf8(sql), IntPtr.Zero, IntPtr.Zero, out error); if (error != IntPtr.Zero) sqlite3_free(error); if (rc != 0) throw new IOException(Error()); }
        }
        public void Run(string sql, params object[] values) { lock (gate) using (var s = Prepare(sql, values)) { int rc = sqlite3_step(s.Handle); if (rc != 101) throw new IOException(Error()); } }
        public List<object[]> Query(string sql, params object[] values)
        {
            lock (gate) using (var s = Prepare(sql, values))
            {
                var rows = new List<object[]>(); int rc;
                while ((rc = sqlite3_step(s.Handle)) == 100)
                {
                    int count = sqlite3_column_count(s.Handle); var row = new object[count];
                    for (int i = 0; i < count; i++)
                    {
                        int type = sqlite3_column_type(s.Handle, i);
                        if (type == 5) row[i] = null;
                        else if (type == 1) row[i] = sqlite3_column_int64(s.Handle, i);
                        else if (type == 4) { int size = sqlite3_column_bytes(s.Handle, i); byte[] bytes = new byte[size]; if (size > 0) Marshal.Copy(sqlite3_column_blob(s.Handle, i), bytes, 0, size); row[i] = bytes; }
                        else { IntPtr p = sqlite3_column_text(s.Handle, i); int len = sqlite3_column_bytes(s.Handle, i); byte[] bytes = new byte[len]; if (len > 0) Marshal.Copy(p, bytes, 0, len); row[i] = Encoding.UTF8.GetString(bytes); }
                    }
                    rows.Add(row);
                }
                if (rc != 101) throw new IOException(Error()); return rows;
            }
        }
        public void Transaction(Action action) { lock (gate) { Execute("BEGIN IMMEDIATE;"); try { action(); Execute("COMMIT;"); } catch { Execute("ROLLBACK;"); throw; } } }
        public void Backup(string target)
        {
            lock (gate) using (var dest = new Sqlite(target))
            {
                IntPtr backup = sqlite3_backup_init(dest.db, Utf8("main"), db, Utf8("main")); if (backup == IntPtr.Zero) throw new IOException(dest.Error());
                int rc = sqlite3_backup_step(backup, -1); int finished = sqlite3_backup_finish(backup);
                if (rc != 101 || finished != 0) throw new IOException("数据库备份失败。");
            }
        }
        private Statement Prepare(string sql, object[] values)
        {
            IntPtr handle; if (sqlite3_prepare_v2(db, Utf8(sql), -1, out handle, IntPtr.Zero) != 0) throw new IOException(Error());
            var s = new Statement(handle);
            try
            {
                for (int i = 0; i < values.Length; i++)
                {
                    object v = values[i]; int rc;
                    if (v == null) rc = sqlite3_bind_null(handle, i + 1);
                    else if (v is byte[]) { var b = (byte[])v; rc = sqlite3_bind_blob(handle, i + 1, b, b.Length, new IntPtr(-1)); }
                    else { byte[] b = Utf8(Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture)); rc = sqlite3_bind_text(handle, i + 1, b, b.Length - 1, new IntPtr(-1)); }
                    if (rc != 0) throw new IOException(Error());
                }
                return s;
            }
            catch { s.Dispose(); throw; }
        }
        private string Error() { if (db == IntPtr.Zero) return "无法打开 SQLite 数据库。"; IntPtr p = sqlite3_errmsg(db); int len = 0; while (Marshal.ReadByte(p, len) != 0) len++; byte[] b = new byte[len]; Marshal.Copy(p, b, 0, len); return Encoding.UTF8.GetString(b); }
        private static byte[] Utf8(string text) { return Encoding.UTF8.GetBytes(text + "\0"); }
        public void Dispose() { lock (gate) { if (db != IntPtr.Zero) { sqlite3_close(db); db = IntPtr.Zero; } } }
        private sealed class Statement : IDisposable { public readonly IntPtr Handle; public Statement(IntPtr h) { Handle = h; } public void Dispose() { sqlite3_finalize(Handle); } }
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_open_v2(byte[] file, out IntPtr db, int flags, IntPtr vfs);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_close(IntPtr db);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr sqlite3_errmsg(IntPtr db);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_busy_timeout(IntPtr db, int ms);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_exec(IntPtr db, byte[] sql, IntPtr callback, IntPtr arg, out IntPtr error);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern void sqlite3_free(IntPtr ptr);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_prepare_v2(IntPtr db, byte[] sql, int length, out IntPtr stmt, IntPtr tail);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_step(IntPtr stmt);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_finalize(IntPtr stmt);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_bind_text(IntPtr stmt, int index, byte[] text, int length, IntPtr destructor);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_bind_blob(IntPtr stmt, int index, byte[] text, int length, IntPtr destructor);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_bind_null(IntPtr stmt, int index);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_column_count(IntPtr stmt);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_column_type(IntPtr stmt, int i);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern long sqlite3_column_int64(IntPtr stmt, int i);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr sqlite3_column_text(IntPtr stmt, int i);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr sqlite3_column_blob(IntPtr stmt, int i);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_column_bytes(IntPtr stmt, int i);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr sqlite3_backup_init(IntPtr target, byte[] targetName, IntPtr source, byte[] sourceName);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_backup_step(IntPtr backup, int pages);
        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_backup_finish(IntPtr backup);
    }
}
