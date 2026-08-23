using System;
using System.Collections.Generic;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace Druglord;

internal sealed class FirearmMissionLogic : MissionLogic
{
    private sealed class ExplosiveProjectileState
    {
        internal ExplosiveProjectileState(Agent shooterAgent)
        {
            ShooterAgent = shooterAgent;
            RemoveAfter = float.MaxValue;
        }

        internal Agent ShooterAgent { get; }
        internal float RemoveAfter { get; set; }
    }

    private const string SmokeParticleName = "psys_dummy_smoke";
    private const string ExplosionFireParticleName =
        "psys_game_burning_boulder_coll";
    private const string ExplosionDebrisParticleName =
        "psys_game_boulder_stone_coll";
    private const string ExplosionSoundEvent =
        "event:/mission/siege/generic/stone_destroy";
    private const float ExplosionRadius = 10f;
    private const float ExplosionCenterDamage = 300f;
    private const float ExplosionEdgeDamage = 50f;

    private int _smokeParticleId;
    private int _explosionFireParticleId;
    private int _explosionDebrisParticleId;
    private int _explosionSoundId;
    private bool _isSpawningAdditionalProjectiles;
    private readonly Dictionary<string, int> _rifleSoundIds =
        new Dictionary<string, int>(StringComparer.Ordinal);
    private readonly Dictionary<int, ExplosiveProjectileState>
        _explosiveProjectiles =
            new Dictionary<int, ExplosiveProjectileState>();
    private readonly List<int> _expiredProjectileIndices =
        new List<int>();
    private readonly List<Agent> _explosionTargets =
        new List<Agent>();

