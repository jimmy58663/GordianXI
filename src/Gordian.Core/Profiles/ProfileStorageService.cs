// src/Gordian.Core/Profiles/ProfileStorageService.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Gordian.Core.Profiles
{
    /// <summary>
    /// Metadata describing a folder container in the profile launch hierarchy.
    /// </summary>
    public sealed class ProfileFolderEntry
    {
        public string Path { get; set; } = string.Empty;
        public bool IsExpanded { get; set; } = true;

        public ProfileFolderEntry() { }

        public ProfileFolderEntry(string path, bool isExpanded = true)
        {
            Path = path;
            IsExpanded = isExpanded;
        }
    }

    /// <summary>
    /// Root document representing the complete persistent profile database,
    /// storing custom folder hierarchy and exact profile ordering.
    /// </summary>
    public sealed class ProfileStoreDocument
    {
        public int Version { get; set; } = 1;
        public List<ProfileFolderEntry> Folders { get; set; } = new();
        public List<AccountProfile> Profiles { get; set; } = new();
    }

    /// <summary>
    /// Service responsible for atomic persistence, crash-safe backups,
    /// and zero-loss legacy migration of GordianXI profiles.
    /// </summary>
    public static class ProfileStorageService
    {
        public const string StoreFileName = "profiles.json";
        public const string BackupFileName = "profiles.json.bak";
        public const string TempFileName = "profiles.json.tmp";

        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            WriteIndented = true
        };

        /// <summary>
        /// Loads the profile store from the given base directory.
        /// If profiles.json is not present, automatically migrates legacy individual .json files.
        /// </summary>
        public static ProfileStoreDocument Load(string baseDirectory)
        {
            if (string.IsNullOrWhiteSpace(baseDirectory))
            {
                return new ProfileStoreDocument();
            }

            string storePath = Path.Combine(baseDirectory, StoreFileName);
            if (File.Exists(storePath))
            {
                try
                {
                    string json = File.ReadAllText(storePath);
                    var doc = JsonSerializer.Deserialize<ProfileStoreDocument>(json, SerializerOptions);
                    if (doc != null)
                    {
                        return doc;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ProfileStorageService] Failed to read {storePath}, attempting backup: {ex.Message}");
                    string backupPath = Path.Combine(baseDirectory, BackupFileName);
                    if (File.Exists(backupPath))
                    {
                        try
                        {
                            string backupJson = File.ReadAllText(backupPath);
                            var backupDoc = JsonSerializer.Deserialize<ProfileStoreDocument>(backupJson, SerializerOptions);
                            if (backupDoc != null)
                            {
                                return backupDoc;
                            }
                        }
                        catch
                        {
                            // Backup read failed as well
                        }
                    }
                }
            }

            // Migration path: Check if legacy individual .json profile files exist
            return MigrateFromLegacyFiles(baseDirectory);
        }

        /// <summary>
        /// Saves the profile store document atomically to profiles.json,
        /// writing to a temporary file and creating a .bak copy for safety.
        /// </summary>
        public static void Save(string baseDirectory, ProfileStoreDocument doc)
        {
            if (string.IsNullOrWhiteSpace(baseDirectory)) return;

            if (!Directory.Exists(baseDirectory))
            {
                Directory.CreateDirectory(baseDirectory);
            }

            string storePath = Path.Combine(baseDirectory, StoreFileName);
            string backupPath = Path.Combine(baseDirectory, BackupFileName);
            string tempPath = Path.Combine(baseDirectory, TempFileName);

            string json = JsonSerializer.Serialize(doc, SerializerOptions);
            File.WriteAllText(tempPath, json);

            if (File.Exists(storePath))
            {
                try
                {
                    File.Copy(storePath, backupPath, overwrite: true);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ProfileStorageService] Warning: Could not create backup: {ex.Message}");
                }
            }

            // Atomic file replacement or move
            try
            {
                if (File.Exists(storePath))
                {
                    File.Delete(storePath);
                }
                File.Move(tempPath, storePath);
            }
            catch
            {
                // Fallback direct copy if move fails
                File.Copy(tempPath, storePath, overwrite: true);
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }

        private static ProfileStoreDocument MigrateFromLegacyFiles(string baseDirectory)
        {
            var doc = new ProfileStoreDocument();
            if (!Directory.Exists(baseDirectory))
            {
                return doc;
            }

            var jsonFiles = Directory.GetFiles(baseDirectory, "*.json", SearchOption.AllDirectories)
                .Where(f => !Path.GetFileName(f).Equals(StoreFileName, StringComparison.OrdinalIgnoreCase) &&
                            !Path.GetFileName(f).Equals(BackupFileName, StringComparison.OrdinalIgnoreCase) &&
                            !Path.GetFileName(f).Equals(TempFileName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (jsonFiles.Count == 0)
            {
                return doc;
            }

            var folderSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in jsonFiles)
            {
                var profile = AccountProfile.LoadFromFile(file, baseDirectory);
                if (profile != null)
                {
                    doc.Profiles.Add(profile);
                    if (!string.IsNullOrWhiteSpace(profile.Folder))
                    {
                        // Add each folder level in the path
                        string[] parts = profile.Folder.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
                        string acc = string.Empty;
                        foreach (var part in parts)
                        {
                            acc = string.IsNullOrEmpty(acc) ? part : $"{acc}/{part}";
                            folderSet.Add(acc);
                        }
                    }
                }
            }

            foreach (var folderPath in folderSet.OrderBy(f => f))
            {
                doc.Folders.Add(new ProfileFolderEntry(folderPath, isExpanded: true));
            }

            // Save migrated document to profiles.json
            Save(baseDirectory, doc);

            return doc;
        }
    }
}
