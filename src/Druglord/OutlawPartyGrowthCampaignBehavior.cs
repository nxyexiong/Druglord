using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace Druglord;

internal sealed class OutlawPartyGrowthCampaignBehavior : CampaignBehaviorBase
{
    private static readonly Dictionary<string, string>
        PeasantIdByOutlawCulture =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["looters"] = "villager_empire",
                ["sea_raiders"] = "villager_sturgia",
                ["mountain_bandits"] = "villager_vlandia",
                ["forest_bandits"] = "villager_battania",
                ["desert_bandits"] = "villager_aserai",
                ["steppe_bandits"] = "villager_khuzait"
            };

    private readonly HashSet<string> _reportedMissingPeasantCultures =
        new HashSet<string>(StringComparer.Ordinal);

    public override void RegisterEvents()
    {
        CampaignEvents.DailyTickPartyEvent.AddNonSerializedListener(
            this,
            OnDailyTickParty);
    }

    public override void SyncData(IDataStore dataStore)
    {
    }

    internal static int CalculateDailyGrowth(int currentSize)
    {
        if (currentSize < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(currentSize),
                currentSize,
                "Party size cannot be negative.");
        }

        // Exact integer form of max(0, floor(-0.1x + 5)).
        int growthNumerator = 50 - currentSize;
        return growthNumerator > 0
            ? growthNumerator / 10
            : 0;
    }

    private void OnDailyTickParty(MobileParty party)
    {
        if (!party.IsActive || !party.IsBandit)
        {
            return;
        }

        GrowMembers(party);
        GrowPrisoners(party);
    }

    private static void GrowMembers(MobileParty party)
    {
        int growth = CalculateDailyGrowth(
            party.MemberRoster.TotalManCount);
        if (growth == 0)
        {
            return;
        }

        List<TroopRosterElement> weightedTroops =
            new List<TroopRosterElement>();
        int totalWeight = 0;

        foreach (TroopRosterElement element in
                 party.MemberRoster.GetTroopRoster())
        {
            if (element.Number <= 0 || element.Character.IsHero)
            {
                continue;
            }

            weightedTroops.Add(element);
            totalWeight += element.Number;
        }

        if (totalWeight == 0)
        {
            return;
        }

        for (int addition = 0; addition < growth; addition++)
        {
            int roll = MBRandom.RandomInt(totalWeight);

            foreach (TroopRosterElement element in weightedTroops)
            {
                if (roll < element.Number)
                {
                    party.MemberRoster.AddToCounts(
                        element.Character,
                        1);
                    break;
                }

                roll -= element.Number;
            }
        }
    }

    private void GrowPrisoners(MobileParty party)
    {
        int growth = CalculateDailyGrowth(
            party.PrisonRoster.TotalManCount);
        if (growth == 0)
        {
            return;
        }

        CharacterObject? peasant = ResolvePeasant(party);
        if (peasant is null)
        {
            return;
        }

        party.PrisonRoster.AddToCounts(peasant, growth);
    }

    private CharacterObject? ResolvePeasant(MobileParty party)
    {
        CultureObject? partyCulture = party.Party.Culture;
        CharacterObject? peasant = partyCulture?.Villager;
        if (peasant is not null)
        {
            return peasant;
        }

        CultureObject? homeCulture = party.HomeSettlement?.Culture;
        peasant = homeCulture?.Villager;
        if (peasant is not null)
        {
            return peasant;
        }

        string? cultureId =
            partyCulture?.StringId ?? homeCulture?.StringId;
        if (cultureId is not null &&
            PeasantIdByOutlawCulture.TryGetValue(
                cultureId,
                out string peasantId))
        {
            return Game.Current.ObjectManager
                       .GetObject<CharacterObject>(peasantId) ??
                   throw new InvalidOperationException(
                       $"Mapped peasant troop '{peasantId}' is unavailable.");
        }

        string reportKey = cultureId ?? "<none>";
        if (_reportedMissingPeasantCultures.Add(reportKey))
        {
            Debug.Print(
                "Druglord: no peasant troop mapping exists for outlaw " +
                $"culture '{reportKey}'.");
        }

        return null;
    }
}
