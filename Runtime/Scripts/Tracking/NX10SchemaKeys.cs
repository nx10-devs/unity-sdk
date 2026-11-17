namespace NX10
{
    /// <summary>
    /// Canonical wire keys and preset values for NX10 SDK events and attributes.
    /// </summary>
    public static class NX10SchemaKeys
    {
        public static class Attributes
        {
            public const string View = "view";
            public const string BannerAdVisible = "banner_ad_visible";
            public const string InterstitialAdRateSeconds = "interstitial_ad_rate_seconds";
            public const string NoAds = "no_ads";
            public const string InTutorial = "in_tutorial";
            public const string TotalActiveSaveSlots = "total_active_save_slots";
            public const string CurrentSaveSlot = "current_save_slot";
        }

        public static class Events
        {
            public const string AppQuit = "app_quit";
            public const string AppBackgrounded = "app_backgrounded";
            public const string AppForegrounded = "app_foregrounded";
            public const string AdStarted = "ad_started";
            public const string AdFinished = "ad_finished";
            public const string RewardedOfferShown = "rewarded_offer_shown";
            public const string RewardedOfferHidden = "rewarded_offer_hidden";
        }

        public static class Metadata
        {
            public const string Id = "id";
            public const string Type = "type";
            public const string AdsInSet = "ads_in_set";
            public const string AdNumber = "ad_number";
            public const string Amount = "amount";
            public const string Trigger = "trigger";
            public const string Reason = "reason";
        }

        public static class Views
        {
            public const string MainMenu = "main_menu";
            public const string Settings = "settings";
            public const string LevelResults = "level_results";
            public const string Gameplay = "gameplay";
            public const string Map = "map";
            public const string Inventory = "inventory";
            public const string Shop = "shop";
            public const string InterstitialAd = "interstitial_ad";
            public const string RewardedAd = "rewarded_ad";
        }

        public static class AdTypes
        {
            public const string Banner = "banner";
            public const string Interstitial = "interstitial";
            public const string Rewarded = "rewarded";
            public const string AppStart = "app_start";
        }

        public static class OfferTypes
        {
            public const string UpgradeSpeed = "upgrade_speed";
            public const string Coins = "coins";
            public const string Diamonds = "diamonds";
        }

        public static class OfferTriggers
        {
            public const string LowCoinsBalance = "low_coins_balance";
            public const string RevivePlayer = "revive_player";
        }

        public static class OfferHideReasons
        {
            public const string UserClosed = "user_closed";
            public const string ViewChanged = "view_changed";
            public const string Expired = "expired";
        }
    }
}

