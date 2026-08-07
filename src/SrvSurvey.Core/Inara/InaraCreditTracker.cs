using System.Globalization;
using Newtonsoft.Json.Linq;

// Behavioral reference: EDMarketConnector's monitor.py and plugins/inara.py.
// Copyright (c) EDCD, licensed under GNU GPL v2 or later.
// API guidance: https://inara.cz/elite/inara-api-docs/

namespace SrvSurvey.Core.Inara
{
    /// <summary>
    /// Maintains the best journal-derived commander balance available between
    /// authoritative LoadGame snapshots. Inara recommends reporting credits at
    /// session boundaries, on significant changes, or at hourly intervals rather
    /// than recording every transaction in the commander's credits log.
    /// </summary>
    internal sealed class InaraCreditTracker
    {
        internal static readonly TimeSpan ReportInterval = TimeSpan.FromHours(1);

        private long? credits;
        private long? loan;
        private long? assets;
        private DateTimeOffset? lastReportAt;

        public bool HasUnreportedChanges { get; private set; }

        public void Reset()
        {
            credits = null;
            loan = null;
            assets = null;
            lastReportAt = null;
            HasUnreportedChanges = false;
        }

        public void Observe(JObject entry, bool inMulticrew)
        {
            var eventName = entry.Value<string>("event");
            if (eventName == "LoadGame")
            {
                ObserveLoadGame(entry);
                return;
            }

            // Journal activity while serving on somebody else's ship must not alter
            // the tracked balance for the local commander.
            if (inMulticrew)
            {
                return;
            }

            if (TryObserveExactBalance(entry, eventName) || !credits.HasValue)
            {
                return;
            }

            ApplyCreditDelta(ComputeCreditDelta(entry, eventName));
        }

        private void ObserveLoadGame(JObject entry)
        {
            Reset();
            credits = value(entry, "Credits");
            loan = value(entry, "Loan");
            HasUnreportedChanges = credits.HasValue;
        }

        private bool TryObserveExactBalance(JObject entry, string? eventName)
        {
            return eventName switch
            {
                "Statistics" => ObserveStatistics(entry),
                "CarrierBankTransfer" => ObserveCarrierBankTransfer(entry),
                _ => false,
            };
        }

        private bool ObserveStatistics(JObject entry)
        {
            var currentAssets = value(entry["Bank_Account"] as JObject, "Current_Wealth");
            if (currentAssets.HasValue && currentAssets != assets)
            {
                assets = currentAssets;
                HasUnreportedChanges = true;
            }

            return true;
        }

        private bool ObserveCarrierBankTransfer(JObject entry)
        {
            if (value(entry, "PlayerBalance") is not long playerBalance)
            {
                return false;
            }

            if (playerBalance != credits)
            {
                credits = playerBalance;
                HasUnreportedChanges = true;
            }

            return true;
        }

        private static long ComputeCreditDelta(JObject entry, string? eventName)
        {
            return TryShipAndModuleDelta(entry, eventName)
                ?? TryOdysseyDelta(entry, eventName)
                ?? TryTradeAndServiceDelta(entry, eventName)
                ?? 0;
        }

        private static long? TryShipAndModuleDelta(JObject entry, string? eventName)
        {
            return eventName switch
            {
                "ShipyardBuy" => -valueOrZero(entry, "ShipPrice"),
                "ModuleBuy" => -valueOrZero(entry, "BuyPrice"),
                "ModuleRetrieve" or "ModuleStore" => -valueOrZero(entry, "Cost"),
                "ModuleSell" or "ModuleSellRemote" => valueOrZero(entry, "SellPrice"),
                "SellShipOnRebuy" or "ShipyardSell" => valueOrZero(entry, "ShipPrice"),
                "ShipyardTransfer" => -valueOrZero(entry, "TransferPrice"),
                "FetchRemoteModule" => -valueOrZero(entry, "TransferCost"),
                _ => null,
            };
        }

        private static long? TryOdysseyDelta(JObject entry, string? eventName)
        {
            return eventName switch
            {
                "BuyMicroResources" or "BuySuit" or "BuyWeapon" => -valueOrZero(entry, "Price"),
                "SellMicroResources" or "SellSuit" or "SellWeapon" => valueOrZero(entry, "Price"),
                "UpgradeSuit" or "UpgradeWeapon" => -valueOrZero(entry, "Cost"),
                "SellOrganicData" => organicDataValue(entry),
                "BookDropship" or "BookTaxi" => -valueOrZero(entry, "Cost"),
                "CancelDropship" or "CancelTaxi" => valueOrZero(entry, "Refund"),
                _ => null,
            };
        }

