using System.Threading;
using System.Threading.Tasks;

namespace InferenceEngine.Core.ModelProviders
{
    /// <summary>
    /// Abstracts the acquisition of a local model file path,
    /// allowing for local, downloaded, or embedded models.
    /// </summary>
    public interface IModelProvider
    {
        /// <summary>
        /// Returns the local file system path to the ONNX model file.
        /// Implementations may download, extract, or otherwise prepare the file.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>The absolute path to the model file.</returns>
        Task<string> GetModelPathAsync(CancellationToken cancellationToken);
    }
}
