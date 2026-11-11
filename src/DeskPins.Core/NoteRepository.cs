using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DeskPins.Core
{
    public sealed class NoteRepository : IDisposable
    {
        private readonly Sqlite db;
        public string FilePath { get { return db.FilePath; } }
        public NoteRepository(string path)
        {
            db = new Sqlite(path);
            var integrity = db.Query("PRAGMA quick_check;"); if (integrity.Count != 1 || (string)integrity[0][0] != "ok") throw new IOException("数据库完整性检查失败，已停止写入。");
            var tables = db.Query("SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%';");
            if (tables.Count > 0 && !tables.Any(r => (string)r[0] == "notes")) { db.Dispose(); throw new IOException("不是 DeskPins 数据库，未修改原文件。"); }
            if (Convert.ToInt32(db.Query("PRAGMA user_version;")[0][0]) > 1) { db.Dispose(); throw new IOException("数据库由更新版本创建，请升级 DeskPins。"); }
            db.Execute("CREATE TABLE IF NOT EXISTS notes(id TEXT PRIMARY KEY, metadata TEXT NOT NULL, body BLOB, plain TEXT NOT NULL DEFAULT ''); CREATE TABLE IF NOT EXISTS catalog(id INTEGER PRIMARY KEY CHECK(id=1), payload TEXT NOT NULL); CREATE TABLE IF NOT EXISTS transfer_receipts(id TEXT PRIMARY KEY); PRAGMA user_version=1;");
        }
        public List<Note> LoadMetadata() { return db.Query("SELECT metadata FROM notes ORDER BY rowid;").Select(r => Json.Read<Note>((string)r[0])).ToList(); }
        public Catalog LoadCatalog() { var rows = db.Query("SELECT payload FROM catalog WHERE id=1;"); var c = rows.Count == 0 ? new Catalog() : Json.Read<Catalog>((string)rows[0][0]); c.Normalize(); return c; }
        public void LoadContent(Note n)
        {
            if (n.Body != null) return;
            var rows = db.Query("SELECT body,plain FROM notes WHERE id=?;", n.Id);
            if (rows.Count == 0) { n.Body = new byte[0]; n.PlainText = ""; return; }
            n.Body = rows[0][0] as byte[] ?? new byte[0]; n.PlainText = rows[0][1] as string ?? "";
        }
        public void Save(Note n)
        {
            if (n.Body == null) db.Run("UPDATE notes SET metadata=? WHERE id=?;", Json.Text(n), n.Id);
            else db.Run("INSERT INTO notes(id,metadata,body,plain) VALUES(?,?,?,?) ON CONFLICT(id) DO UPDATE SET metadata=excluded.metadata,body=excluded.body,plain=excluded.plain;", n.Id, Json.Text(n), n.Body, n.Encrypted ? "" : n.PlainText ?? "");
        }
        public void SaveCatalog(Catalog c) { db.Run("INSERT OR REPLACE INTO catalog(id,payload) VALUES(1,?);", Json.Text(c)); }
        public void Delete(string id) { db.Run("DELETE FROM notes WHERE id=?;", id); }
        public bool ReceiveOnce(string id) { if (db.Query("SELECT id FROM transfer_receipts WHERE id=?;", id).Count > 0) return false; db.Run("INSERT INTO transfer_receipts(id) VALUES(?);", id); return true; }
        public List<string> Search(string term) { term = term ?? ""; string t = "%" + term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%"; return db.Query("SELECT id,metadata,(plain LIKE ? ESCAPE '\\' COLLATE NOCASE) FROM notes WHERE plain LIKE ? ESCAPE '\\' COLLATE NOCASE OR metadata LIKE ? ESCAPE '\\' COLLATE NOCASE;", t, t, t).Where(r => { var n = Json.Read<Note>((string)r[1]); return (n.Title ?? "").IndexOf(term, StringComparison.CurrentCultureIgnoreCase) >= 0 || n.Id.StartsWith(term, StringComparison.OrdinalIgnoreCase) || !n.Encrypted && Convert.ToInt64(r[2]) != 0; }).Select(r => (string)r[0]).ToList(); }
        public void Transaction(Action action) { db.Transaction(action); }
        public void Backup(string file) { if (Path.GetFullPath(file).Equals(FilePath, StringComparison.OrdinalIgnoreCase)) throw new IOException("不能覆盖正在使用的数据库。"); db.Backup(file); }
        public void Vacuum() { db.Execute("VACUUM;"); }
        public void ImportBackup(string file, bool merge, IEnumerable<string> selectedIds = null)
        {
            using (var source = new Sqlite(file, true))
            {
                var check = source.Query("PRAGMA quick_check;"); if (check.Count != 1 || (string)check[0][0] != "ok") throw new IOException("备份已损坏。");
                var rows = source.Query("SELECT id,metadata,body,plain FROM notes;"); var catalog = source.Query("SELECT payload FROM catalog WHERE id=1;");
                var receipts = source.Query("SELECT name FROM sqlite_master WHERE type='table' AND name='transfer_receipts';").Count > 0 ? source.Query("SELECT id FROM transfer_receipts;") : new List<object[]>();
                var selection = selectedIds == null ? null : new HashSet<string>(selectedIds);
                foreach (var row in rows) Json.Read<Note>((string)row[1]); if (catalog.Count > 0) Json.Read<Catalog>((string)catalog[0][0]).Normalize();
                db.Transaction(() => { if (!merge) { db.Execute("DELETE FROM notes; DELETE FROM catalog; DELETE FROM transfer_receipts;"); foreach (var receipt in receipts) db.Run("INSERT INTO transfer_receipts(id) VALUES(?);", receipt[0]); } foreach (var r in rows) { if (selection != null && !selection.Contains((string)r[0])) continue; db.Run("INSERT OR REPLACE INTO notes(id,metadata,body,plain) VALUES(?,?,?,?);", r); } if (!merge && catalog.Count > 0) db.Run("INSERT INTO catalog(id,payload) VALUES(1,?);", catalog[0][0]); else if (merge && catalog.Count > 0) { var target = LoadCatalog(); var incoming = Json.Read<Catalog>((string)catalog[0][0]); if (string.IsNullOrEmpty(target.Settings.PasswordVerifier)) target.Settings.PasswordVerifier = incoming.Settings.PasswordVerifier; else if (incoming.Settings.PasswordVerifier != target.Settings.PasswordVerifier && rows.Any(r => Json.Read<Note>((string)r[1]).Encrypted && (selection == null || selection.Contains((string)r[0])))) throw new InvalidDataException("加密备份使用不同密码，不能合并；请选择完整恢复或先解密导出。"); foreach (var f in incoming.Folders) if (!target.Folders.Any(x => x.Id == f.Id)) target.Folders.Add(f); foreach (var s in incoming.Stacks) if (!target.Stacks.Any(x => x.Id == s.Id)) target.Stacks.Add(s); foreach (var t in incoming.Tags) if (!target.Tags.Any(x => x.Id == t.Id)) target.Tags.Add(t); SaveCatalog(target); } });
            }
        }
        public void Dispose() { db.Dispose(); }
    }
}
