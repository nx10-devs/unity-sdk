using System;
using System.Collections.Generic;
using UnityEngine;

namespace NX10
{
    /// <summary>
    /// Stateful handle returned when an ad starts. Call <see cref="Finish"/> when the ad closes
    /// to automatically send "ad_finished" with the matching UUID and ad-set metadata.
    /// </summary>
    public sealed class NX10AdHandle
    {
        private readonly NX10EventsApi _eventsApi;
        private readonly NX10AdSetType _adSetType;
        private readonly bool _isValid;

        public string Id { get; }
        public string Type { get; }
        public bool IsBanner { get; }
        public int? AdsInSet { get; }
        public int? AdNumber { get; }
        public bool IsFinished { get; private set; }

        internal NX10AdHandle(
            NX10EventsApi eventsApi,
            string id,
            string type,
            bool isBanner,
            NX10AdSetType adSetType,
            int? adsInSet,
            int? adNumber,
            bool isValid = true)
        {
            _eventsApi = eventsApi;
            Id = id;
            Type = type;
            IsBanner = isBanner;
            _adSetType = adSetType;
            AdsInSet = adsInSet;
            AdNumber = adNumber;
            _isValid = isValid;
        }

        /// <summary>
        /// Fires "ad_finished" using the same UUID, type, ads_in_set, and ad_number from when the ad started.
        /// Safe to call multiple times; subsequent calls are ignored.
        /// </summary>
        public void Finish()
        {
            if (IsFinished || !_isValid || _eventsApi == null)
                return;

            IsFinished = true;

            if (IsBanner)
            {
                _eventsApi.SendBannerAdFinished(Id);
            }
            else
            {
                _eventsApi.SendAdFinished(Id, _adSetType, AdsInSet.GetValueOrDefault(1), AdNumber.GetValueOrDefault(1));
            }
        }

        /// <summary>
        /// Starts the next sequential ad in a multi-ad set using the same group UUID and ads_in_set count.
        /// </summary>
        public NX10AdHandle StartNextAdInSet()
        {
            if (IsBanner || !_isValid || _eventsApi == null)
            {
                Debug.LogWarning("[NX10] StartNextAdInSet is only valid for non-banner ad sets.");
                return this;
            }

            int nextAdNumber = AdNumber.GetValueOrDefault(1) + 1;
            int totalInSet = AdsInSet.GetValueOrDefault(nextAdNumber);
            if (nextAdNumber > totalInSet)
            {
                Debug.LogWarning($"[NX10] Next ad number ({nextAdNumber}) exceeds ads_in_set ({totalInSet}).");
            }

            return _eventsApi.StartAd(_adSetType, totalInSet, nextAdNumber, Id);
        }
    }

    /// <summary>
    /// Stateful handle returned when a rewarded offer is shown. Call <see cref="Hide"/> when the offer closes
    /// to automatically send "rewarded_offer_hidden" with the matching UUID, type, amount, and trigger.
    /// </summary>
    public sealed class NX10OfferHandle
    {
        private readonly NX10EventsApi _eventsApi;
        private readonly bool _isValid;

        public string Id { get; }
        public NX10OfferType OfferType { get; }
        public int Amount { get; }
        public NX10OfferTrigger Trigger { get; }
        public bool IsHidden { get; private set; }

        internal NX10OfferHandle(
            NX10EventsApi eventsApi,
            string id,
            NX10OfferType offerType,
            int amount,
            NX10OfferTrigger trigger,
            bool isValid = true)
        {
            _eventsApi = eventsApi;
            Id = id;
            OfferType = offerType;
            Amount = amount;
            Trigger = trigger;
            _isValid = isValid;
        }

        /// <summary>
        /// Fires "rewarded_offer_hidden" with the provided reason and the original offer's UUID and metadata.
        /// Safe to call multiple times; subsequent calls are ignored.
        /// </summary>
        public void Hide(NX10OfferHideReason reason)
        {
            if (IsHidden || !_isValid || _eventsApi == null)
                return;

            if (!reason.IsValid)
            {
                Debug.LogWarning("[NX10] Cannot send rewarded_offer_hidden: hide reason is invalid.");
                return;
            }

            IsHidden = true;
            _eventsApi.SendRewardedOfferHidden(Id, OfferType, Amount, Trigger, reason);
        }
    }

