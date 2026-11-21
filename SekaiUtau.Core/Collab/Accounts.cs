using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;

namespace SekaiUtau.Core.Collab {
    /// <summary>
    /// One local account. Passwords are never stored: only a PBKDF2-SHA256 hash and its salt.
    /// </summary>
    public class Account {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = string.Empty;
        public string Salt { get; set; } = string.Empty;
        public string Hash { get; set; } = string.Empty;
        public string CreatedAt { get; set; } = string.Empty;

        [JsonIgnore] public bool HasPassword => !string.IsNullOrEmpty(Hash);

        public override string ToString() => Name;
    }

    /// <summary>
    /// Local account store used by the duo (two people) feature. Accounts live next to the
    /// application data, so no server or network is involved: signing in tells the app who is
    /// editing, which tracks belong to whom, and whose contribution gets exported.
    /// </summary>
    public static class Accounts {
        class Store {
            public string? CurrentId { get; set; }
            public List<Account> Accounts { get; set; } = new();
        }

        const int Iterations = 100_000;
        static Store? store;

        public static string FilePath => Path.Combine(PathManager.Inst.DataPath, "accounts.json");

        static Store Load() {
            if (store != null) {
                return store;
            }
            try {
                if (File.Exists(FilePath)) {
                    store = JsonSerializer.Deserialize<Store>(File.ReadAllText(FilePath)) ?? new Store();
                } else {
                    store = new Store();
                }
            } catch (Exception e) {
                Log.Error(e, "Failed to read duo accounts");
                store = new Store();
            }
            store.Accounts ??= new List<Account>();
            return store;
        }

        static void Save() {
            try {
                var dir = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(dir)) {
                    Directory.CreateDirectory(dir);
                }
                File.WriteAllText(FilePath, JsonSerializer.Serialize(Load(),
                    new JsonSerializerOptions { WriteIndented = true }));
            } catch (Exception e) {
                Log.Error(e, "Failed to save duo accounts");
            }
        }

        public static IReadOnlyList<Account> All => Load().Accounts;

        public static Account? Current {
            get {
                var s = Load();
                return s.CurrentId == null ? null : s.Accounts.FirstOrDefault(a => a.Id == s.CurrentId);
            }
        }

        public static string CurrentName => Current?.Name ?? string.Empty;

        public static bool IsSignedIn => Current != null;

        /// <summary>Creates an account and signs it in. Returns a resource key on error, null on success.</summary>
        public static string? Create(string name, string password) {
            name = (name ?? string.Empty).Trim();
            if (name.Length < 2) {
                return "duo.error.name";
            }
            var s = Load();
            if (s.Accounts.Any(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase))) {
                return "duo.error.exists";
            }
            var account = new Account { Name = name, CreatedAt = DateTime.Now.ToString("yyyy-MM-dd") };
            if (!string.IsNullOrEmpty(password)) {
                var salt = RandomNumberGenerator.GetBytes(16);
                account.Salt = Convert.ToBase64String(salt);
                account.Hash = Hash(password, salt);
            }
            s.Accounts.Add(account);
            s.CurrentId = account.Id;
            Save();
            Log.Information("Duo account created: {Name}", name);
            return null;
        }

        /// <summary>Signs in. Returns a resource key on error, null on success.</summary>
        public static string? SignIn(string name, string password) {
            var s = Load();
            name = (name ?? string.Empty).Trim();
            var account = s.Accounts.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
            if (account == null) {
                return "duo.error.unknown";
            }
            if (account.HasPassword) {
                if (string.IsNullOrEmpty(account.Salt)) {
                    return "duo.error.password";
                }
                if (Hash(password ?? string.Empty, Convert.FromBase64String(account.Salt)) != account.Hash) {
                    return "duo.error.password";
                }
            }
            s.CurrentId = account.Id;
            Save();
            Log.Information("Duo account signed in: {Name}", account.Name);
            return null;
        }

        public static void SignOut() {
            var s = Load();
            if (s.CurrentId == null) {
                return;
            }
            s.CurrentId = null;
            Save();
        }

        /// <summary>Deletes an account after checking its password. Returns a resource key on error.</summary>
        public static string? Delete(string name, string password) {
            var s = Load();
            name = (name ?? string.Empty).Trim();
            var account = s.Accounts.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
            if (account == null) {
                return "duo.error.unknown";
            }
            if (account.HasPassword &&
                Hash(password ?? string.Empty, Convert.FromBase64String(account.Salt)) != account.Hash) {
                return "duo.error.password";
            }
            s.Accounts.Remove(account);
            if (s.CurrentId == account.Id) {
                s.CurrentId = null;
            }
            Save();
            Log.Information("Duo account deleted: {Name}", name);
            return null;
        }

        static string Hash(string password, byte[] salt) =>
            Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32));
    }
}
