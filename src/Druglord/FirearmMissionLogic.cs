using System;
using System.Collections.Generic;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace Druglord;

internal sealed class FirearmMissionLogic : MissionLogic
{
    private const string SmokeParticleName = "psys_dummy_smoke";

    private int _smokeParticleId;
    private readonly Dictionary<string, int> _rifleSoundIds =
        new Dictionary<string, int>(StringComparer.Ordinal);

    public override void OnBehaviorInitialize()
    {
        base.OnBehaviorInitialize();

        _smokeParticleId = ParticleSystemManager.GetRuntimeIdByName(SmokeParticleName);

        if (_smokeParticleId < 0)
        {
            Debug.Print($"Druglord: particle system '{SmokeParticleName}' was not found.");
        }
    }

    public override void OnAgentShootMissile(
        Agent shooterAgent,
        EquipmentIndex weaponIndex,
        Vec3 position,
        Vec3 velocity,
        Mat3 orientation,
        bool hasRigidBody,
        int forcedMissileIndex)
    {
        if (!TryGetFirearm(
                shooterAgent,
                weaponIndex,
                out string itemId))
        {
            return;
        }

        MatrixFrame effectFrame = new MatrixFrame(orientation, position);
        effectFrame.rotation.Orthonormalize();

        if (_smokeParticleId >= 0)
        {
            Mission.Scene.CreateBurstParticle(_smokeParticleId, effectFrame);
        }

        int soundId = GetSoundId(itemId);
        if (soundId >= 0)
        {
            Mission.MakeSound(
                soundId,
                position,
                soundCanBePredicted: false,
                isReliable: true,
                shooterAgent.Index,
                -1);
        }

        Mission.AddSoundAlarmFactorToAgents(shooterAgent, position, 18f);
    }

    private int GetSoundId(string itemId)
    {
        if (_rifleSoundIds.TryGetValue(itemId, out int soundId))
        {
            return soundId;
        }

        if (!RifleSettingsRegistry.TryGet(
                Game.Current,
                itemId,
                out RifleSettings? settings) ||
            settings is null)
        {
            Debug.Print(
                $"Druglord: rifle '{itemId}' has no configured " +
                "sound event.");
            _rifleSoundIds.Add(itemId, -1);
            return -1;
        }

        soundId = SoundEvent.GetEventIdFromString(settings.SoundEvent);
        _rifleSoundIds.Add(itemId, soundId);
        if (soundId < 0)
        {
            Debug.Print(
                $"Druglord: sound event '{settings.SoundEvent}' for " +
                $"'{itemId}' was not found.");
            return -1;
        }

        Debug.Print(
            $"Druglord: sound event '{settings.SoundEvent}' loaded for " +
            $"'{itemId}'.");
        return soundId;
    }

    private static bool TryGetFirearm(
        Agent shooterAgent,
        EquipmentIndex weaponIndex,
        out string itemId)
    {
        itemId = string.Empty;

        if (shooterAgent is null || weaponIndex == EquipmentIndex.None)
        {
            return false;
        }

        MissionWeapon weapon = shooterAgent.Equipment[weaponIndex];
        WeaponComponentData? usage = weapon.CurrentUsageItem;
        if (usage is null)
        {
            return false;
        }

        if (usage.WeaponClass != WeaponClass.Musket)
        {
            return false;
        }

        itemId = weapon.Item.StringId;
        return RifleSettingsRegistry.TryGet(
            Game.Current,
            itemId,
            out _);
    }
}
