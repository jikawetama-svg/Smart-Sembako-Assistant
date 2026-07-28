using System;
using System.IO;
using SmartSembakoAssistant.Services;
using Xunit;

namespace SmartSembakoAssistant.Tests
{
    public class ConfigServiceTests : IDisposable
    {
        private readonly string _tempDirectory;

        public ConfigServiceTests()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "SSA_ConfigTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDirectory);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_tempDirectory))
                {
                    Directory.Delete(_tempDirectory, recursive: true);
                }
            }
            catch
            {
                // Best effort cleanup
            }
        }

        [Fact]
        public void LoadConfig_ValidJson_LoadsSuccessfully()
        {
            // Arrange
            string configPath = Path.Combine(_tempDirectory, "config.json");
            string validJson = @"{
  ""Groq"": {
    ""ApiKey"": ""test-key-123"",
    ""Model"": ""llama-3.3-70b-versatile""
  },
  ""App"": {
    ""Theme"": ""Dark""
  }
}";
            File.WriteAllText(configPath, validJson);

            // Act
            var service = new ConfigService(configPath);

            // Assert
            Assert.False(service.IsConfigCorrupted);
            Assert.NotNull(service.Config);
            Assert.Equal("test-key-123", service.Config.Groq.ApiKey);
            Assert.Equal("Dark", service.Config.App.Theme);
        }

        [Fact]
        public void LoadConfig_InvalidJson_CreatesInvalidBackupAndDoesNotOverwriteOriginal()
        {
            // Arrange
            string configPath = Path.Combine(_tempDirectory, "config.json");
            string malformedJson = @"{ ""Groq"": { ""ApiKey"": ""broken-json"", "; // Syntax error
            File.WriteAllText(configPath, malformedJson);

            // Act
            var service = new ConfigService(configPath);

            // Assert
            Assert.True(service.IsConfigCorrupted);
            Assert.NotNull(service.LastLoadError);
            Assert.NotNull(service.BackupCorruptConfigPath);
            Assert.True(File.Exists(service.BackupCorruptConfigPath));

            // Verify original corrupted content was NOT overwritten with default JSON
            string contentAfterLoad = File.ReadAllText(configPath);
            Assert.Equal(malformedJson, contentAfterLoad);
        }

        [Fact]
        public void LoadConfig_SupabaseWithoutDeviceId_AutoGeneratesDeviceIdFromMachineName()
        {
            // Arrange
            string configPath = Path.Combine(_tempDirectory, "config.json");
            string json = @"{
  ""Supabase"": {
    ""Enabled"": true,
    ""MerchantId"": ""merchant_test"",
    ""DeviceId"": """",
    ""SyncMode"": ""primary""
  }
}";
            File.WriteAllText(configPath, json);

            // Act
            var service = new ConfigService(configPath);

            // Assert
            Assert.NotNull(service.Config?.Supabase?.DeviceId);
            Assert.False(string.IsNullOrWhiteSpace(service.Config.Supabase.DeviceId));
            // Must contain a normalized version of MachineName (lowercase alphanumeric/dash)
            string machineName = Environment.MachineName.ToLower();
            string safeMachinePrefix = new string(machineName.Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray());
            Assert.StartsWith(safeMachinePrefix, service.Config.Supabase.DeviceId);
        }

        [Fact]
        public void SaveConfig_SupabaseSettings_PersistsMerchantIdAndDeviceId()
        {
            // Arrange
            string configPath = Path.Combine(_tempDirectory, "config.json");
            string json = @"{
  ""Supabase"": {
    ""Enabled"": true,
    ""MerchantId"": ""merchant_sembako_satu"",
    ""DeviceId"": ""kasir-pos-01"",
    ""EnforceTenantIsolation"": true,
    ""SyncMode"": ""primary"",
    ""SyncIntervalMinutes"": 15
  }
}";
            File.WriteAllText(configPath, json);
            var service = new ConfigService(configPath);

            // Act: trigger a save
            service.SaveConfig();

            // Assert: reload from disk and verify properties survived the round-trip
            var reloaded = new ConfigService(configPath);
            Assert.Equal("merchant_sembako_satu", reloaded.Config?.Supabase?.MerchantId);
            Assert.Equal("kasir-pos-01", reloaded.Config?.Supabase?.DeviceId);
            Assert.True(reloaded.Config?.Supabase?.EnforceTenantIsolation);
            Assert.Equal("primary", reloaded.Config?.Supabase?.SyncMode);
        }

        [Fact]
        public void LoadConfig_SupabaseWithExistingDeviceId_DoesNotOverrideIt()
        {
            // Arrange
            string configPath = Path.Combine(_tempDirectory, "config.json");
            const string existingDeviceId = "kasir-utama-01";
            string json = $@"{{
  ""Supabase"": {{
    ""Enabled"": true,
    ""DeviceId"": ""{existingDeviceId}"",
    ""SyncMode"": ""primary""
  }}
}}";
            File.WriteAllText(configPath, json);

            // Act
            var service = new ConfigService(configPath);

            // Assert: DeviceId yang sudah ada tidak boleh diganti
            Assert.Equal(existingDeviceId, service.Config?.Supabase?.DeviceId);
        }
    }
}
