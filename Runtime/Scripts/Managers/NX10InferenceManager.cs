using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Unity.InferenceEngine;
using UnityEngine;
using UnityEngine.Events;

namespace NX10
{
    public class NX10InferenceManager : MonoBehaviour
    {
        [Tooltip("Optional custom file name on disk. Leave empty to infer from URL.")]
        [SerializeField] private string customFileName = "";

        [Tooltip("If true, bypasses any locally cached version and downloads a fresh copy.")]
        [SerializeField] private bool forceRedownload = false;

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

        private Coroutine _activeInferenceRoutine;
        public bool IsInferring => _activeInferenceRoutine != null;

        private void Awake()
        {
            Service = new NX10InferenceModelService(backendType, cacheFolderName);

            Service.OnDownloadProgress += progress => OnDownloadProgressChanged?.Invoke(progress);
            Service.OnModelLoaded += () => OnModelReady?.Invoke();
            Service.OnError += error => OnModelLoadFailed?.Invoke(error);
        }

        public async Task<bool> InitializeModelAsync(string modelUrl)
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

        public void RunInference(Action<float[]> onResult, float maxMsPerFrame = 2.5f)
        {
            if (!IsReady)
            {
                Debug.LogWarning("[NX10] Cannot start inference: Model is not ready.", this);
                return;
            }

            if (IsInferring)
            {
                Debug.LogWarning("[NX10] Previous multi-frame inference is still running. Ignoring request.", this);
                return;
            }

            _activeInferenceRoutine = StartCoroutine(
                Service.Predict(
                    results =>
                    {
                        _activeInferenceRoutine = null;
                        onResult?.Invoke(results);
                    },
                    maxMsPerFrame
                )
            );
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