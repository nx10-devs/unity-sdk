using System;
using System.Collections.Generic;
using UnityEngine;

namespace NX10
{
    /// <summary>
    /// Fluent batch builder for setting multiple NX10 attributes in a single network request.
    /// </summary>
    public sealed class NX10AttributesBatchBuilder
    {
        internal Dictionary<string, object> Attributes { get; } = new Dictionary<string, object>();

        public NX10AttributesBatchBuilder SetView(NX10View view)
        {
            if (!view.IsValid)
            {
                Debug.LogWarning("[NX10] Ignoring SetView in batch: NX10View is invalid.");
                return this;
            }
            Attributes[NX10SchemaKeys.Attributes.View] = view.Value;
            return this;
        }

        public NX10AttributesBatchBuilder SetBannerAdVisible(bool visible)
        {
            Attributes[NX10SchemaKeys.Attributes.BannerAdVisible] = visible;
            return this;
        }

        public NX10AttributesBatchBuilder SetInterstitialAdRateSeconds(int seconds)
        {
            Attributes[NX10SchemaKeys.Attributes.InterstitialAdRateSeconds] = seconds;
            return this;
        }

        public NX10AttributesBatchBuilder SetNoAds(bool noAds)
        {
            Attributes[NX10SchemaKeys.Attributes.NoAds] = noAds;
            return this;
        }

        public NX10AttributesBatchBuilder SetInTutorial(bool inTutorial)
        {
            Attributes[NX10SchemaKeys.Attributes.InTutorial] = inTutorial;
            return this;
        }

        public NX10AttributesBatchBuilder SetTotalActiveSaveSlots(int totalActiveSaveSlots)
        {
            Attributes[NX10SchemaKeys.Attributes.TotalActiveSaveSlots] = totalActiveSaveSlots;
            return this;
        }

        public NX10AttributesBatchBuilder SetCurrentSaveSlot(string currentSaveSlot)
        {
            if (currentSaveSlot == null)
            {
                Debug.LogWarning("[NX10] Ignoring SetCurrentSaveSlot in batch: slot value cannot be null.");
                return this;
            }
            Attributes[NX10SchemaKeys.Attributes.CurrentSaveSlot] = currentSaveSlot;
            return this;
        }

        /// <summary>
        /// Convenience helper to set both current_save_slot and total_active_save_slots together.
        /// </summary>
        public NX10AttributesBatchBuilder SetSaveSlots(string currentSaveSlot, int totalActiveSaveSlots)
        {
            SetCurrentSaveSlot(currentSaveSlot);
            SetTotalActiveSaveSlots(totalActiveSaveSlots);
            return this;
        }

        /// <summary>
        /// Escape hatch to include a custom attribute key/value in the same batched request.
        /// </summary>
        public NX10AttributesBatchBuilder SetCustom(string key, object value)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                Debug.LogWarning("[NX10] Ignoring SetCustom in batch: key cannot be null or empty.");
                return this;
            }
            Attributes[key] = value;
            return this;
        }
    }

    /// <summary>
    /// Internal implementation for setting standard NX10 user/game attributes.
    /// Exposed via wrapper methods on <see cref="NX10Manager"/>.
    /// </summary>
    internal sealed class NX10AttributesApi
    {
        private readonly NX10Manager _manager;

        internal NX10AttributesApi(NX10Manager manager)
        {
            _manager = manager;
        }

        /// <summary>
        /// Updates multiple attributes at once and dispatches a single network payload.
        /// </summary>
        public void Batch(Action<NX10AttributesBatchBuilder> configure)
        {
            if (configure == null)
                return;

            var builder = new NX10AttributesBatchBuilder();
            configure(builder);

            if (builder.Attributes.Count > 0)
            {
                _manager.SetAttributes(builder.Attributes);
            }
        }

        public void SetView(NX10View view)
        {
            if (!view.IsValid)
            {
                Debug.LogWarning("[NX10] Cannot set view attribute: NX10View is invalid.");
                return;
            }
            _manager.SetAttribute(NX10SchemaKeys.Attributes.View, view.Value);
        }

        public void SetBannerAdVisible(bool visible)
        {
            _manager.SetAttribute(NX10SchemaKeys.Attributes.BannerAdVisible, visible);
        }

        public void SetInterstitialAdRateSeconds(int seconds)
        {
            _manager.SetAttribute(NX10SchemaKeys.Attributes.InterstitialAdRateSeconds, seconds);
        }

        public void SetNoAds(bool noAds)
        {
            _manager.SetAttribute(NX10SchemaKeys.Attributes.NoAds, noAds);
        }

        public void SetInTutorial(bool inTutorial)
        {
            _manager.SetAttribute(NX10SchemaKeys.Attributes.InTutorial, inTutorial);
        }

        public void SetTotalActiveSaveSlots(int totalActiveSaveSlots)
        {
            _manager.SetAttribute(NX10SchemaKeys.Attributes.TotalActiveSaveSlots, totalActiveSaveSlots);
        }

        public void SetCurrentSaveSlot(string currentSaveSlot)
        {
            if (currentSaveSlot == null)
            {
                Debug.LogWarning("[NX10] Cannot set current_save_slot attribute: value cannot be null.");
                return;
            }
            _manager.SetAttribute(NX10SchemaKeys.Attributes.CurrentSaveSlot, currentSaveSlot);
        }

        /// <summary>
        /// Updates both current_save_slot and total_active_save_slots in a single network request.
        /// </summary>
        public void SetSaveSlots(string currentSaveSlot, int totalActiveSaveSlots)
        {
            Batch(b => b.SetSaveSlots(currentSaveSlot, totalActiveSaveSlots));
        }
    }
}

