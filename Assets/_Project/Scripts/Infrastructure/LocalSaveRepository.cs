using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using ZombieSLG.Application;
using ZombieSLG.Domain;

namespace ZombieSLG.Infrastructure
{
    public sealed class SystemClock : IClock
    {
        public double Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
    }

    /// <summary>
    /// Local save: JSON → AES-CBC → file, written atomically with a one-generation backup.
    /// The key ships in the client, so this deters casual editing only; authority moves to
    /// Cloud Save + Cloud Code in M3.
    /// </summary>
    public sealed class LocalSaveRepository : ISaveRepository
    {
        private const string FileName = "save.dat";
        private static readonly byte[] Key = Derive("ZombieSLG.save.key.v1", 32);

        private readonly string _path;

        public LocalSaveRepository(string directory = null)
        {
            _path = Path.Combine(directory ?? UnityEngine.Application.persistentDataPath, FileName);
        }

        public static string DefaultPath => Path.Combine(UnityEngine.Application.persistentDataPath, FileName);
        public string FilePath => _path;

        public GameSave Load()
        {
            var save = TryRead(_path) ?? TryRead(_path + ".bak");
            if (save != null) return save;
            return ImportLegacyPlayerPrefs();
        }

        public void Save(GameSave save)
        {
            try
            {
                string json = JsonUtility.ToJson(save);
                byte[] data = Encrypt(Encoding.UTF8.GetBytes(json));
                string tmp = _path + ".tmp";
                File.WriteAllBytes(tmp, data);
                if (File.Exists(_path)) File.Copy(_path, _path + ".bak", true);
                File.Copy(tmp, _path, true);
                File.Delete(tmp);
            }
            catch (Exception e)
            {
                Debug.LogError("[ZombieSLG] 저장 실패: " + e.Message);
            }
        }

        public void Delete()
        {
            foreach (var p in new[] { _path, _path + ".bak", _path + ".tmp" })
                if (File.Exists(p)) File.Delete(p);
        }

        private static GameSave TryRead(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                string json = Encoding.UTF8.GetString(Decrypt(File.ReadAllBytes(path)));
                return JsonUtility.FromJson<GameSave>(json);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ZombieSLG] 세이브 읽기 실패 ({Path.GetFileName(path)}): {e.Message}");
                return null;
            }
        }

        /// <summary>M0 kept night/resources in PlayerPrefs. Carry them into the first M1 save once.</summary>
        private static GameSave ImportLegacyPlayerPrefs()
        {
            const string nightKey = "zslg.night";
            if (!PlayerPrefs.HasKey(nightKey)) return null;

            var s = new GameSave { Version = 0, LastTick = 0 };
            s.Night = Mathf.Max(1, PlayerPrefs.GetInt(nightKey, 1));
            s.Food = 400 + PlayerPrefs.GetInt("zslg.food", 0);
            s.Fuel = 250 + PlayerPrefs.GetInt("zslg.fuel", 0);
            s.Materials = 400 + PlayerPrefs.GetInt("zslg.materials", 0);
            s.HeroShards = PlayerPrefs.GetInt("zslg.shards", 0);

            foreach (var k in new[] { nightKey, "zslg.food", "zslg.fuel", "zslg.materials", "zslg.shards" }) PlayerPrefs.DeleteKey(k);
            PlayerPrefs.Save();
            Debug.Log("[ZombieSLG] M0 진행 데이터(PlayerPrefs)를 새 세이브로 옮겼습니다.");
            return s;
        }

        private static byte[] Encrypt(byte[] plain)
        {
            using (var aes = Aes.Create())
            {
                aes.Key = Key;
                aes.GenerateIV();
                using (var enc = aes.CreateEncryptor())
                {
                    byte[] body = enc.TransformFinalBlock(plain, 0, plain.Length);
                    var result = new byte[aes.IV.Length + body.Length];
                    Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
                    Buffer.BlockCopy(body, 0, result, aes.IV.Length, body.Length);
                    return result;
                }
            }
        }

        private static byte[] Decrypt(byte[] data)
        {
            using (var aes = Aes.Create())
            {
                aes.Key = Key;
                var iv = new byte[16];
                Buffer.BlockCopy(data, 0, iv, 0, 16);
                aes.IV = iv;
                using (var dec = aes.CreateDecryptor())
                    return dec.TransformFinalBlock(data, 16, data.Length - 16);
            }
        }

        private static byte[] Derive(string secret, int length)
        {
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(secret));
                var key = new byte[length];
                Buffer.BlockCopy(hash, 0, key, 0, Math.Min(length, hash.Length));
                return key;
            }
        }
    }
}
