using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;

namespace Druglord;

internal sealed class OutlawPartyGrowthCampaignBehavior : CampaignBehaviorBase
{
    private const int GrowthStopPopulation = 200;
    private const int GrowthDivisor = 3;
    private const string LooterCultureId = "looters";

    public override void RegisterEvents()
    {
        CampaignEvents.DailyTickPartyEvent.AddNonSerializedListener(
            this,
            OnDailyTickParty);
    }

    public override void SyncData(IDataStore dataStore)
    {
    }

    internal static int CalculateTroopTypeGrowth(int currentCount)
    {
        if (currentCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(currentCount),
                currentCount,
                "Troop count cannot be negative.");
        }

        return currentCount == 0
            ? 0
            : Math.Max(1, currentCount / GrowthDivisor);
    }

    private void OnDailyTickParty(MobileParty party)
    {
        if (!party.IsActive ||
            !party.IsBandit ||
            string.Equals(
                party.Party.Culture?.StringId,
                LooterCultureId,
                StringComparison.Ordinal))
        {
            return;
        }

        GrowRoster(party.MemberRoster);
        GrowRoster(party.PrisonRoster);
    }

    private static void GrowRoster(TroopRoster roster)
    {
        if (roster.TotalManCount >= GrowthStopPopulation)
        {
            return;
        }

        List<TroopRosterElement> troopTypes =
            new List<TroopRosterElement>();

        foreach (TroopRosterElement element in
                 roster.GetTroopRoster())
        {
            if (element.Number <= 0 || element.Character.IsHero)
            {
                continue;
            }

            troopTypes.Add(element);
        }

        foreach (TroopRosterElement troopType in troopTypes)
        {
            roster.AddToCounts(
                troopType.Character,
                CalculateTroopTypeGrowth(troopType.Number));
        }
    }
}