    /// <summary>
    /// Internal implementation for sending standard NX10 events without manually constructing metadata dictionaries.
    /// Exposed via wrapper methods on <see cref="NX10Manager"/>.
    /// </summary>
    internal sealed class NX10EventsApi
    {
        private readonly NX10Manager _manager;

        internal NX10EventsApi(NX10Manager manager)
        {
            _manager = manager;
        }

        #region Lifecycle Events

        /// <summary>
        /// Sends the "app_quit" event. Sent automatically by <see cref="NX10Manager"/> unless auto-tracking is disabled.
        /// </summary>
        public void AppQuit()
        {
            _manager.SendEvent(NX10SchemaKeys.Events.AppQuit);
        }

        /// <summary>
        /// Sends the "app_backgrounded" event. Sent automatically by <see cref="NX10Manager"/> unless auto-tracking is disabled.
        /// </summary>
        public void AppBackgrounded()
        {
            _manager.SendEvent(NX10SchemaKeys.Events.AppBackgrounded);
        }

        /// <summary>
        /// Sends the "app_foregrounded" event. Sent automatically by <see cref="NX10Manager"/> unless auto-tracking is disabled.
        /// </summary>
        public void AppForegrounded()
        {
            _manager.SendEvent(NX10SchemaKeys.Events.AppForegrounded);
        }

        #endregion

        #region Ad Events (Stateful Handles)

        /// <summary>
        /// Sends "ad_started" for a banner ad (omitting ads_in_set and ad_number) and returns a handle to call <see cref="NX10AdHandle.Finish"/>.
        /// </summary>
        public NX10AdHandle StartBannerAd(string customId = null)
        {
            string id = ResolveId(customId);
            SendBannerAdStarted(id);
            return new NX10AdHandle(this, id, NX10SchemaKeys.AdTypes.Banner, true, default, null, null, true);
        }

        /// <summary>
        /// Sends "ad_started" for a non-banner ad (interstitial, rewarded, app_start, or custom) and returns a handle to call <see cref="NX10AdHandle.Finish"/>.
        /// </summary>
        /// <param name="type">Ad type (e.g. <see cref="NX10AdSetType.Interstitial"/>).</param>
        /// <param name="adsInSet">Total concurrent/sequential ads in this ad set (defaults to 1).</param>
        /// <param name="adNumber">1-based index of this ad within the set (defaults to 1).</param>
        /// <param name="groupId">Optional UUID shared across ads in the same set. Auto-generated when null or empty.</param>
        public NX10AdHandle StartAd(NX10AdSetType type, int adsInSet = 1, int adNumber = 1, string groupId = null)
        {
            if (!type.IsValid)
            {
                Debug.LogWarning("[NX10] Cannot send ad_started: ad type is invalid.");
                return new NX10AdHandle(this, string.Empty, string.Empty, false, type, adsInSet, adNumber, false);
            }

            int sanitizedAdsInSet = SanitizePositiveInt(adsInSet, NX10SchemaKeys.Metadata.AdsInSet);
            int sanitizedAdNumber = SanitizePositiveInt(adNumber, NX10SchemaKeys.Metadata.AdNumber);
            string id = ResolveId(groupId);

            SendAdStarted(id, type, sanitizedAdsInSet, sanitizedAdNumber);
            return new NX10AdHandle(this, id, type.Value, false, type, sanitizedAdsInSet, sanitizedAdNumber, true);
        }

        #endregion

        #region Rewarded Offer Events (Stateful Handle)

        /// <summary>
        /// Sends "rewarded_offer_shown" and returns a handle to call <see cref="NX10OfferHandle.Hide"/> when dismissed, expired, or navigated away.
        /// </summary>
        public NX10OfferHandle ShowRewardedOffer(NX10OfferType type, int amount, NX10OfferTrigger trigger, string customId = null)
        {
            if (!type.IsValid || !trigger.IsValid)
            {
                Debug.LogWarning("[NX10] Cannot send rewarded_offer_shown: offer type and trigger must be valid.");
                return new NX10OfferHandle(this, string.Empty, type, amount, trigger, false);
            }

            string id = ResolveId(customId);
            SendRewardedOfferShown(id, type, amount, trigger);
            return new NX10OfferHandle(this, id, type, amount, trigger, true);
        }

        #endregion

        #region Stateless Event Overloads