    public override void OnBehaviorInitialize()
    {
        base.OnBehaviorInitialize();
        MissionMissileLauncher.Initialize();

        _smokeParticleId = ParticleSystemManager.GetRuntimeIdByName(SmokeParticleName);
        _explosionFireParticleId =
            ParticleSystemManager.GetRuntimeIdByName(
                ExplosionFireParticleName);
        _explosionDebrisParticleId =
            ParticleSystemManager.GetRuntimeIdByName(
                ExplosionDebrisParticleName);
        _explosionSoundId =
            SoundEvent.GetEventIdFromString(ExplosionSoundEvent);

        if (_smokeParticleId < 0)
        {
            Debug.Print($"Druglord: particle system '{SmokeParticleName}' was not found.");
        }

        if (_explosionFireParticleId < 0)
        {
            Debug.Print(
                $"Druglord: particle system '{ExplosionFireParticleName}' " +
                "was not found.");
        }

        if (_explosionDebrisParticleId < 0)
        {
            Debug.Print(
                $"Druglord: particle system '{ExplosionDebrisParticleName}' " +
                "was not found.");
        }

        if (_explosionSoundId < 0)
        {
            Debug.Print(
                $"Druglord: sound event '{ExplosionSoundEvent}' was not found.");
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
        if (_isSpawningAdditionalProjectiles)
        {
            return;
        }

        if (!TryGetFirearm(
                shooterAgent,
                weaponIndex,
                out string itemId,
                out RifleSettings? settings) ||
            settings is null)
        {
            return;
        }

        TrackExplosiveProjectile(
            shooterAgent,
            position,
            settings);

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
        SpawnAdditionalProjectiles(
            shooterAgent,
            weaponIndex,
            position,
            velocity,
            orientation,
            hasRigidBody,
            settings);
    }

    public override void OnMissileHit(
        Agent attackerAgent,
        Agent victimAgent,
        bool isCanceled,
        AttackCollisionData collisionData)
    {
        int missileIndex =
            collisionData.AffectorWeaponSlotOrMissileIndex;
        if (!_explosiveProjectiles.TryGetValue(
                missileIndex,
                out ExplosiveProjectileState? projectileState) ||
            projectileState is null)
        {
            return;
        }

        _explosiveProjectiles.Remove(missileIndex);
        TriggerExplosion(
            projectileState.ShooterAgent,
            victimAgent,
            missileIndex,
            collisionData);
    }

    public override void OnMissileRemoved(int missileIndex)
    {
        if (_explosiveProjectiles.TryGetValue(
                missileIndex,
                out ExplosiveProjectileState? projectileState) &&
            projectileState is not null)
        {
            projectileState.RemoveAfter = Mission.CurrentTime + 0.1f;
        }

        base.OnMissileRemoved(missileIndex);
    }

    public override void OnMissionTick(float dt)
    {
        base.OnMissionTick(dt);

        _expiredProjectileIndices.Clear();
        foreach (KeyValuePair<int, ExplosiveProjectileState> projectile
                 in _explosiveProjectiles)
        {
            if (projectile.Value.RemoveAfter <= Mission.CurrentTime)
            {
                _expiredProjectileIndices.Add(projectile.Key);
            }
        }

        foreach (int missileIndex in _expiredProjectileIndices)
        {
            _explosiveProjectiles.Remove(missileIndex);
        }
    }

    protected override void OnEndMission()
    {
        _explosiveProjectiles.Clear();
        _expiredProjectileIndices.Clear();
        _explosionTargets.Clear();
        base.OnEndMission();
    }

    private void TrackExplosiveProjectile(
        Agent shooterAgent,
        Vec3 launchPosition,
        RifleSettings settings)
    {
        if (!settings.IsExplosive)
        {
            return;
        }

        Mission.Missile? closestMissile = null;
        float closestDistanceSquared = float.MaxValue;

        foreach (Mission.Missile missile in Mission.MissilesList)
        {
            if (_explosiveProjectiles.ContainsKey(missile.Index) ||
                !ReferenceEquals(missile.ShooterAgent, shooterAgent) ||
                missile.Weapon.IsEmpty ||
                !string.Equals(
                    missile.Weapon.Item.StringId,
                    settings.AmmunitionItemId,
                    StringComparison.Ordinal))
            {
                continue;
            }

            float distanceSquared =
                (missile.GetPosition() - launchPosition).LengthSquared;
            if (distanceSquared < closestDistanceSquared)
            {
                closestMissile = missile;
                closestDistanceSquared = distanceSquared;
            }
        }

        if (closestMissile is null)
        {
            throw new InvalidOperationException(
                $"Druglord could not track the explosive projectile " +
                $"launched by '{settings.ItemId}'.");
        }

        _explosiveProjectiles.Add(
            closestMissile.Index,
            new ExplosiveProjectileState(shooterAgent));
    }

    private void TriggerExplosion(
        Agent shooterAgent,
        Agent victimAgent,
        int missileIndex,
        AttackCollisionData collisionData)
    {
        Vec3 impactPosition = collisionData.CollisionGlobalPosition;
        MatrixFrame effectFrame =
            new MatrixFrame(Mat3.Identity, impactPosition);

        if (_explosionFireParticleId >= 0)
        {
            Mission.Scene.CreateBurstParticle(
                _explosionFireParticleId,
                effectFrame);
        }

        if (_explosionDebrisParticleId >= 0)
        {
            Mission.Scene.CreateBurstParticle(
                _explosionDebrisParticleId,
                effectFrame);
        }

        if (_explosionSoundId >= 0)
        {
            Mission.MakeSound(
                _explosionSoundId,
                impactPosition,
                soundCanBePredicted: false,
                isReliable: true,
                shooterAgent.Index,
                -1);
        }

        Mission.AddSoundAlarmFactorToAgents(
            shooterAgent,
            impactPosition,
            30f);

        _explosionTargets.Clear();
        foreach (Agent targetAgent in Mission.Agents)
        {
            if (!CanReceiveExplosionDamage(targetAgent) ||
                (targetAgent.CollisionCapsuleCenter - impactPosition)
                    .LengthSquared >= ExplosionRadius * ExplosionRadius)
            {
                continue;
            }

            _explosionTargets.Add(targetAgent);
        }

        if (victimAgent is not null &&
            CanReceiveExplosionDamage(victimAgent) &&
            !_explosionTargets.Contains(victimAgent))
        {
            _explosionTargets.Add(victimAgent);
        }

        foreach (Agent targetAgent in _explosionTargets)
        {
            ApplyExplosionDamage(
                shooterAgent,
                targetAgent,
                impactPosition);
        }

        Debug.Print(
            $"Druglord: explosive projectile {missileIndex} detonated " +
            $"and damaged {_explosionTargets.Count} agent(s).");
    }

    private static bool CanReceiveExplosionDamage(Agent targetAgent)
    {
        if (!targetAgent.IsActive() ||
            targetAgent.CurrentMortalityState ==
                Agent.MortalityState.Invulnerable)
        {
            return false;
        }

        return true;
    }

    private static void ApplyExplosionDamage(
        Agent shooterAgent,
        Agent targetAgent,
        Vec3 impactPosition)
    {
        Vec3 targetPosition = targetAgent.CollisionCapsuleCenter;
        Vec3 direction = targetPosition - impactPosition;
        float distance = direction.Normalize();
        if (distance <= 0.001f)
        {
            direction = Vec3.Up;
            distance = 0f;
        }

        float proximity = MathF.Clamp(
            1f - (distance / ExplosionRadius),
            0f,
            1f);
        int damage = MathF.Round(
            MathF.Lerp(
                ExplosionEdgeDamage,
                ExplosionCenterDamage,
                proximity * proximity));

        sbyte boneIndex = targetAgent.Monster.SpineLowerBoneIndex;
        if (boneIndex < 0)
        {
            boneIndex = targetAgent.Monster.HeadLookDirectionBoneIndex;
        }

        Blow blow = new Blow(shooterAgent.Index)
        {
            DamageType = DamageTypes.Blunt,
            BoneIndex = boneIndex,
            VictimBodyPart = BoneBodyPartType.Chest,
            GlobalPosition = targetPosition,
            BaseMagnitude = damage,
            InflictedDamage = damage,
            Direction = direction,
            SwingDirection = direction,
            DamageCalculated = true,
            DamagedPercentage = 1f,
            StrikeType = StrikeType.Thrust,
            BlowFlag = distance <= ExplosionRadius * 0.45f
                ? BlowFlags.KnockDown
                : BlowFlags.KnockBack
        };
        blow.WeaponRecord.FillAsMeleeBlow(null, null, -1, -1);

        AttackCollisionData damageCollisionData =
            AttackCollisionData.GetAttackCollisionDataForDebugPurpose(
                _attackBlockedWithShield: false,
                _correctSideShieldBlock: false,
                _isAlternativeAttack: false,
                _isColliderAgent: true,
                _collidedWithShieldOnBack: false,
                _isMissile: false,
                _isMissileBlockedWithWeapon: false,
                _missileHasPhysics: false,
                _entityExists: false,
                _thrustTipHit: false,
                _missileGoneUnderWater: false,
                _missileGoneOutOfBorder: false,
                CombatCollisionResult.StrikeAgent,
                -1,
                (int)StrikeType.Thrust,
                (int)DamageTypes.Blunt,
                boneIndex,
                BoneBodyPartType.Chest,
                shooterAgent.Monster.MainHandItemBoneIndex,
                Agent.UsageDirection.AttackLeft,
                -1,
                CombatHitResultFlags.NormalHit,
                0.5f,
                1f,
                0f,
                0f,
                0f,
                0f,
                0f,
                0f,
                Vec3.Up,
                direction,
                targetPosition,
                Vec3.Zero,
                Vec3.Zero,
                targetAgent.Velocity,
                Vec3.Up);

        targetAgent.RegisterBlow(
            blow,
            in damageCollisionData);
    }

    private void SpawnAdditionalProjectiles(
        Agent shooterAgent,
        EquipmentIndex weaponIndex,
        Vec3 position,
        Vec3 velocity,
        Mat3 orientation,
        bool hasRigidBody,
        RifleSettings settings)
    {
        if (settings.ProjectileCountPerShot <= 1)
        {
            return;
        }

        Vec3 centerDirection = velocity;
        float missileSpeed = centerDirection.Normalize();
        if (missileSpeed <= 0f)
        {
            throw new InvalidOperationException(
                $"Cannot spawn projectiles for '{settings.ItemId}' " +
                "with zero missile velocity.");
        }

        float spreadRadians =
            settings.MaximumSpreadDegrees.ToRadians();
        _isSpawningAdditionalProjectiles = true;
        try
        {
            for (int projectileIndex = 1;
                 projectileIndex < settings.ProjectileCountPerShot;
                 projectileIndex++)
            {
                Mat3 projectileOrientation = orientation;
                projectileOrientation.Orthonormalize();

                if (spreadRadians > 0f)
                {
                    float horizontalFactor;
                    float verticalFactor;
                    do
                    {
                        horizontalFactor =
                            MBRandom.RandomFloatRanged(-1f, 1f);
                        verticalFactor =
                            MBRandom.RandomFloatRanged(-1f, 1f);
                    }
                    while (horizontalFactor * horizontalFactor +
                           verticalFactor * verticalFactor > 1f);

                    projectileOrientation.RotateAboutUp(
                        horizontalFactor * spreadRadians);
                    projectileOrientation.RotateAboutSide(
                        verticalFactor * spreadRadians);
                }

                Vec3 projectileVelocity =
                    projectileOrientation.f * missileSpeed;
                MissionMissileLauncher.Shoot(
                    Mission,
                    shooterAgent,
                    weaponIndex,
                    position,
                    projectileVelocity,
                    projectileOrientation,
                    hasRigidBody,
                    isPrimaryWeaponShot: false,
                    forcedMissileIndex: -1);
            }
        }
        finally
        {
            _isSpawningAdditionalProjectiles = false;
        }
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
        out string itemId,
        out RifleSettings? settings)
    {
        itemId = string.Empty;
        settings = null;

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
            out settings);
    }
}
