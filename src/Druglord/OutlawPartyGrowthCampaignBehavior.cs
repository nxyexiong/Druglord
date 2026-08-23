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

    private const int GrowthStopPopulation = 200;
    private const int GrowthDivisor = 3;
    private const string LooterCultureId = "looters";

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

    internal static int CalculateTroopTypeGrowth(int currentCount)
    {
        if (currentCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(currentCount),
                currentCount,
                "Troop count cannot be negative.");
        }

        return Math.Max(1, currentCount / GrowthDivisor);
    }

    private void OnDailyTickParty(MobileParty party)
    {
        if (!party.IsActive || !party.IsBandit)
        {
            return;
        }

        if (!string.Equals(
                party.Party.Culture?.StringId,
                LooterCultureId,
                StringComparison.Ordinal))
        {
            GrowRoster(party.MemberRoster);
        }

        GrowPrisoners(party);
    }

    private void GrowPrisoners(MobileParty party)
    {
        if (party.PrisonRoster.TotalManCount > 0)
        {
            GrowRoster(party.PrisonRoster);
            return;
        }

        CharacterObject? peasant = ResolvePeasant(party);
        if (peasant is null)
        {
            return;
        }

        party.PrisonRoster.AddToCounts(
            peasant,
            CalculateTroopTypeGrowth(0));
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
