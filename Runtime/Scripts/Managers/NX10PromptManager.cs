using System;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
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

            StartPolling();
        }

        private void OnDisable()
        {
            _= StopPollingAsync();
        }

        public void StartPolling()
        {
            if (_pollingTask != null && !_pollingTask.IsCompleted)
            {
                Console.WriteLine("Polling is already running.");
                return;
            }

            _cts = new CancellationTokenSource();
            _pollingTask = PollNetworkAsync(_cts.Token);
        }

        public async Task StopPollingAsync()
        {
            if (_cts == null || _pollingTask == null) return;

            _cts.Cancel();

            try
            {
                await _pollingTask;
            }
            catch (OperationCanceledException)
            {

            }
            finally
            {
                _cts.Dispose();
                _cts = null;
                _pollingTask = null;
            }
        }

        private async Task PollNetworkAsync(CancellationToken cancellationToken)
        {
            int delayMilliseconds = (int)(saaqPollingPeriod * 1000);

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    networkingManager.CheckPrompt();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error during network check: {ex.Message}");
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

        public void ShowPrompt(SAAQBlock promptData, float timer, bool dismissable, Action<SAAQAnswer> promptAnsweredAction)
        {
            uiController.ShowPrompt(promptData, timer, dismissable, (answer) =>
            {
                promptAnsweredAction(answer);
            });
        }
    }
}

