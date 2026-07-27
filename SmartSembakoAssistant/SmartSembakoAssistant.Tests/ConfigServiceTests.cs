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
    }
}
