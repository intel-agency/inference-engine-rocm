using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace InferenceEngine.Core.ModelProviders
{
    /// <summary>
    /// A model provider that downloads an ONNX model to a local cache directory on first use.
    /// </summary>
    public class DownloadingModelProvider : IModelProvider
    {
        private readonly string _modelName;
        private readonly string[] _downloadUrls;
        private readonly string _cacheDirectory;
        private readonly ILogger _logger;

        public DownloadingModelProvider(
            string modelName,
            string[] downloadUrls,
            string cacheDirectory,
            ILogger? logger = null)
        {
            _modelName = modelName ?? throw new ArgumentNullException(nameof(modelName));
            _downloadUrls = downloadUrls ?? throw new ArgumentNullException(nameof(downloadUrls));
            _cacheDirectory = cacheDirectory ?? throw new ArgumentNullException(nameof(cacheDirectory));
            _logger = logger ?? NullLogger.Instance;
        }

        /// <inheritdoc />
        public async Task<string> GetModelPathAsync(CancellationToken cancellationToken)
        {
            var localPath = Path.Combine(_cacheDirectory, _modelName);

            if (File.Exists(localPath))
            {
                _logger.LogInformation("Model '{ModelName}' found in cache at '{Path}'.", _modelName, localPath);
                return localPath;
            }

            Directory.CreateDirectory(_cacheDirectory);

            Exception? lastException = null;
            foreach (var url in _downloadUrls)
            {
                try
                {
                    _logger.LogInformation("Downloading model '{ModelName}' from '{Url}'...", _modelName, url);
                    using var client = new HttpClient();
                    using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    response.EnsureSuccessStatusCode();

                    var tempPath = localPath + ".tmp";
                    await using (var fs = File.Create(tempPath))
                    {
                        await response.Content.CopyToAsync(fs, cancellationToken);
                    }

                    File.Move(tempPath, localPath, overwrite: true);
                    _logger.LogInformation("Model '{ModelName}' downloaded successfully to '{Path}'.", _modelName, localPath);
                    return localPath;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    lastException = ex;
                    _logger.LogWarning(ex, "Failed to download model from '{Url}'. Trying next URL...", url);
                }
            }

            throw new InvalidOperationException(
                $"Failed to download model '{_modelName}' from all {_downloadUrls.Length} URL(s).",
                lastException);
        }
    }
}
