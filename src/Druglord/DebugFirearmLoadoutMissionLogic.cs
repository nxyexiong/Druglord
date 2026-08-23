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

    public override void OnDeploymentFinished()
    {
        base.OnDeploymentFinished();
        SpawnDebugItems();
        InformationManager.DisplayMessage(
            new InformationMessage(
                $"Druglord dropped {_loadouts.Count} loaded debug firearm(s) " +
                $"and {_loadouts.Count} ammunition stack(s) near the player."));
    }

    private void SpawnDebugItems()
    {
        Agent? mainAgent = Mission.MainAgent;
        if (mainAgent is null || !mainAgent.IsHuman)
        {
            throw new InvalidOperationException(
                "Druglord could not locate the debug battle player.");
        }

        if (_loadouts.Count == 0)
        {
            throw new InvalidOperationException(
                "Druglord debug weapons were not loaded.");
        }

        MatrixFrame agentFrame = mainAgent.Frame;
        Vec3 forward = agentFrame.rotation.f;
        forward.z = 0f;
        if (forward.Normalize() <= 0.001f)
        {
            forward = Vec3.Forward;
        }

        Vec3 side = agentFrame.rotation.s;
        side.z = 0f;
        if (side.Normalize() <= 0.001f)
        {
            side = Vec3.Side;
        }

        const int loadoutsPerRow = 4;
        const float firstRowDistance = 2.5f;
        const float rowSpacing = 1.4f;
        const float loadoutSpacing = 1.4f;
        const float pairSpacing = 0.4f;
        const float dropHeight = 0.75f;

        for (int index = 0; index < _loadouts.Count; index++)
        {
            int row = index / loadoutsPerRow;
            int column = index % loadoutsPerRow;
            int loadoutsInRow = Math.Min(
                loadoutsPerRow,
                _loadouts.Count - (row * loadoutsPerRow));
            float centeredColumn =
                column - ((loadoutsInRow - 1) * 0.5f);
            Vec3 pairCenter =
                mainAgent.Position +
                (forward * (firstRowDistance + (row * rowSpacing))) +
                (side * (centeredColumn * loadoutSpacing)) +
                (Vec3.Up * dropHeight);

            DebugLoadout loadout = _loadouts[index];
            MissionWeapon firearm = CreateLoadedFirearm(loadout);
            SpawnWeaponDrop(
                ref firearm,
                agentFrame,
                pairCenter - (side * pairSpacing));

            MissionWeapon ammunition =
                new MissionWeapon(loadout.Ammunition, null, null);
            SpawnWeaponDrop(
                ref ammunition,
                agentFrame,
                pairCenter + (side * pairSpacing));
        }

        Debug.Print(
            $"Druglord: spawned {_loadouts.Count} firearm and ammunition " +
            $"drop pair(s) near main agent {mainAgent.Index}.");
    }

    private static MissionWeapon CreateLoadedFirearm(
        DebugLoadout loadout)
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
        return firearm;
    }

    private void SpawnWeaponDrop(
        ref MissionWeapon weapon,
        MatrixFrame orientation,
        Vec3 position)
    {
        orientation.origin = position;
        Mission.SpawnWeaponWithNewEntity(
            ref weapon,
            TaleWorlds.MountAndBlade.Mission.WeaponSpawnFlags.WithPhysics,
            orientation);
    }
}