        private static long? TryTradeAndServiceDelta(JObject entry, string? eventName)
        {
            return eventName switch
            {
                "BuyDrones" or "MarketBuy" => -valueOrZero(entry, "TotalCost"),
                "MarketSell" or "SellDrones" => valueOrZero(entry, "TotalSale"),
                "MissionCompleted" or "CommunityGoalReward" => valueOrZero(entry, "Reward"),
                "MultiSellExplorationData" or "SellExplorationData" => valueOrZero(entry, "TotalEarnings"),
                "BuyExplorationData" or "BuyTradeData" or "BuyAmmo" or "CrewHire" => -valueOrZero(entry, "Cost"),
                "PayBounties" or "PayFines" or "PayLegacyFines" => -valueOrZero(entry, "Amount"),
                "RedeemVoucher" or "PowerplaySalary" => valueOrZero(entry, "Amount"),
                "RefuelAll" or "RefuelPartial" or "Repair" or "RepairAll" or "RestockVehicle" => -valueOrZero(entry, "Cost"),
                "PowerplayFastTrack" => -valueOrZero(entry, "Cost"),
                "CarrierBuy" => -valueOrZero(entry, "Price"),
                "NpcCrewPaidWage" => -valueOrZero(entry, "Amount"),
                "Resurrect" => -valueOrZero(entry, "Cost"),
                _ => null,
            };
        }

        private void ApplyCreditDelta(long delta)
        {
            if (delta == 0 || !credits.HasValue)
            {
                return;
            }

            var updatedCredits = credits.Value + delta;
            if (updatedCredits < 0)
            {
                // A missing or malformed journal delta means the reconstructed
                // balance is no longer trustworthy. Wait for the next exact
                // LoadGame or CarrierBankTransfer value instead of uploading it.
                credits = null;
                HasUnreportedChanges = false;
                return;
            }

            credits = updatedCredits;
            HasUnreportedChanges = true;
        }

        public InaraEvent? CreateReport(string timestamp, bool force, bool includeAssets = false)
        {
            if (!credits.HasValue)
            {
                return null;
            }

            var reportAt = parseTimestamp(timestamp);
            if (!force && !ShouldReport(reportAt))
            {
                return null;
            }

            lastReportAt = reportAt;
            HasUnreportedChanges = false;
            return new InaraEvent(
                "setCommanderCredits",
                timestamp,
                BuildCreditsPayload(includeAssets),
                "credits");
        }

        private bool ShouldReport(DateTimeOffset reportAt)
        {
            if (!HasUnreportedChanges)
            {
                return false;
            }

            return !lastReportAt.HasValue
                || reportAt - lastReportAt.Value >= ReportInterval;
        }

        private JObject BuildCreditsPayload(bool includeAssets)
        {
            var data = new JObject
            {
                ["commanderCredits"] = credits!.Value,
            };
            if (loan.HasValue)
            {
                data["commanderLoan"] = loan.Value;
            }

            // Current_Wealth is authoritative only at the Statistics timestamp.
            // Omitting a later stale value lets Inara calculate assets from its data.
            if (includeAssets && assets.HasValue)
            {
                data["commanderAssets"] = assets.Value;
            }

            return data;
        }

        private static DateTimeOffset parseTimestamp(string timestamp) =>
            DateTimeOffset.TryParse(
                timestamp,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out var parsed)
                    ? parsed
                    : DateTimeOffset.UtcNow;

        private static long organicDataValue(JObject entry) =>
            (entry["BioData"] as JArray)?.OfType<JObject>()
                .Sum(item => valueOrZero(item, "Value") + valueOrZero(item, "Bonus")) ?? 0;

        private static long valueOrZero(JObject? entry, string property) => value(entry, property) ?? 0;

        private static long? value(JObject? entry, string property)
        {
            var token = entry?[property];
            if (token == null || token.Type is JTokenType.Null or JTokenType.Undefined) return null;
            return token.Value<long?>();
        }
    }
}
