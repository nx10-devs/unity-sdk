using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;
using Unity.InferenceEngine;

namespace NX10
{
    public class NX10InferenceManager : MonoBehaviour
    {
        [Header("Model Source Configuration")]
        [Tooltip("Direct URL to download the .onnx or .sentis model.")]
        [SerializeField] private string modelUrl = "https://example.com/models/classifier.onnx";

        [Tooltip("Optional custom file name on disk. Leave empty to infer from URL.")]
        [SerializeField] private string customFileName = "";

        [Tooltip("If true, bypasses any locally cached version and downloads a fresh copy.")]
        [SerializeField] private bool forceRedownload = false;

        [Tooltip("Automatically begins download/load routine on Start().")]
        [SerializeField] private bool loadOnStart = true;

        [Header("Backend Configuration")]
        [SerializeField] private BackendType backendType = BackendType.GPUCompute;
        [SerializeField] private string cacheFolderName = "ModelCache";

        [Header("Events")]
        public UnityEvent<float> OnDownloadProgressChanged;
        public UnityEvent OnModelReady;
        public UnityEvent<string> OnModelLoadFailed;

        public NX10InferenceModelService Service { get; private set; }
        public bool IsReady => Service != null && Service.IsReady;

        private CancellationTokenSource _cts;

        private void Awake()
        {
            Service = new NX10InferenceModelService(backendType, cacheFolderName);

            // Forward service events to UnityEvents (main-thread compatible)
            Service.OnDownloadProgress += progress => OnDownloadProgressChanged?.Invoke(progress);
            Service.OnModelLoaded += () => OnModelReady?.Invoke();
            Service.OnError += error => OnModelLoadFailed?.Invoke(error);
        }

        private async void Start()
        {
            if (loadOnStart)
            {
                await InitializeModelAsync();
            }
        }

        /// <summary>
        /// Starts the remote load routine. Can be called manually if loadOnStart is false.
        /// </summary>
        public async Task<bool> InitializeModelAsync()
        {
            if (string.IsNullOrWhiteSpace(modelUrl))
            {
                Debug.LogError("[NX10] Model URL is empty.", this);
                return false;
            }

            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();

            string fileName = string.IsNullOrWhiteSpace(customFileName) ? null : customFileName;

            return await Service.LoadFromUrlAsync(modelUrl, fileName, forceRedownload, _cts.Token);
        }

        /// <summary>
        /// Predict wrapper passing directly through to the service.
        /// </summary>
        public float[] RunInference(float[] inputData, TensorShape shape, string outputNode = null)
        {
            if (!IsReady)
            {
                Debug.LogWarning("[NX10] Cannot execute inference: Model is not ready yet.", this);
                return null;
            }

            return Service.Predict(inputData, shape, outputNode);
        }

        /// <summary>
        /// Clears local disk cache for this service's cache directory.
        /// </summary>
        public void ClearModelCache()
        {
            Service?.ClearCache();
            Debug.Log("[NX10] Model cache directory cleared.", this);
        }

        private void OnDestroy()
        {
            // Cancel any in-flight download requests
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;

            // Dispose GPU workers, native tensors, and models
            Service?.Dispose();
        }
    }
}