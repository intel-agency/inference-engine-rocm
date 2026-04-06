using System.Threading.Tasks;

namespace InferenceEngine.Core
{
    /// <summary>
    /// Defines the public contract for an inference engine.
    /// </summary>
    /// <typeparam name="TInput">The type of input data (e.g., an image, a string).</typeparam>
    /// <typeparam name="TOutput">The type of output data (e.g., a list of detections).</typeparam>
    public interface IInferenceEngine<TInput, TOutput> : IDisposable
    {
        /// <summary>
        /// Loads the model and initializes the inference session.
        /// </summary>
        Task LoadAsync();

        /// <summary>
        /// Runs inference on the given input and returns the prediction.
        /// </summary>
        /// <param name="input">The input data to run inference on.</param>
        /// <returns>The inference result.</returns>
        Task<TOutput> PredictAsync(TInput input);
    }
}
