using InferenceEngine.Core.ModelProviders;

namespace InferenceEngine.Core
{
    /// <summary>
    /// Configuration options for the inference engine.
    /// </summary>
    public class InferenceEngineOptions
    {
        /// <summary>
        /// Whether to use GPU acceleration when available. Defaults to true.
        /// </summary>
        public bool UseGpuAcceleration { get; set; } = true;

        /// <summary>
        /// The GPU device ID to use. Defaults to 0.
        /// </summary>
        public int DeviceId { get; set; } = 0;

        /// <summary>
        /// Whether to perform a warmup inference on load to initialize the model. Defaults to true.
        /// </summary>
        public bool WarmupOnLoad { get; set; } = true;

        /// <summary>
        /// An explicit model provider to use. When set, overrides the engine's built-in
        /// model resolution (ModelName + ModelDownloadUrls + ModelRegistry).
        /// </summary>
        public IModelProvider? ModelProvider { get; set; }
    }
}
