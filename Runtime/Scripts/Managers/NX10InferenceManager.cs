using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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

        private NX10TelemetryManager _telemetryManager;
        private NX10TelemetryInferenceWindow inferenceWindow => _telemetryManager.CurrentInferenceWindow;

        private void Awake()
        {
            Service = new NX10InferenceModelService(backendType, cacheFolderName);

            Service.OnDownloadProgress += progress => OnDownloadProgressChanged?.Invoke(progress);
            Service.OnModelLoaded += () => OnModelReady?.Invoke();
            Service.OnError += error => OnModelLoadFailed?.Invoke(error);
        }

        public void Initialise(NX10TelemetryManager telemetryManager)
        {
            _telemetryManager = telemetryManager;
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

            Tensor<float> accTensor = GetAccelTensor();
            Tensor<float> gyroTensor = GetGyroTensor();
            Tensor<float> magTensor = GetMagTensor();
            Tensor<float> touchTensor = GetTouchTensor();

            Dictionary<string, Tensor> tensors = new Dictionary<string, Tensor>()
            {
                { "acc_raw", accTensor },
                { "gyro_raw", gyroTensor },
                { "touch_raw", touchTensor },
                { "mag_raw", magTensor }
            };

            LogInferenceInputsToFile(tensors);

            _activeInferenceRoutine = StartCoroutine(
                Service.Predict(
                    results =>
                    {
                        _activeInferenceRoutine = null;
                        onResult?.Invoke(results);

                        foreach (var tensor in tensors.Values)
                        {
                            tensor?.Dispose();
                        }
                    },
                    maxMsPerFrame,
                    tensors
                )
            );
        }

        private Tensor<float> GetAccelTensor()
        {
            float offsetInference = (float)Math.Round(inferenceWindow.AccDelta().TotalMilliseconds, 3, MidpointRounding.AwayFromZero);
            if(inferenceWindow.accelEvents.Count > 0)
            {
                List<float> flatAccel = new List<float>(inferenceWindow.accelEvents);
                flatAccel.Add(offsetInference);
                Tensor<float> accFloat = new Tensor<float>(new TensorShape(flatAccel.Count), flatAccel.ToArray());

                return accFloat;
            }
            else
            {
                float[] flatAccel = new float[5];
                flatAccel[4] = offsetInference;
                Tensor<float> accFloat = new Tensor<float>(new TensorShape(flatAccel.Length), flatAccel);

                return accFloat;
            }


        }

        private Tensor<float> GetGyroTensor()
        {
            float offsetInference = (float)Math.Round(inferenceWindow.GyroDelta().TotalMilliseconds, 3, MidpointRounding.AwayFromZero);
            if(inferenceWindow.gyroEvents.Count > 0)
            {
                List<float> flatGyro = new List<float>(inferenceWindow.gyroEvents);
                flatGyro.Add(offsetInference);
                Tensor<float> gyroFloat = new Tensor<float>(new TensorShape(flatGyro.Count), flatGyro.ToArray());
                return gyroFloat;

            }
            else
            {
                float[] flatGyro = new float[5];
                flatGyro[4] = offsetInference;
                Tensor<float> gyroFloat = new Tensor<float>(new TensorShape(flatGyro.Length), flatGyro);

                return gyroFloat;
            }
            
        }

        private Tensor<float> GetMagTensor()
        {
            float offsetInference = (float)Math.Round(inferenceWindow.MagDelta().TotalMilliseconds, 3, MidpointRounding.AwayFromZero);
            if(inferenceWindow.magEvents.Count > 0)
            {
                List<float> flatMag = new List<float>(inferenceWindow.magEvents);
                flatMag.Add(offsetInference);
                Tensor<float> magFloat = new Tensor<float>(new TensorShape(flatMag.Count), flatMag.ToArray());

                return magFloat;
            }
            else
            {
                float[] flatMag = new float[5];
                flatMag[4] = offsetInference;
                Tensor<float> magFloat = new Tensor<float>(new TensorShape(flatMag.Length), flatMag);

                return magFloat;
            }
        
        }

        private Tensor<float> GetTouchTensor()
        {
            float offsetInference = (float)Math.Round(inferenceWindow.TouchDelta().TotalMilliseconds, 3, MidpointRounding.AwayFromZero);

            if(inferenceWindow.touchEvents.Count > 0)
            {
                List<float> flatTouch = new List<float>(inferenceWindow.touchEvents);
                flatTouch.Add(offsetInference);
                Tensor<float> magFloat = new Tensor<float>(new TensorShape(flatTouch.Count), flatTouch.ToArray());

                return magFloat;
            }
            else
            {
                float[] flatTouch = new float[7];
                flatTouch[6] = offsetInference;
                Tensor<float> touchFloat = new Tensor<float>(new TensorShape(flatTouch.Length), flatTouch);

                return touchFloat;
            }
            
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
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;

            Service?.Dispose();
        }

        private void LogInferenceInputsToFile(Dictionary<string, Tensor> tensors)
        {
            try
            {
                string logDirectory = Path.Combine(Application.persistentDataPath, "InferenceLogs");
                if (!Directory.Exists(logDirectory))
                {
                    Directory.CreateDirectory(logDirectory);
                }

                string filePath = Path.Combine(logDirectory, $"inference_inputs_{DateTime.UtcNow:yyyy-MM-dd}.txt");

                var sb = new StringBuilder();
                sb.AppendLine($"==================================================");
                sb.AppendLine($"Timestamp (UTC): {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}");
                sb.AppendLine($"Frame Count: {Time.frameCount}");
                sb.AppendLine($"--------------------------------------------------");

                foreach (var kvp in tensors)
                {
                    string tensorName = kvp.Key;
                    Tensor tensor = kvp.Value;

                    if (tensor is Tensor<float> floatTensor)
                    {
                        float[] values = floatTensor.DownloadToArray();
                        sb.AppendLine($"Input: {tensorName} | Shape: {tensor.shape} | Count: {values.Length}");
                        sb.AppendLine($"Values: [{string.Join(", ", values)}]");
                    }
                    else
                    {
                        sb.AppendLine($"Input: {tensorName} | Shape: {tensor?.shape.ToString() ?? "null"} (Non-float or null tensor)");
                    }
                    sb.AppendLine();
                }

                sb.AppendLine(); 

                File.AppendAllText(filePath, sb.ToString());
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NX10] Failed to write inference inputs to file: {ex.Message}");
            }
        }
    }
}