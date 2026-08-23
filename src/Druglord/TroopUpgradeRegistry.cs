using System;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace Druglord;

internal static class TroopUpgradeRegistry
{
    internal const string RecruitId = "druglord_recruit";
    internal const string AssaultId = "druglord_assault";
    internal const string SniperId = "druglord_sniper";

    private static readonly string[] PeasantIds =
    {
        "villager_aserai",
        "villager_battania",
        "villager_empire",
        "villager_khuzait",
        "villager_sturgia",
        "villager_vlandia"
    };

    private static Game? _configuredGame;

    internal static void EnsureConfigured(Game game)
    {
        if (ReferenceEquals(_configuredGame, game))
        {
            return;
        }

        CharacterObject recruit =
            game.ObjectManager.GetObject<CharacterObject>(RecruitId) ??
            throw new InvalidOperationException(
                "Druglord Recruit troop is unavailable.");
        CharacterObject assault =
            game.ObjectManager.GetObject<CharacterObject>(AssaultId) ??
            throw new InvalidOperationException(
                "Druglord Assault troop is unavailable.");
        CharacterObject sniper =
            game.ObjectManager.GetObject<CharacterObject>(SniperId) ??
            throw new InvalidOperationException(
                "Druglord Sniper troop is unavailable.");

        if (!ContainsUpgradeTarget(recruit, assault))
        {
            throw new InvalidOperationException(
                "Druglord Recruit must upgrade to Druglord Assault.");
        }

        if (!ContainsUpgradeTarget(assault, sniper))
        {
            throw new InvalidOperationException(
                "Druglord Assault must upgrade to Druglord Sniper.");
        }

        MethodInfo setUpgradeTargets =
            AccessTools.PropertySetter(
                typeof(CharacterObject),
                nameof(CharacterObject.UpgradeTargets)) ??
            throw new MissingMethodException(
                typeof(CharacterObject).FullName,
                $"set_{nameof(CharacterObject.UpgradeTargets)}");

        foreach (string peasantId in PeasantIds)
        {
            CharacterObject peasant =
                game.ObjectManager.GetObject<CharacterObject>(peasantId) ??
                throw new InvalidOperationException(
                    $"Bannerlord peasant troop '{peasantId}' is unavailable.");

            if (ContainsUpgradeTarget(peasant, recruit))
            {
                continue;
            }

            CharacterObject[] existingTargets =
                peasant.UpgradeTargets ?? Array.Empty<CharacterObject>();
            CharacterObject[] updatedTargets =
                new CharacterObject[existingTargets.Length + 1];

            Array.Copy(
                existingTargets,
                updatedTargets,
                existingTargets.Length);
            updatedTargets[updatedTargets.Length - 1] = recruit;

            setUpgradeTargets.Invoke(
                peasant,
                new object[] { updatedTargets });
        }

        _configuredGame = game;
        Debug.Print(
            "Druglord: added Recruit as an upgrade for all peasant cultures.");
    }

    private static bool ContainsUpgradeTarget(
        CharacterObject source,
        CharacterObject target)
    {
        CharacterObject[] targets =
            source.UpgradeTargets ?? Array.Empty<CharacterObject>();

        foreach (CharacterObject candidate in targets)
        {
            if (ReferenceEquals(candidate, target) ||
                string.Equals(
                    candidate.StringId,
                    target.StringId,
                    StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
