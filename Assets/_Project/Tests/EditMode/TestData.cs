using System;
using System.Collections.Generic;
using System.IO;
using ZombieSLG.Domain;

namespace ZombieSLG.Tests
{
    /// <summary>
    /// Loads the real CSV tables from Assets/_Project/Data/Tables, so tests always run against the
    /// numbers the game ships with. Works from the Unity Test Runner (cwd = project root) and from
    /// `dotnet test tools/DomainTests` (searches upward).
    /// </summary>
    internal static class TestData
    {
        private const string TablesRelative = "Assets/_Project/Data/Tables";
        private static GameData _data;

        public static string TablesDirectory
        {
            get
            {
                foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
                {
                    var dir = new DirectoryInfo(start);
                    while (dir != null)
                    {
                        string candidate = Path.Combine(dir.FullName, TablesRelative);
                        if (Directory.Exists(candidate)) return candidate;
                        dir = dir.Parent;
                    }
                }
                throw new DirectoryNotFoundException("Could not find " + TablesRelative);
            }
        }

        public static string ReadTable(string fileName)
        {
            string path = Path.Combine(TablesDirectory, fileName);
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }

        public static GameTables Tables(List<string> errors) => GameTables.Parse(ReadTable, errors);

        /// <summary>Shared, read-only use. Call <see cref="Fresh"/> when a test needs to mutate rules.</summary>
        public static GameData Data => _data ?? (_data = Fresh());

        public static GameData Fresh()
        {
            var errors = new List<string>();
            var data = GameDataBuilder.Build(Tables(errors), errors);
            if (data == null || errors.Count > 0)
                throw new InvalidOperationException("Data tables are invalid:\n" + string.Join("\n", errors));
            return data;
        }
    }
}
