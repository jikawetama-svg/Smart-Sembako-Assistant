using System.IO;

namespace SmartSembakoAssistant.Helpers
{
    internal static class RuntimePaths
    {
        private const string PortableMarkerFileName = "portable.mode";
        private const string ProductFolderName = "Smart Sembako Assistant";

        private static bool? _isPortableMode;

        public static string AppBaseDirectory => AppDomain.CurrentDomain.BaseDirectory;

        public static bool IsPortableMode => _isPortableMode ??= DetectPortableMode();

        public static string WritableRootDirectory => IsPortableMode
            ? AppBaseDirectory
            : EnsureDirectory(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                ProductFolderName));

        public static string DataDirectory => EnsureDirectory(Path.Combine(WritableRootDirectory, "data"));

        public static string ConfigFilePath => EnsureParentDirectory(Path.Combine(WritableRootDirectory, "config.json"));

        public static string MemoryDatabasePath => EnsureParentDirectory(Path.Combine(DataDirectory, "memory.db"));

        public static string LogsDirectory => EnsureDirectory(Path.Combine(DataDirectory, "logs"));

        public static string BaileysSessionDirectory => EnsureDirectory(Path.Combine(DataDirectory, "baileys-session"));

        public static string BundledNodeBinaryPath => Path.Combine(AppBaseDirectory, "runtimes", "node", "node.exe");

        public static string BundledCloudflaredBinaryPath => Path.Combine(AppBaseDirectory, "runtimes", "cloudflared", "cloudflared.exe");

        public static string ResolveWritablePath(string? configuredPath, string fallbackRelativePath)
        {
            if (string.IsNullOrWhiteSpace(configuredPath))
            {
                return EnsureParentDirectory(Path.Combine(WritableRootDirectory, fallbackRelativePath));
            }

            if (Path.IsPathRooted(configuredPath))
            {
                return EnsureParentDirectory(configuredPath);
            }

            return EnsureParentDirectory(Path.Combine(WritableRootDirectory, configuredPath));
        }

        private static bool DetectPortableMode()
        {
            string markerPath = Path.Combine(AppBaseDirectory, PortableMarkerFileName);
            return File.Exists(markerPath);
        }

        private static string EnsureDirectory(string path)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            return path;
        }

        private static string EnsureParentDirectory(string path)
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                EnsureDirectory(directory);
            }

            return path;
        }
    }
}
