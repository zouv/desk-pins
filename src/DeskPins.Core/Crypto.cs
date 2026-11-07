using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace DeskPins.Core
{
    public static class Crypto
    {
        private static readonly byte[] Header = Encoding.ASCII.GetBytes("DPE1");
        public static byte[] Protect(byte[] data, string password)
        {
            if (string.IsNullOrEmpty(password)) throw new ArgumentException("密码不能为空。");
            byte[] salt = Random(16), iv = Random(16), key = Derive(password, salt);
            using (var aes = Aes.Create()) using (var output = new MemoryStream())
            {
                aes.Key = Slice(key, 0, 32); aes.IV = iv;
                output.Write(Header, 0, Header.Length); output.Write(salt, 0, salt.Length); output.Write(iv, 0, iv.Length);
                using (var encryptor = aes.CreateEncryptor()) { var encrypted = encryptor.TransformFinalBlock(data, 0, data.Length); output.Write(encrypted, 0, encrypted.Length); }
                var body = output.ToArray(); using (var mac = new HMACSHA256(Slice(key, 32, 32))) { var tag = mac.ComputeHash(body); output.Write(tag, 0, tag.Length); }
                Array.Clear(key, 0, key.Length); return output.ToArray();
            }
        }
        public static byte[] Unprotect(byte[] data, string password)
        {
            if (data == null || data.Length < 84 || Encoding.ASCII.GetString(data, 0, 4) != "DPE1") throw new CryptographicException("加密数据格式无效。");
            byte[] key = Derive(password, Slice(data, 4, 16));
            try
            {
                using (var mac = new HMACSHA256(Slice(key, 32, 32))) { var actual = mac.ComputeHash(data, 0, data.Length - 32); int diff = 0; for (int i = 0; i < 32; i++) diff |= actual[i] ^ data[data.Length - 32 + i]; if (diff != 0) throw new CryptographicException("密码错误或加密数据损坏。"); }
                using (var aes = Aes.Create()) { aes.Key = Slice(key, 0, 32); aes.IV = Slice(data, 20, 16); using (var decryptor = aes.CreateDecryptor()) return decryptor.TransformFinalBlock(data, 36, data.Length - 68); }
            }
            finally { Array.Clear(key, 0, key.Length); }
        }
        public static byte[] Random(int length) { byte[] b = new byte[length]; using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(b); return b; }
        private static byte[] Derive(string password, byte[] salt) { using (var k = new Rfc2898DeriveBytes(password, salt, 100000, HashAlgorithmName.SHA256)) return k.GetBytes(64); }
        private static byte[] Slice(byte[] data, int offset, int length) { var result = new byte[length]; Buffer.BlockCopy(data, offset, result, 0, length); return result; }
    }
}
