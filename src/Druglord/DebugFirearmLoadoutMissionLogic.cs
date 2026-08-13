using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace Druglord;

internal sealed class DebugFirearmLoadoutMissionLogic : MissionLogic
{
    private ItemObject? _rifle;
    private ItemObject? _cartridges;
    private int _equippedAgentCount;

    public override void OnBehaviorInitialize()
    {
        base.OnBehaviorInitialize();

        FirearmItemRegistry.EnsureLoaded(Game.Current);
        _rifle = FirearmItemRegistry.Rifle;
        _cartridges = FirearmItemRegistry.Cartridges;
    }

    public override void OnAgentBuild(Agent agent, Banner banner)
    {
        base.OnAgentBuild(agent, banner);

        if (!agent.IsHuman)
        {
            return;
        }

        if (_rifle is null || _cartridges is null)
        {
            const string error = "Druglord debug weapons were not loaded.";
            Debug.Print($"Druglord: {error}");
            return;
        }

        EquipRifleLoadout(agent);
        _equippedAgentCount++;
        Debug.Print(
            $"Druglord: equipped rifle loadout for agent {agent.Index}; total {_equippedAgentCount}.");
    }

    public override void OnDeploymentFinished()
    {
        base.OnDeploymentFinished();
        InformationManager.DisplayMessage(
            new InformationMessage(
                $"Druglord equipped {_equippedAgentCount} soldiers with rifles."));
    }

    private void EquipRifleLoadout(Agent agent)
    {
        MissionWeapon cartridgeStack = new MissionWeapon(_cartridges, null, null);
        MissionWeapon rifleAmmo = cartridgeStack.Consume(
            RifleSettings.MagazineSize);

        MissionWeapon rifle = new MissionWeapon(_rifle, null, null);
        rifle.ReloadAmmo(rifleAmmo, rifle.ReloadPhaseCount);

        EquipWeapon(
            agent,
            EquipmentIndex.WeaponItemBeginSlot,
            ref rifle);
        EquipWeapon(agent, EquipmentIndex.Weapon1, ref cartridgeStack);

        for (EquipmentIndex slot = EquipmentIndex.Weapon2;
             slot <= EquipmentIndex.Weapon3;
             slot++)
        {
            if (!agent.Equipment[slot].IsEmpty)
            {
                agent.RemoveEquippedWeapon(slot);
            }
        }

        agent.TryToWieldWeaponInSlot(
            EquipmentIndex.WeaponItemBeginSlot,
            Agent.WeaponWieldActionType.InstantAfterPickUp,
            false);
    }

    private static void EquipWeapon(
        Agent agent,
        EquipmentIndex slot,
        ref MissionWeapon weapon)
    {
        agent.EquipWeaponWithNewEntity(slot, ref weapon);
    }
}
