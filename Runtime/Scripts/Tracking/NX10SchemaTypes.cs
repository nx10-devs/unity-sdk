using System;
using UnityEngine;

namespace NX10
{
    /// <summary>
    /// Strongly-typed view identifier for the "view" attribute, with presets and a custom escape hatch.
    /// </summary>
    public readonly struct NX10View : IEquatable<NX10View>
    {
        public string Value { get; }
        public bool IsValid => !string.IsNullOrWhiteSpace(Value);

        private NX10View(string value)
        {
            Value = value;
        }

        public static readonly NX10View MainMenu = new NX10View(NX10SchemaKeys.Views.MainMenu);
        public static readonly NX10View Settings = new NX10View(NX10SchemaKeys.Views.Settings);
        public static readonly NX10View LevelResults = new NX10View(NX10SchemaKeys.Views.LevelResults);
        public static readonly NX10View Gameplay = new NX10View(NX10SchemaKeys.Views.Gameplay);
        public static readonly NX10View Map = new NX10View(NX10SchemaKeys.Views.Map);
        public static readonly NX10View Inventory = new NX10View(NX10SchemaKeys.Views.Inventory);
        public static readonly NX10View Shop = new NX10View(NX10SchemaKeys.Views.Shop);
        public static readonly NX10View InterstitialAd = new NX10View(NX10SchemaKeys.Views.InterstitialAd);
        public static readonly NX10View RewardedAd = new NX10View(NX10SchemaKeys.Views.RewardedAd);

        /// <summary>
        /// Creates a custom view value (prefer snake_case, e.g. "vip_shop").
        /// </summary>
        public static NX10View Custom(string customView)
        {
            if (string.IsNullOrWhiteSpace(customView))
            {
                Debug.LogWarning("[NX10] Custom NX10View value should not be null or empty.");
                return default;
            }
            return new NX10View(customView.Trim());
        }

        public bool Equals(NX10View other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is NX10View other && Equals(other);
        public override int GetHashCode() => Value != null ? StringComparer.Ordinal.GetHashCode(Value) : 0;
        public override string ToString() => Value ?? string.Empty;
        public static bool operator ==(NX10View left, NX10View right) => left.Equals(right);
        public static bool operator !=(NX10View left, NX10View right) => !left.Equals(right);
    }

    /// <summary>
    /// Non-banner ad types that require ads_in_set and ad_number metadata.
    /// For banner ads, use <see cref="NX10Manager.StartBannerAd"/>.
    /// </summary>
    public readonly struct NX10AdSetType : IEquatable<NX10AdSetType>
    {
        public string Value { get; }
        public bool IsValid => !string.IsNullOrWhiteSpace(Value);

        private NX10AdSetType(string value)
        {
            Value = value;
        }

        public static readonly NX10AdSetType Interstitial = new NX10AdSetType(NX10SchemaKeys.AdTypes.Interstitial);
        public static readonly NX10AdSetType Rewarded = new NX10AdSetType(NX10SchemaKeys.AdTypes.Rewarded);
        public static readonly NX10AdSetType AppStart = new NX10AdSetType(NX10SchemaKeys.AdTypes.AppStart);

        /// <summary>
        /// Creates a custom non-banner ad type (prefer snake_case, e.g. "rewarded_interstitial").
        /// </summary>
        public static NX10AdSetType Custom(string customType)
        {
            if (string.IsNullOrWhiteSpace(customType))
            {
                Debug.LogWarning("[NX10] Custom NX10AdSetType value should not be null or empty.");
                return default;
            }
            return new NX10AdSetType(customType.Trim());
        }

        public bool Equals(NX10AdSetType other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is NX10AdSetType other && Equals(other);
        public override int GetHashCode() => Value != null ? StringComparer.Ordinal.GetHashCode(Value) : 0;
        public override string ToString() => Value ?? string.Empty;
        public static bool operator ==(NX10AdSetType left, NX10AdSetType right) => left.Equals(right);
        public static bool operator !=(NX10AdSetType left, NX10AdSetType right) => !left.Equals(right);
    }

    /// <summary>
    /// Strongly-typed reward type for rewarded_offer_shown and rewarded_offer_hidden events.
    /// </summary>
    public readonly struct NX10OfferType : IEquatable<NX10OfferType>
    {
        public string Value { get; }
        public bool IsValid => !string.IsNullOrWhiteSpace(Value);

        private NX10OfferType(string value)
        {
            Value = value;
        }

        public static readonly NX10OfferType UpgradeSpeed = new NX10OfferType(NX10SchemaKeys.OfferTypes.UpgradeSpeed);
        public static readonly NX10OfferType Coins = new NX10OfferType(NX10SchemaKeys.OfferTypes.Coins);
        public static readonly NX10OfferType Diamonds = new NX10OfferType(NX10SchemaKeys.OfferTypes.Diamonds);

        /// <summary>
        /// Creates a custom offer type (prefer snake_case, e.g. "chips").
        /// </summary>
        public static NX10OfferType Custom(string customType)
        {
            if (string.IsNullOrWhiteSpace(customType))
            {
                Debug.LogWarning("[NX10] Custom NX10OfferType value should not be null or empty.");
                return default;
            }
            return new NX10OfferType(customType.Trim());
        }

        public bool Equals(NX10OfferType other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is NX10OfferType other && Equals(other);
        public override int GetHashCode() => Value != null ? StringComparer.Ordinal.GetHashCode(Value) : 0;
        public override string ToString() => Value ?? string.Empty;
        public static bool operator ==(NX10OfferType left, NX10OfferType right) => left.Equals(right);
        public static bool operator !=(NX10OfferType left, NX10OfferType right) => !left.Equals(right);
    }

    /// <summary>
    /// Strongly-typed trigger reason for rewarded_offer_shown and rewarded_offer_hidden events.
    /// </summary>
    public readonly struct NX10OfferTrigger : IEquatable<NX10OfferTrigger>
    {
        public string Value { get; }
        public bool IsValid => !string.IsNullOrWhiteSpace(Value);

        private NX10OfferTrigger(string value)
        {
            Value = value;
        }

        public static readonly NX10OfferTrigger LowCoinsBalance = new NX10OfferTrigger(NX10SchemaKeys.OfferTriggers.LowCoinsBalance);
        public static readonly NX10OfferTrigger RevivePlayer = new NX10OfferTrigger(NX10SchemaKeys.OfferTriggers.RevivePlayer);

        /// <summary>
        /// Creates a custom offer trigger (prefer snake_case, e.g. "daily_bonus_double").
        /// </summary>
        public static NX10OfferTrigger Custom(string customTrigger)
        {
            if (string.IsNullOrWhiteSpace(customTrigger))
            {
                Debug.LogWarning("[NX10] Custom NX10OfferTrigger value should not be null or empty.");
                return default;
            }
            return new NX10OfferTrigger(customTrigger.Trim());
        }

        public bool Equals(NX10OfferTrigger other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is NX10OfferTrigger other && Equals(other);
        public override int GetHashCode() => Value != null ? StringComparer.Ordinal.GetHashCode(Value) : 0;
        public override string ToString() => Value ?? string.Empty;
        public static bool operator ==(NX10OfferTrigger left, NX10OfferTrigger right) => left.Equals(right);
        public static bool operator !=(NX10OfferTrigger left, NX10OfferTrigger right) => !left.Equals(right);
    }

    /// <summary>
    /// Strongly-typed dismissal/hide reason for rewarded_offer_hidden events.
    /// </summary>
    public readonly struct NX10OfferHideReason : IEquatable<NX10OfferHideReason>
    {
        public string Value { get; }
        public bool IsValid => !string.IsNullOrWhiteSpace(Value);

        private NX10OfferHideReason(string value)
        {
            Value = value;
        }

        public static readonly NX10OfferHideReason UserClosed = new NX10OfferHideReason(NX10SchemaKeys.OfferHideReasons.UserClosed);
        public static readonly NX10OfferHideReason ViewChanged = new NX10OfferHideReason(NX10SchemaKeys.OfferHideReasons.ViewChanged);
        public static readonly NX10OfferHideReason Expired = new NX10OfferHideReason(NX10SchemaKeys.OfferHideReasons.Expired);

        /// <summary>
        /// Creates a custom hide reason (prefer snake_case, e.g. "accepted").
        /// </summary>
        public static NX10OfferHideReason Custom(string customReason)
        {
            if (string.IsNullOrWhiteSpace(customReason))
            {
                Debug.LogWarning("[NX10] Custom NX10OfferHideReason value should not be null or empty.");
                return default;
            }
            return new NX10OfferHideReason(customReason.Trim());
        }

        public bool Equals(NX10OfferHideReason other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is NX10OfferHideReason other && Equals(other);
        public override int GetHashCode() => Value != null ? StringComparer.Ordinal.GetHashCode(Value) : 0;
        public override string ToString() => Value ?? string.Empty;
        public static bool operator ==(NX10OfferHideReason left, NX10OfferHideReason right) => left.Equals(right);
        public static bool operator !=(NX10OfferHideReason left, NX10OfferHideReason right) => !left.Equals(right);
    }
}

