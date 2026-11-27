using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace NX10.Tests
{
    public class MacroFeatureRefreshTests
    {
        private const string TestMetricsEndpoint = "https://control-plane.affectstack-stage.com/routes/metrics";
        private const string TestBearerToken = "test-session-jwt-token";

        private GameObject _sdkRoot;
        private NX10Manager _manager;
        private NX10NetworkingManager _networking;
        private NX10TelemetryManager _telemetry;
        private NX10InferenceManager _inference;
        private NX10AttributesManager _attributes;

        private DateTime _mockUtcNow;

        [SetUp]
        public void SetUp()
        {
            _mockUtcNow = new DateTime(2026, 11, 27, 12, 0, 0, DateTimeKind.Utc);
            NX10SystemClock.SetUtcNowForTesting(() => _mockUtcNow);

            _sdkRoot = new GameObject("NX10_MacroRefreshTestRig");

            var netGo = new GameObject("NX10NetworkingManager");
            netGo.transform.SetParent(_sdkRoot.transform);
            _networking = netGo.AddComponent<NX10NetworkingManager>();

            var telGo = new GameObject("NX10TelemetryManager");
            telGo.transform.SetParent(_sdkRoot.transform);
            telGo.AddComponent<NativeGyro>();
            telGo.AddComponent<NativeScale>();
            telGo.AddComponent<NativeAccelerometer>();
            telGo.AddComponent<NativeMagnetometer>();
            telGo.AddComponent<NativeTouch>();
            _telemetry = telGo.AddComponent<NX10TelemetryManager>();

            var attrGo = new GameObject("NX10AttributesManager");
            attrGo.transform.SetParent(_sdkRoot.transform);
            _attributes = attrGo.AddComponent<NX10AttributesManager>();

            var infGo = new GameObject("NX10InferenceManager");
            infGo.transform.SetParent(_sdkRoot.transform);
            _inference = infGo.AddComponent<NX10InferenceManager>();

            _manager = _sdkRoot.AddComponent<NX10Manager>();

            InvokeLifecycleMethod(_telemetry, "Awake");
            InvokeLifecycleMethod(_telemetry, "OnEnable");
            InvokeLifecycleMethod(_inference, "Awake");
            InvokeLifecycleMethod(_manager, "Awake");

            _networking.MetricsEndpointOverride = TestMetricsEndpoint;
        }

        [TearDown]
        public void TearDown()
        {
            NX10SystemClock.SetUtcNowForTesting(null);

            if (_networking != null)
            {
                _networking.CancelMetricsRefresh();
            }

            if (_sdkRoot != null)
            {
                InvokeLifecycleMethod(_inference, "OnDestroy");
                InvokeLifecycleMethod(_telemetry, "OnDisable");
                InvokeLifecycleMethod(_telemetry, "OnDestroy");
                InvokeLifecycleMethod(_manager, "OnDestroy");
                UnityEngine.Object.DestroyImmediate(_sdkRoot);
            }
        }

        private static void InvokeLifecycleMethod(object target, string methodName, params object[] args)
        {
            if (target == null) return;
            MethodInfo mi = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            mi?.Invoke(target, args);
        }

        private NX10SDKSession SeedActiveSession(Dictionary<string, float> initialMetrics = null)
        {
            var session = new NX10SDKSession();
            session.Initialize(new SessionStartData
            {
                token = TestBearerToken,
                metrics = initialMetrics ?? new Dictionary<string, float>
                {
                    { "interstitial-ad-count", 10f },
                    { "interstitial-quit-count", 2f },
                    { "completed-session-count", 4f },
                    { "historical-median-active-seconds", 180f },
                    { "historical-median-event-add-per-minute", 5.5f },
                }
            });

            _manager.SetSessionInitializedForTesting(session);
            // Set acquisitionWindowSize > 0 without starting background sensor threads unless needed
            _telemetry.SetTelemetryVariables(null, null, null, null, null, 10, false, 326f);
            return session;
        }

        private static bool StepEnumeratorChain(Stack<IEnumerator> stack)
        {
            while (stack.Count > 0)
            {
                IEnumerator top = stack.Peek();
                if (!top.MoveNext())
                {
                    stack.Pop();
                    continue;
                }

                if (top.Current is IEnumerator nested)
                {
                    stack.Push(nested);
                    continue;
                }

                return true;
            }

            return false;
        }

        private static IEnumerator EmptyRoutine()
        {
            yield break;
        }

        private static IEnumerator SingleYieldRoutine()
        {
            yield return null;
        }

        [Test]
        public void Config_GetMetricsEndpoint_UsesControlPlaneRoutesMetrics()
        {
            var config = ScriptableObject.CreateInstance<NX10Config>();
            try
            {
                config.editorTarget = KeyType.Staging;
                config.devBuildTarget = KeyType.Staging;
                config.releaseBuildTarget = KeyType.Staging;
                Assert.AreEqual("https://control-plane.affectstack-stage.com/routes/metrics", config.GetMetricsEndPoint());

                config.editorTarget = KeyType.Production;
                config.devBuildTarget = KeyType.Production;
                config.releaseBuildTarget = KeyType.Production;
                Assert.AreEqual("https://control-plane.affectstack.com/routes/metrics", config.GetMetricsEndPoint());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void PauseAtOrUnder300Seconds_PreservesLocalMacroFeatures_AndDoesNotRefetchMetrics()
        {
            SeedActiveSession();
            _manager.AutoTrackAppLifecycle = true;

            int metricsFetchCalls = 0;
            _networking.MetricsTransportOverride = (uri, token, done) =>
            {
                metricsFetchCalls++;
                done(true, "{\"data\":{\"metrics\":{\"completed-session-count\":99}}}");
                return EmptyRoutine();
            };

            _manager.StartTelemetry();

            // Simulate 60s of active play with 2 custom events + 1 interstitial ad
            _mockUtcNow = _mockUtcNow.AddSeconds(30);
            _manager.SendEvent("hand_dealt");
            _manager.SendEvent("hand_won");
            _mockUtcNow = _mockUtcNow.AddSeconds(30);
            _manager.StartAd(NX10AdSetType.Interstitial);

            // Pause at t = 60s
            InvokeLifecycleMethod(_manager, "OnApplicationPause", true);

            // Resume after exactly 300.0s (threshold is strictly > 300s)
            _mockUtcNow = _mockUtcNow.AddSeconds(300.0);
            InvokeLifecycleMethod(_manager, "OnApplicationPause", false);

            Assert.AreEqual(0, metricsFetchCalls, "Inactivity of <= 300s must not trigger /routes/metrics");

            Dictionary<string, float[]> tensors = _inference.CaptureTensorArraysForTesting();

            // 3 gameplay events + app_backgrounded + app_foregrounded = 5 events in the same deployment session
            Assert.AreEqual(5f, tensors["current_session_event_add_prior_count"][0], 0.001f);
            // Interstitial happened at t = 60s; resume is at t = 360s -> 300s ago
            Assert.AreEqual(300f, tensors["seconds_since_previous_interstitial"][0], 0.01f);
            // Total elapsed deployment session time = 60s + 300s = 360s
            Assert.AreEqual(360f, tensors["elapsed_session_seconds"][0], 0.01f);
            // Cloud metrics unchanged
            Assert.AreEqual(4f, tensors["prior_sessions_count"][0], 0.001f);
        }

        [UnityTest]
        public IEnumerator PauseOver300Seconds_ResetsLocalMacroFeatures_AndRefetchesCloudMetrics()
        {
            SeedActiveSession();
            _manager.AutoTrackAppLifecycle = true;

            string capturedUri = null;
            string capturedToken = null;
            int fetchCount = 0;

            _networking.MetricsTransportOverride = (uri, token, done) =>
            {
                fetchCount++;
                capturedUri = uri;
                capturedToken = token;
                done(true, @"{
                    ""data"": {
                        ""metrics"": {
                            ""interstitial-ad-count"": 11.0,
                            ""interstitial-quit-count"": 3.0,
                            ""completed-session-count"": 5.0,
                            ""historical-median-active-seconds"": 210.5,
                            ""historical-median-event-add-per-minute"": 6.25
                        }
                    }
                }");
                return EmptyRoutine();
            };

            _manager.StartTelemetry();

            _mockUtcNow = _mockUtcNow.AddSeconds(45);
            _manager.SendEvent("round_started");
            _manager.StartAd(NX10AdSetType.Interstitial);

            // Background the app
            InvokeLifecycleMethod(_manager, "OnApplicationPause", true);

            // Exceed 300s inactivity (300.1s)
            _mockUtcNow = _mockUtcNow.AddSeconds(300.1);
            InvokeLifecycleMethod(_manager, "OnApplicationPause", false);

            // Allow nested coroutine slice to complete
            yield return null;

            Assert.AreEqual(1, fetchCount, "Expected exactly 1 GET /routes/metrics call after >300s pause");
            Assert.AreEqual(TestMetricsEndpoint, capturedUri);
            Assert.AreEqual(TestBearerToken, capturedToken);

            Dictionary<string, float[]> tensors = _inference.CaptureTensorArraysForTesting();

            // Local macro features restarted before app_foregrounded:
            // - elapsed_session_seconds restarted at 0
            // - seconds_since_previous_interstitial cleared to 0 (no interstitial in new deployment session)
            // - current_session_event_add_prior_count == 1 (the app_foregrounded event of the new deployment session)
            Assert.AreEqual(0f, tensors["elapsed_session_seconds"][0], 0.01f);
            Assert.AreEqual(0f, tensors["seconds_since_previous_interstitial"][0], 0.01f);
            Assert.AreEqual(1f, tensors["current_session_event_add_prior_count"][0], 0.001f);

            // Refreshed cloud macro features applied
            Assert.AreEqual(11f, tensors["prior_interstitial_count"][0], 0.001f);
            Assert.AreEqual(3f, tensors["prior_interstitial_quit_count"][0], 0.001f);
            Assert.AreEqual(5f, tensors["prior_sessions_count"][0], 0.001f);
            Assert.AreEqual(210.5f, tensors["historical_median_active_seconds"][0], 0.001f);
            Assert.AreEqual(6.25f, tensors["historical_median_event_add_per_active_minute"][0], 0.001f);
        }

        [UnityTest]
        public IEnumerator ManualStopAndStartTelemetryOver300Seconds_ResetsLocalFeatures_AndRefetchesMetrics()
        {
            SeedActiveSession();
            _manager.AutoTrackAppLifecycle = false;

            int fetchCount = 0;
            _networking.MetricsTransportOverride = (uri, token, done) =>
            {
                fetchCount++;
                done(true, "{\"data\":{\"metrics\":{\"completed-session-count\":8,\"interstitial-ad-count\":20,\"interstitial-quit-count\":1,\"historical-median-active-seconds\":95,\"historical-median-event-add-per-minute\":4.0}}}");
                return EmptyRoutine();
            };

            _manager.StartTelemetry();
            _mockUtcNow = _mockUtcNow.AddSeconds(20);
            _manager.SendEvent("bet_placed");
            _manager.StartAd(NX10AdSetType.Interstitial);

            // Stop telemetry manually without backgrounding
            _manager.StopTelemetry();

            // Remain inactive without telemetry for 310s
            _mockUtcNow = _mockUtcNow.AddSeconds(310);
            _manager.StartTelemetry();

            yield return null;

            Assert.AreEqual(1, fetchCount);

            Dictionary<string, float[]> tensors = _inference.CaptureTensorArraysForTesting();
            Assert.AreEqual(0f, tensors["elapsed_session_seconds"][0], 0.01f);
            Assert.AreEqual(0f, tensors["seconds_since_previous_interstitial"][0], 0.01f);
            Assert.AreEqual(0f, tensors["current_session_event_add_prior_count"][0], 0.001f);
            Assert.AreEqual(8f, tensors["prior_sessions_count"][0], 0.001f);
            Assert.AreEqual(20f, tensors["prior_interstitial_count"][0], 0.001f);
        }

        [Test]
        public void FailedMetricsRefetch_RetainsCurrentValuesAsFallback_AndRetriesWithExponentialBackoffUntilSuccess()
        {
            NX10SDKSession session = SeedActiveSession();

            var recordedDelays = new List<float>();
            int attempt = 0;

            _networking.MetricsTransportOverride = (uri, token, done) =>
            {
                attempt++;
                switch (attempt)
                {
                    case 1:
                        // Network error
                        done(false, string.Empty);
                        break;
                    case 2:
                        // Malformed JSON on 200
                        done(true, "{not-valid-json");
                        break;
                    case 3:
                        // Missing metrics payload on 200
                        done(true, "{\"data\":{}}");
                        break;
                    default:
                        // Attempt 4 succeeds!
                        done(true, @"{
                            ""data"": {
                                ""metrics"": {
                                    ""interstitial-ad-count"": 25.0,
                                    ""interstitial-quit-count"": 4.0,
                                    ""completed-session-count"": 9.0,
                                    ""historical-median-active-seconds"": 300.0,
                                    ""historical-median-event-add-per-minute"": 7.5
                                }
                            }
                        }");
                        break;
                }
                return EmptyRoutine();
            };

            _networking.MetricsRetryWaitOverride = delaySeconds =>
            {
                recordedDelays.Add(delaySeconds);

                // While failing/retrying, fallback values MUST remain intact!
                Dictionary<string, float[]> fallbackTensors = _inference.CaptureTensorArraysForTesting();
                Assert.AreEqual(10f, fallbackTensors["prior_interstitial_count"][0], 0.001f);
                Assert.AreEqual(2f, fallbackTensors["prior_interstitial_quit_count"][0], 0.001f);
                Assert.AreEqual(4f, fallbackTensors["prior_sessions_count"][0], 0.001f);
                Assert.AreEqual(180f, fallbackTensors["historical_median_active_seconds"][0], 0.001f);
                Assert.AreEqual(5.5f, fallbackTensors["historical_median_event_add_per_active_minute"][0], 0.001f);

                return SingleYieldRoutine();
            };

            bool? finalOutcome = null;
            IEnumerator routine = _networking.RefreshMetricsRoutine(
                TestMetricsEndpoint,
                session,
                0,
                () => _manager.IsSessionValid,
                succeeded => finalOutcome = succeeded);

            var stack = new Stack<IEnumerator>();
            stack.Push(routine);
            int safetySteps = 0;
            while (StepEnumeratorChain(stack) && safetySteps++ < 100) { }

            Assert.AreEqual(4, attempt, "Should retry 3 failures and stop immediately on the 4th (successful) attempt");
            Assert.AreEqual(true, finalOutcome);
            Assert.AreEqual(3, recordedDelays.Count, "Expected 3 backoff waits prior to the 4th attempt");

            // Verify exponential backoff bounds (2s, 4s, 8s +/- 20% jitter)
            Assert.That(recordedDelays[0], Is.InRange(1.6f, 2.4f));
            Assert.That(recordedDelays[1], Is.InRange(3.2f, 4.8f));
            Assert.That(recordedDelays[2], Is.InRange(6.4f, 9.6f));

            // Verify high retry counts cap cleanly at MetricsRetryMaxDelaySeconds (60s)
            Assert.AreEqual(60f, NX10NetworkingManager.ComputeBackoffDelaySeconds(20, 1.0f), 0.001f);
            Assert.That(NX10NetworkingManager.ComputeBackoffDelaySeconds(20, 0.0f), Is.InRange(48f, 60f));

            // Verify refreshed metrics are now active
            Dictionary<string, float[]> updatedTensors = _inference.CaptureTensorArraysForTesting();
            Assert.AreEqual(25f, updatedTensors["prior_interstitial_count"][0], 0.001f);
            Assert.AreEqual(4f, updatedTensors["prior_interstitial_quit_count"][0], 0.001f);
            Assert.AreEqual(9f, updatedTensors["prior_sessions_count"][0], 0.001f);
            Assert.AreEqual(300f, updatedTensors["historical_median_active_seconds"][0], 0.001f);
            Assert.AreEqual(7.5f, updatedTensors["historical_median_event_add_per_active_minute"][0], 0.001f);
        }

        [Test]
        public void StopSession_StopsRetryLoopImmediately_AndRetainsFallbackMetrics()
        {
            NX10SDKSession session = SeedActiveSession();

            int attempts = 0;
            _networking.MetricsTransportOverride = (uri, token, done) =>
            {
                attempts++;
                done(false, string.Empty);
                return EmptyRoutine();
            };

            _networking.MetricsRetryWaitOverride = _ =>
            {
                if (attempts == 2)
                {
                    _manager.StopSession();
                }
                return SingleYieldRoutine();
            };

            bool? completedOutcome = null;
            IEnumerator routine = _networking.RefreshMetricsRoutine(
                TestMetricsEndpoint,
                session,
                0,
                () => _manager.IsSessionValid,
                succeeded => completedOutcome = succeeded);

            var stack = new Stack<IEnumerator>();
            stack.Push(routine);
            int safetySteps = 0;
            while (StepEnumeratorChain(stack) && safetySteps++ < 100) { }

            Assert.AreEqual(2, attempts, "Retry loop must terminate once StopSession invalidates the session");
            Assert.AreEqual(false, completedOutcome);
            Assert.AreEqual(4f, session.GetMetric("completed-session-count"), 0.001f);
        }
    }
}
