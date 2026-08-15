using System;
using System.Collections.Generic;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace Druglord;

internal sealed class DebugFirearmLoadoutMissionLogic : MissionLogic
{
    private sealed class DebugLoadout
    {
        internal DebugLoadout(
            RifleSettings settings,
            ItemObject firearm,
            ItemObject ammunition)
        {
            Settings = settings;
            Firearm = firearm;
            Ammunition = ammunition;
        }

        internal RifleSettings Settings { get; }
        internal ItemObject Firearm { get; }
        internal ItemObject Ammunition { get; }
    }

    private readonly List<DebugLoadout> _loadouts =
        new List<DebugLoadout>();
    private int _equippedAgentCount;

    public override void OnBehaviorInitialize()
    {
        base.OnBehaviorInitialize();

        FirearmItemRegistry.EnsureLoaded(Game.Current);
        IReadOnlyList<RifleSettings> settings =
            RifleSettingsRegistry.GetDebugLoadouts(Game.Current);

        foreach (RifleSettings rifleSettings in settings)
        {
            ItemObject firearm =
                Game.Current.ObjectManager.GetObject<ItemObject>(
                    rifleSettings.ItemId) ??
                throw new InvalidOperationException(
                    $"Debug firearm '{rifleSettings.ItemId}' is unavailable.");
            ItemObject ammunition =
                Game.Current.ObjectManager.GetObject<ItemObject>(
                    rifleSettings.AmmunitionItemId) ??
                throw new InvalidOperationException(
                    $"Debug ammunition '{rifleSettings.AmmunitionItemId}' is unavailable.");

            _loadouts.Add(
                new DebugLoadout(
                    rifleSettings,
                    firearm,
                    ammunition));
        }
    }

    public override void OnAgentBuild(Agent agent, Banner banner)
    {
        base.OnAgentBuild(agent, banner);

        if (!agent.IsHuman)
        {
            return;
        }

        if (_loadouts.Count == 0)
        {
            const string error = "Druglord debug weapons were not loaded.";
            Debug.Print($"Druglord: {error}");
            return;
        }

        DebugLoadout loadout =
            _loadouts[_equippedAgentCount % _loadouts.Count];
        EquipFirearmLoadout(agent, loadout);
        _equippedAgentCount++;
        Debug.Print(
            $"Druglord: equipped {loadout.Firearm.Name} for agent " +
            $"{agent.Index}; total {_equippedAgentCount}.");
    }

    public override void OnDeploymentFinished()
    {
        base.OnDeploymentFinished();
        EquipMainAgentLoadout();
        InformationManager.DisplayMessage(
            new InformationMessage(
                $"Druglord equipped {_equippedAgentCount} soldiers with " +
                $"{_loadouts.Count} debug firearm loadout(s); the player " +
                "carries both the AKM and AWP."));
    }

    private void EquipMainAgentLoadout()
    {
        Agent? mainAgent = Mission.MainAgent;
        if (mainAgent is null || !mainAgent.IsHuman)
        {
            throw new InvalidOperationException(
                "Druglord could not equip the debug battle player.");
        }

        DebugLoadout akm = GetRequiredLoadout(FirearmItemRegistry.AkmId);
        DebugLoadout awp = GetRequiredLoadout(FirearmItemRegistry.AwpId);

        EquipFirearm(
            mainAgent,
            akm,
            EquipmentIndex.WeaponItemBeginSlot,
            EquipmentIndex.Weapon1);
        EquipFirearm(
            mainAgent,
            awp,
            EquipmentIndex.Weapon2,
            EquipmentIndex.Weapon3);

        Debug.Print(
            $"Druglord: equipped AKM and AWP for main agent " +
            $"{mainAgent.Index}.");
    }

    private DebugLoadout GetRequiredLoadout(string itemId)
    {
        foreach (DebugLoadout loadout in _loadouts)
        {
            if (string.Equals(
                    loadout.Settings.ItemId,
                    itemId,
                    StringComparison.Ordinal))
            {
                return loadout;
            }
        }

        throw new InvalidOperationException(
            $"Debug firearm loadout '{itemId}' is unavailable.");
    }

    private static void EquipFirearmLoadout(
        Agent agent,
        DebugLoadout loadout)
    {
        EquipFirearm(
            agent,
            loadout,
            EquipmentIndex.WeaponItemBeginSlot,
            EquipmentIndex.Weapon1);

        for (EquipmentIndex slot = EquipmentIndex.Weapon2;
             slot <= EquipmentIndex.Weapon3;
             slot++)
        {
            if (!agent.Equipment[slot].IsEmpty)
            {
                agent.RemoveEquippedWeapon(slot);
            }
        }
    }

    private static void EquipFirearm(
        Agent agent,
        DebugLoadout loadout,
        EquipmentIndex firearmSlot,
        EquipmentIndex ammunitionSlot)
    {
        MissionWeapon ammunitionStack =
            new MissionWeapon(loadout.Ammunition, null, null);
        MissionWeapon loadedAmmunition = ammunitionStack.Consume(
            loadout.Settings.MagazineSize);

        MissionWeapon firearm =
            new MissionWeapon(loadout.Firearm, null, null);
        firearm.ReloadAmmo(
            loadedAmmunition,
            firearm.ReloadPhaseCount);

        EquipWeapon(
            agent,
            firearmSlot,
            ref firearm);
        EquipWeapon(
            agent,
            ammunitionSlot,
            ref ammunitionStack);
    }

    private static void EquipWeapon(
        Agent agent,
        EquipmentIndex slot,
        ref MissionWeapon weapon)
    {
        agent.EquipWeaponWithNewEntity(slot, ref weapon);
    }
}
