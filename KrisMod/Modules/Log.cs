using BepInEx.Logging;

namespace KrisMod.Modules
{
    /// <summary>Thin wrapper so every file can log without carrying the plugin reference around.</summary>
    internal static class Log
    {
        private static ManualLogSource _logger;

        internal static void Init(ManualLogSource logger) => _logger = logger;

        internal static void Info(object data) => _logger?.LogInfo(data);
        internal static void Warning(object data) => _logger?.LogWarning(data);
        internal static void Error(object data) => _logger?.LogError(data);
    }
}
