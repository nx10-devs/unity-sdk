using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using Unity.InferenceEngine;

namespace NX10
{
    public class NX10InferenceModelService : IDisposable
    {
        private Model _runtimeModel;
        private Worker _worker;
        private readonly BackendType _backendType;
        private readonly string _cacheDirectory;

        public bool IsReady => _worker != null;
        public string CacheDirectory => _cacheDirectory;

        public event Action<float> OnDownloadProgress;
        public event Action OnModelLoaded;
        public event Action<string> OnError;

        public NX10InferenceModelService(
            BackendType backendType = BackendType.GPUCompute,
            string subfolder = "ModelCache")
        {
            _backendType = backendType;
            _cacheDirectory = Path.Combine(Application.persistentDataPath, subfolder);

            if (!Directory.Exists(_cacheDirectory))
            {
                Directory.CreateDirectory(_cacheDirectory);
            }
        }

        /// <summary>
        /// Downloads the model from a remote URL to persistent storage (if not already cached)
        /// and initializes the inference worker.
        /// </summary>
        /// <param name="url">The direct URL to the .onnx or .sentis model.</param>
        /// <param name="fileName">Optional local filename. If null, extracted from the URL or generated.</param>
        /// <param name="forceRedownload">If true, overwrites any existing cached file.</param>
        /// <param name="ct">Cancellation token to cancel downloads cleanly.</param>
        public async Task<bool> LoadFromUrlAsync(
            string url,
            string fileName = null,
            bool forceRedownload = false,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                OnError?.Invoke("Model URL cannot be null or empty.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = ExtractFileNameFromUrl(url);
            }

            string targetPath = Path.Combine(_cacheDirectory, fileName);

            try
            {
                if (forceRedownload || !File.Exists(targetPath))
                {
                    await DownloadModelFileAsync(url, targetPath, ct);
                }

                LoadFromFile(targetPath);
                OnModelLoaded?.Invoke();
                return true;
            }
            catch (OperationCanceledException)
            {
                OnError?.Invoke("Model download/loading was cancelled.");
                return false;
            }
            catch (Exception ex)
            {
                OnError?.Invoke($"Failed to load model from URL: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Synchronously loads a model already present on disk.
        /// </summary>
        public void LoadFromFile(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"Model file not found at: {filePath}");
            }

            Dispose();
            _runtimeModel = ModelLoader.Load(filePath);
            _worker = new Worker(_runtimeModel, _backendType);
        }

        /// <summary>
        /// Executes inference on the provided raw input and shape.
        /// </summary>
        public float[] Predict(float[] rawInput, TensorShape shape, string outputName = null)
        {
            if (_worker == null)
            {
                throw new InvalidOperationException("Worker is not initialized. Call LoadFromUrlAsync or LoadFromFile first.");
            }

            using var inputTensor = new Tensor<float>(shape, rawInput);
            _worker.Schedule(inputTensor);

            Tensor<float> output = string.IsNullOrEmpty(outputName)
                ? _worker.PeekOutput() as Tensor<float>
                : _worker.PeekOutput(outputName) as Tensor<float>;

            return output?.DownloadToArray();
        }

        private async Task DownloadModelFileAsync(string url, string destinationPath, CancellationToken ct)
        {
            using var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbGET);
            request.downloadHandler = new DownloadHandlerFile(destinationPath);

            var operation = request.SendWebRequest();

            while (!operation.isDone)
            {
                ct.ThrowIfCancellationRequested();
                OnDownloadProgress?.Invoke(operation.progress);
                await Task.Yield();
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                if (File.Exists(destinationPath))
                {
                    File.Delete(destinationPath);
                }
                throw new IOException($"Download failed ({request.responseCode}): {request.error}");
            }

            OnDownloadProgress?.Invoke(1.0f);
        }

        private static string ExtractFileNameFromUrl(string url)
        {
            try
            {
                var uri = new Uri(url);
                string leaf = Path.GetFileName(uri.LocalPath);
                if (!string.IsNullOrEmpty(leaf) && (leaf.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase) || leaf.EndsWith(".sentis", StringComparison.OrdinalIgnoreCase)))
                {
                    return leaf;
                }
            }
            catch { }

            return "downloaded_model.onnx";
        }

        /// <summary>
        /// Clears all cached models from disk.
        /// </summary>
        public void ClearCache()
        {
            if (Directory.Exists(_cacheDirectory))
            {
                Directory.Delete(_cacheDirectory, true);
                Directory.CreateDirectory(_cacheDirectory);
            }
        }

        public void Dispose()
        {
            _worker?.Dispose();
            _worker = null;
            _runtimeModel = null;
        }
    }
}