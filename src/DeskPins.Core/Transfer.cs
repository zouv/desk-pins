using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace DeskPins.Core
{
    [DataContract]
    public sealed class NotePackage
    {
        [DataMember] public int Version = 1;
        [DataMember] public string PackageId = Guid.NewGuid().ToString("N");
        [DataMember] public Note Note;
        [DataMember] public byte[] Body;
        [DataMember] public string PlainText;
        public static NotePackage From(Note n) { return new NotePackage { Note = n, Body = n.Body, PlainText = n.PlainText }; }
        public Note Import() { if (Version != 1 || Note == null) throw new InvalidDataException("不支持的便签格式。"); var n = Note.Clone(); n.Body = Body ?? new byte[0]; n.PlainText = PlainText ?? ""; n.State = NoteState.Desktop; n.FolderId = n.StackId = ""; n.Attachment = null; n.Encrypted = false; return n; }
    }
    public static class Transfer
    {
        public const int MaxPacket = 16 * 1024 * 1024;
        public static string EncodeSticky(Note n, int senderPort = 52673)
        {
            var s = new StringBuilder("#sticky ");
            s.Append("TEXT=").Append(Escape(n.PlainText ?? "")).Append('\n');
            if (n.Kind == NoteKind.Image) s.Append("IMAGE=").Append(Convert.ToBase64String(n.Body ?? new byte[0])).Append('\n');
            else if (n.Body != null && n.Body.Length > 0) s.Append("RTF=").Append(Escape(Encoding.ASCII.GetString(n.Body))).Append('\n');
            s.Append("TITLE=").Append(Escape(n.Title)).Append('\n').Append("W=").Append(n.Width.ToString(CultureInfo.InvariantCulture)).Append('\n').Append("HEIGHT=").Append(n.Height.ToString(CultureInfo.InvariantCulture)).Append('\n');
            int col; if (int.TryParse(n.Color.TrimStart('#'), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out col)) s.Append("COL=").Append((col >> 16) & 255).Append(',').Append((col >> 8) & 255).Append(',').Append(col & 255).Append('\n');
            if (n.Secret) s.Append("O=O_SS\n");
            s.Append("POS=").Append(n.Left.ToString(CultureInfo.InvariantCulture)).Append(',').Append(n.Top.ToString(CultureInfo.InvariantCulture)).Append('\n');
            s.Append("PORT=").Append(senderPort).Append('\n');
            return s.ToString();
        }
        public static Note DecodeSticky(string text)
        {
            if (!text.StartsWith("#sticky ", StringComparison.Ordinal)) throw new InvalidDataException("不是 Stickies 网络格式。");
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in text.Substring(8).Split('\n')) { int equals = line.IndexOf('='); if (equals > 0) fields[line.Substring(0, equals).Trim()] = line.Substring(equals + 1).TrimEnd('\r'); }
            var n = new Note { Topmost = true, Body = new byte[0], PlainText = Value(fields, "TEXT").Replace("^M", "\n"), Title = Value(fields, "TITLE"), Secret = Value(fields, "O").Contains("O_SS") };
            var image = Value(fields, "IMAGE"); var rtf = Value(fields, "RTF");
            if (image.Length > 0) { n.Kind = NoteKind.Image; n.Body = Convert.FromBase64String(image); }
            else if (rtf.Length > 0) n.Body = Encoding.ASCII.GetBytes(rtf.Replace("^M", "\r\n"));
            n.Width = Number(fields, "W", 260); if (n.Width < 100) n.Width = 260; n.Height = Math.Max(40, Number(fields, "HEIGHT", 190));
            var color = Value(fields, "COL").Split(','); if (color.Length == 3) { byte r, g, b; if (byte.TryParse(color[0], out r) && byte.TryParse(color[1], out g) && byte.TryParse(color[2], out b)) n.Color = "#" + r.ToString("X2") + g.ToString("X2") + b.ToString("X2"); }
            var position = Value(fields, "POS").Split(','); if (position.Length == 2) { double x, y; if (double.TryParse(position[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x) && double.TryParse(position[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y) && !double.IsInfinity(x) && !double.IsInfinity(y) && !double.IsNaN(x) && !double.IsNaN(y)) { n.Left = x; n.Top = y; } }
            int port; if (int.TryParse(Value(fields, "PORT"), out port) && port > 0 && port <= 65535) n.SourcePort = port;
            return n;
        }
        public static byte[] Frame(string text)
        {
            byte[] body = Encoding.UTF8.GetBytes(text); if (body.Length > MaxPacket) throw new InvalidDataException("便签超过网络传输大小限制。");
            using (var s = new MemoryStream()) using (var b = new BinaryWriter(s)) { if (body.Length < 255) b.Write((byte)body.Length); else if (body.Length < 65535) { b.Write((byte)255); b.Write((ushort)body.Length); } else { b.Write((byte)255); b.Write((ushort)65535); b.Write(body.Length); } b.Write(body); return s.ToArray(); }
        }
        public static string ReadFrame(Stream stream)
        {
            int first = stream.ReadByte(); if (first < 0) throw new EndOfStreamException(); int length = first;
            using (var b = new BinaryReader(stream, Encoding.UTF8, true))
            {
                if (first == 255) { length = b.ReadUInt16(); if (length == 65535) length = b.ReadInt32(); }
                if (length < 0 || length > MaxPacket) throw new InvalidDataException("网络数据长度无效。");
                byte[] data = b.ReadBytes(length); if (data.Length != length) throw new EndOfStreamException(); return Encoding.UTF8.GetString(data);
            }
        }
        public static async Task SendAsync(Note n, string destination, int senderPort = 52673)
        {
            string host = destination; int port = 52673; int colon = destination.LastIndexOf(':'); if (colon > 0 && destination.IndexOf(':') == colon) { host = destination.Substring(0, colon); if (!int.TryParse(destination.Substring(colon + 1), out port) || port < 1 || port > 65535) throw new ArgumentException("端口无效。"); }
            var bytes = Frame(EncodeSticky(n, senderPort));
            using (var client = new TcpClient()) { var connect = client.ConnectAsync(host, port); if (await Task.WhenAny(connect, Task.Delay(5000)) != connect) throw new TimeoutException("连接超时。"); await connect; var write = client.GetStream().WriteAsync(bytes, 0, bytes.Length); if (await Task.WhenAny(write, Task.Delay(5000)) != write) throw new TimeoutException("发送超时。"); await write; }
        }
        public static string Escape(string s) { return (s ?? "").Replace("\r\n", "^M").Replace("\r", "^M").Replace("\n", "^M"); }
        private static string Value(Dictionary<string, string> fields, string key) { string value; return fields.TryGetValue(key, out value) ? value : ""; }
        private static double Number(Dictionary<string, string> fields, string key, double fallback) { double n; return double.TryParse(Value(fields, key), NumberStyles.Float, CultureInfo.InvariantCulture, out n) && !double.IsNaN(n) && !double.IsInfinity(n) ? n : fallback; }
    }
}