        public void SendBannerAdStarted(string id)
        {
            var data = new Dictionary<string, object>(2)
            {
                { NX10SchemaKeys.Metadata.Id, ResolveId(id) },
                { NX10SchemaKeys.Metadata.Type, NX10SchemaKeys.AdTypes.Banner }
            };
            _manager.SendEvent(NX10SchemaKeys.Events.AdStarted, data);
        }

        public void SendBannerAdFinished(string id)
        {
            var data = new Dictionary<string, object>(2)
            {
                { NX10SchemaKeys.Metadata.Id, ResolveId(id) },
                { NX10SchemaKeys.Metadata.Type, NX10SchemaKeys.AdTypes.Banner }
            };
            _manager.SendEvent(NX10SchemaKeys.Events.AdFinished, data);
        }

        public void SendAdStarted(string id, NX10AdSetType type, int adsInSet, int adNumber)
        {
            if (!type.IsValid)
            {
                Debug.LogWarning("[NX10] Cannot send ad_started: ad type is invalid.");
                return;
            }

            var data = BuildAdSetMetadata(ResolveId(id), type.Value, adsInSet, adNumber);
            _manager.SendEvent(NX10SchemaKeys.Events.AdStarted, data);
        }

        public void SendAdFinished(string id, NX10AdSetType type, int adsInSet, int adNumber)
        {
            if (!type.IsValid)
            {
                Debug.LogWarning("[NX10] Cannot send ad_finished: ad type is invalid.");
                return;
            }

            var data = BuildAdSetMetadata(ResolveId(id), type.Value, adsInSet, adNumber);
            _manager.SendEvent(NX10SchemaKeys.Events.AdFinished, data);
        }

        public void SendRewardedOfferShown(string id, NX10OfferType type, int amount, NX10OfferTrigger trigger)
        {
            if (!type.IsValid || !trigger.IsValid)
            {
                Debug.LogWarning("[NX10] Cannot send rewarded_offer_shown: offer type and trigger must be valid.");
                return;
            }

            var data = new Dictionary<string, object>(4)
            {
                { NX10SchemaKeys.Metadata.Id, ResolveId(id) },
                { NX10SchemaKeys.Metadata.Type, type.Value },
                { NX10SchemaKeys.Metadata.Amount, amount },
                { NX10SchemaKeys.Metadata.Trigger, trigger.Value }
            };
            _manager.SendEvent(NX10SchemaKeys.Events.RewardedOfferShown, data);
        }

        public void SendRewardedOfferHidden(string id, NX10OfferType type, int amount, NX10OfferTrigger trigger, NX10OfferHideReason reason)
        {
            if (!type.IsValid || !trigger.IsValid || !reason.IsValid)
            {
                Debug.LogWarning("[NX10] Cannot send rewarded_offer_hidden: offer type, trigger, and reason must be valid.");
                return;
            }

            var data = new Dictionary<string, object>(5)
            {
                { NX10SchemaKeys.Metadata.Id, ResolveId(id) },
                { NX10SchemaKeys.Metadata.Type, type.Value },
                { NX10SchemaKeys.Metadata.Amount, amount },
                { NX10SchemaKeys.Metadata.Trigger, trigger.Value },
                { NX10SchemaKeys.Metadata.Reason, reason.Value }
            };
            _manager.SendEvent(NX10SchemaKeys.Events.RewardedOfferHidden, data);
        }

        #endregion

        private static Dictionary<string, object> BuildAdSetMetadata(string id, string type, int adsInSet, int adNumber)
        {
            return new Dictionary<string, object>(4)
            {
                { NX10SchemaKeys.Metadata.Id, id },
                { NX10SchemaKeys.Metadata.Type, type },
                { NX10SchemaKeys.Metadata.AdsInSet, SanitizePositiveInt(adsInSet, NX10SchemaKeys.Metadata.AdsInSet) },
                { NX10SchemaKeys.Metadata.AdNumber, SanitizePositiveInt(adNumber, NX10SchemaKeys.Metadata.AdNumber) }
            };
        }

        private static string ResolveId(string customId)
        {
            return string.IsNullOrWhiteSpace(customId) ? Guid.NewGuid().ToString() : customId.Trim();
        }

        private static int SanitizePositiveInt(int value, string fieldName)
        {
            if (value < 1)
            {
                Debug.LogWarning($"[NX10] {fieldName} should be >= 1 (received {value}). Defaulting to 1.");
                return 1;
            }
            return value;
        }
    }
}

