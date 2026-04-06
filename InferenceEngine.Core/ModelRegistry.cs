using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace InferenceEngine.Core
{
    /// <summary>
    /// Looks up ONNX model download URLs by model name using a bundled models.json registry.
    /// </summary>
    public class ModelRegistry
    {
        private readonly ILogger _logger;
        private readonly Dictionary<string, string[]> _entries;

        public ModelRegistry(string? registryFilePath = null, ILogger? logger = null)
        {
            _logger = logger ?? NullLogger.Instance;
            _entries = Load(registryFilePath);
        }

        /// <summary>
        /// Returns download URLs for the given model name, or null if not found.
        /// </summary>
        public string[]? GetModelUrls(string modelName)
        {
            if (_entries.TryGetValue(modelName, out var urls))
            {
                return urls;
            }
            _logger.LogDebug("Model '{ModelName}' not found in registry.", modelName);
            return null;
        }

        private Dictionary<string, string[]> Load(string? filePath)
        {
            // Resolve path: explicit > beside the assembly > working directory
            filePath ??= FindRegistryFile();

            if (filePath is null || !File.Exists(filePath))
            {
                _logger.LogDebug("models.json registry not found; registry will be empty.");
                return new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            }

            try
            {
                var json = File.ReadAllText(filePath);
                var dict = JsonSerializer.Deserialize<Dictionary<string, string[]>>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                _logger.LogDebug("Loaded {Count} model entries from registry '{Path}'.", dict?.Count ?? 0, filePath);
                return dict ?? new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse models.json. Registry will be empty.");
                return new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private static string? FindRegistryFile()
        {
            var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (assemblyDir is not null)
            {
                var candidate = Path.Combine(assemblyDir, "models.json");
                if (File.Exists(candidate)) return candidate;
            }

            var cwdCandidate = Path.Combine(Directory.GetCurrentDirectory(), "models.json");
            return File.Exists(cwdCandidate) ? cwdCandidate : null;
        }
    }
}
