using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace NX10
{
    public enum PromptType
    {
        SaaqType1,
        SaaqType2,
        SaaqType3,
        SaaqType4,
        SaaqType5,
    }

    public enum FeelingType
    {
        VeryFrustrated = 0,
        Frustrated = 1,
        Neutral = 2,
        Enjoyment = 3,
        Ecstatic = 4,
        Bored = 5,
        Surprised = 6,
        Relaxed = 7,
        Fun = 8,
    }

    public class NX10PromptManager : MonoBehaviour
    {
        [SerializeField] private PromptUiController uiController;
        public PromptUiController PromptUiController => uiController;

        private float saaqPollingPeriod;
        private NX10NetworkingManager networkingManager;

        private CancellationTokenSource _cts;
        private Task _pollingTask;

        public void Initalise(float pollingPeriod, NX10NetworkingManager networkingManager)
        {
            PromptUiController.Initialise(this);
            this.saaqPollingPeriod = pollingPeriod;
            this.networkingManager = networkingManager;

            if (NX10Manager.Instance.CanCollect && pollingPeriod > 0f)
            {
                StartPolling();
            }
        }

        private void OnDisable()
        {
            StopPolling();
        }

        public void StartPolling()
        {
            if (!NX10Manager.Instance.CanCollect || saaqPollingPeriod <= 0f)
                return;

            if (_pollingTask != null && !_pollingTask.IsCompleted)
            {
                return;
            }

            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            _pollingTask = PollNetworkAsync(_cts.Token);
        }

        public void StopPolling()
        {
            if (_cts == null)
                return;

            try
            {
                _cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        public async Task StopPollingAsync()
        {
            if (_cts == null || _pollingTask == null) return;

            var cts = _cts;
            var task = _pollingTask;
            _cts = null;
            _pollingTask = null;

            cts.Cancel();

            try
            {
                await task;
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                cts.Dispose();
            }
        }

        private async Task PollNetworkAsync(CancellationToken cancellationToken)
        {
            int delayMilliseconds = Mathf.Max(100, (int)(saaqPollingPeriod * 1000));

            while (!cancellationToken.IsCancellationRequested)
            {
                if (!NX10Manager.Instance.CanCollect)
                    break;

                try
                {
                    networkingManager?.CheckPrompt();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[NX10] Error during SAAQ poll: {ex.Message}");
                }

                try
                {
                    await Task.Delay(delayMilliseconds, cancellationToken);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }

        public void ShowPrompt(SAAQBlock promptData, bool dismissable, Action<SAAQAnswer> promptAnsweredAction)
        {
            if (!NX10Manager.Instance.CanCollect || promptData == null)
                return;

            uiController.ShowPrompt(promptData, dismissable, (answer) =>
            {
                promptAnsweredAction?.Invoke(answer);
            });
        }
    }
}
