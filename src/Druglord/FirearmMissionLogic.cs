using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace Druglord;

internal sealed class FirearmMissionLogic : MissionLogic
{
    private const string SmokeParticleName = "psys_dummy_smoke";
    private const string HandgunSoundEvent = "event:/mission/siege/ballista/fire";
    private const string RifleSoundEvent = "event:/mission/siege/burning_ballista/shot";

    private int _smokeParticleId;
    private int _handgunSoundId;
    private int _rifleSoundId;

    public override void OnBehaviorInitialize()
    {
        base.OnBehaviorInitialize();

        _smokeParticleId = ParticleSystemManager.GetRuntimeIdByName(SmokeParticleName);
        _handgunSoundId = SoundEvent.GetEventIdFromString(HandgunSoundEvent);
        _rifleSoundId = SoundEvent.GetEventIdFromString(RifleSoundEvent);

        if (_smokeParticleId < 0)
        {
            Debug.Print($"Druglord: particle system '{SmokeParticleName}' was not found.");
        }

        if (_handgunSoundId < 0 || _rifleSoundId < 0)
        {
            Debug.Print("Druglord: one or more placeholder firearm sounds were not found.");
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
        if (!TryGetFirearmClass(shooterAgent, weaponIndex, out WeaponClass weaponClass))
        {
            return;
        }

        MatrixFrame effectFrame = new MatrixFrame(orientation, position);
        effectFrame.rotation.Orthonormalize();

        if (_smokeParticleId >= 0)
        {
            Mission.Scene.CreateBurstParticle(_smokeParticleId, effectFrame);
        }

        int soundId = weaponClass == WeaponClass.Musket
            ? _rifleSoundId
            : _handgunSoundId;

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

        float alarmLevel = weaponClass == WeaponClass.Musket ? 18f : 14f;
        Mission.AddSoundAlarmFactorToAgents(shooterAgent, position, alarmLevel);
    }

    private static bool TryGetFirearmClass(
        Agent shooterAgent,
        EquipmentIndex weaponIndex,
        out WeaponClass weaponClass)
    {
        weaponClass = WeaponClass.Undefined;

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

        weaponClass = usage.WeaponClass;
        return weaponClass == WeaponClass.Pistol || weaponClass == WeaponClass.Musket;
    }
}
