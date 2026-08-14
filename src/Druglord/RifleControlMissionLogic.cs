using System;
using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.Screens;

namespace Druglord;

internal sealed class RifleControlMissionLogic : MissionLogic
{
    private enum WeaponState
    {
        Lowered,
        Raising,
        Ready,
        Firing,
        Reloading
    }

    private delegate void ShootMissileDelegate(
        Mission mission,
        Agent shooterAgent,
        EquipmentIndex weaponIndex,
        Vec3 position,
        Vec3 velocity,
        Mat3 orientation,
        bool hasRigidBody,
        bool isPrimaryWeaponShot,
        int forcedMissileIndex);

    private ShootMissileDelegate? _shootMissile;
    private ActionIndexCache _readyAction;
    private ActionIndexCache _readyContinueAction;
    private ActionIndexCache _releaseAction;
    private ActionIndexCache _reloadAction;
    private WeaponState _weaponState;
    private bool _aimHeld;
    private bool _triggerHeld;
    private bool _shotQueued;
    private bool _outOfAmmoNotified;
    private bool _wasRifleWielded;
    private float _raiseCompletionTime;
    private float _firingCompletionTime;
    private float _nextShotTime;
    private float _reloadCompletionTime;
    private MissionScreen? _missionScreen;
    private RifleSettings? _activeSettings;
    private string _activeRifleName = "Rifle";
    private int _consecutiveShotCount;
    private float _lastShotTime = float.MinValue;

    private RifleSettings Settings =>
        _activeSettings ??
        throw new InvalidOperationException(
            "Druglord rifle settings are unavailable.");

    public RifleControlMissionLogic()
    {
        _weaponState = WeaponState.Lowered;
    }

    public override void OnBehaviorInitialize()
    {
        base.OnBehaviorInitialize();
        RifleSettingsRegistry.EnsureLoaded(Game.Current);

        System.Reflection.MethodInfo shootMethod = AccessTools.Method(
            typeof(Mission),
            "OnAgentShootMissile",
            new[]
            {
                typeof(Agent),
                typeof(EquipmentIndex),
                typeof(Vec3),
                typeof(Vec3),
                typeof(Mat3),
                typeof(bool),
                typeof(bool),
                typeof(int)
            }) ?? throw new MissingMethodException(
                typeof(Mission).FullName,
                "OnAgentShootMissile");

        _shootMissile = (ShootMissileDelegate)shootMethod.CreateDelegate(
            typeof(ShootMissileDelegate));
        _readyAction = ActionIndexCache.Create("act_ready_crossbow");
        _readyContinueAction =
            ActionIndexCache.Create("act_ready_continue_crossbow");
        _releaseAction = ActionIndexCache.Create("act_release_crossbow");
        _reloadAction = ActionIndexCache.Create("act_reload_crossbow_light");

        Debug.Print("Druglord: Harmony rifle controller initialized.");
    }

    internal void HandleMainAgentInput(
        IInputContext input,
        MissionScreen missionScreen)
    {
        Agent? mainAgent = Mission.MainAgent;
        if (mainAgent is null ||
            !mainAgent.IsActive() ||
            !TryGetWieldedRifle(
                mainAgent,
                out EquipmentIndex rifleSlot,
                out RifleSettings? settings) ||
            settings is null)
        {
            OnRifleNoLongerWielded();
            return;
        }

        if (!ReferenceEquals(_activeSettings, settings))
        {
            OnRifleNoLongerWielded();
            _activeSettings = settings;
            _activeRifleName =
                mainAgent.Equipment[rifleSlot].Item.Name.ToString();
        }

        if (!_wasRifleWielded)
        {
            _wasRifleWielded = true;
            Debug.Print(
                $"Druglord: Harmony rifle input hook is active for " +
                $"'{settings.ItemId}'.");
            InformationManager.DisplayMessage(
                new InformationMessage(
                    $"{_activeRifleName}: {GetFireModeLabel(settings)} | " +
                    "RMB readies weapon."));
        }

        SuppressNativeRifleControls(mainAgent);

        _missionScreen = missionScreen;
        _aimHeld =
            input.IsKeyDown(InputKey.RightMouseButton) ||
            input.IsGameKeyDown(10);
        _triggerHeld = IsAttackButtonDown(input);
        bool triggerPressed = IsAttackButtonPressed(input);
        bool automaticTriggerHeld =
            settings.FireMode == RifleFireMode.Automatic &&
            _triggerHeld;
        bool triggerRequested =
            triggerPressed || automaticTriggerHeld;

        if (Mission.CurrentTime - _lastShotTime >
            settings.RecoilResetDelay)
        {
            _consecutiveShotCount = 0;
        }

        if (_weaponState == WeaponState.Reloading)
        {
            if (Mission.CurrentTime >= _reloadCompletionTime)
            {
                CompleteReload(mainAgent, rifleSlot);
            }

            return;
        }

        MissionWeapon rifle = mainAgent.Equipment[rifleSlot];
        if (_weaponState != WeaponState.Firing && rifle.Ammo <= 0)
        {
            BeginReload(mainAgent);
            return;
        }

        switch (_weaponState)
        {
        case WeaponState.Lowered:
            if (_aimHeld ||
                triggerRequested)
            {
                _shotQueued = triggerRequested;
                BeginRaise(mainAgent);
            }
            break;

        case WeaponState.Raising:
            if (triggerRequested)
            {
                _shotQueued = true;
            }

            if (!_aimHeld && !automaticTriggerHeld && !_shotQueued)
            {
                LowerRifle(mainAgent);
            }
            else if (IsRaiseComplete(mainAgent))
            {
                EnterReady(mainAgent);
                TryFireQueuedShot(mainAgent, rifleSlot);
            }
            break;

        case WeaponState.Ready:
            MaintainReadyPose(mainAgent);

            if ((_shotQueued || triggerRequested) &&
                Mission.CurrentTime >= _nextShotTime)
            {
                FireRifle(mainAgent, rifleSlot);
            }
            else if (!_aimHeld &&
                     !automaticTriggerHeld &&
                     !_shotQueued)
            {
                LowerRifle(mainAgent);
            }
            break;

        case WeaponState.Firing:
            if (triggerPressed)
            {
                _shotQueued = true;
            }

            if (Mission.CurrentTime >= _firingCompletionTime)
            {
                rifle = mainAgent.Equipment[rifleSlot];
                if (rifle.Ammo <= 0)
                {
                    BeginReload(mainAgent);
                }
                else if (_aimHeld ||
                         automaticTriggerHeld ||
                         _shotQueued)
                {
                    EnterReady(mainAgent);
                    TryFireQueuedShot(mainAgent, rifleSlot);
                }
                else
                {
                    LowerRifle(mainAgent);
                }
            }
            break;
        }
    }

    protected override void OnEndMission()
    {
        if (Mission.MainAgent is { } mainAgent)
        {
            LowerRifle(mainAgent);
        }

        base.OnEndMission();
    }

    private void BeginRaise(Agent agent)
    {
        _weaponState = WeaponState.Raising;
        _raiseCompletionTime =
            Mission.CurrentTime + Settings.RaiseDuration;

        agent.SetActionChannel(
            1,
            _readyAction,
            ignorePriority: true,
            actionSpeed: 1f,
            blendInPeriod: 0.08f,
            blendOutPeriod: 0.08f);

        Debug.Print($"Druglord: {_activeRifleName} raising.");
    }

    private bool IsRaiseComplete(Agent agent)
    {
        ActionIndexCache currentAction = agent.GetCurrentAction(1);
        if (currentAction == _readyContinueAction)
        {
            return true;
        }

        if (currentAction == _readyAction &&
            agent.GetCurrentActionProgress(1) >= 0.9f)
        {
            return true;
        }

        return Mission.CurrentTime >= _raiseCompletionTime;
    }

    private void EnterReady(Agent agent)
    {
        _weaponState = WeaponState.Ready;
        agent.SetActionChannel(
            1,
            _readyContinueAction,
            ignorePriority: true,
            actionSpeed: 1f,
            blendInPeriod: 0.04f,
            blendOutPeriod: 0.08f);
    }

    private void MaintainReadyPose(Agent agent)
    {
        ActionIndexCache currentAction = agent.GetCurrentAction(1);
        if (currentAction != _readyAction &&
            currentAction != _readyContinueAction)
        {
            EnterReady(agent);
        }
    }

    private void TryFireQueuedShot(
        Agent agent,
        EquipmentIndex rifleSlot)
    {
        bool shouldFire =
            _shotQueued ||
            (Settings.FireMode == RifleFireMode.Automatic &&
             _triggerHeld);

        if (shouldFire &&
            Mission.CurrentTime >= _nextShotTime)
        {
            FireRifle(agent, rifleSlot);
        }
    }

    private void FireRifle(
        Agent agent,
        EquipmentIndex rifleSlot)
    {
        RifleSettings settings = Settings;
        MissionWeapon rifle = agent.Equipment[rifleSlot];
        if (rifle.Ammo <= 0 || rifle.AmmoWeapon.IsEmpty)
        {
            BeginReload(agent);
            return;
        }

        Vec3 position = GetMuzzlePosition(
            agent,
            rifleSlot,
            rifle.Item.MultiMeshName,
            settings);
        Vec3 direction = GetConstrainedAimDirection(agent);

        int recoilShotCount = UpdateRecoilShotCount();
        float recoilIntensity = GetRecoilIntensity(recoilShotCount);
        float spreadDegrees = MathF.Lerp(
            settings.MinimumSpreadDegrees,
            settings.MaximumSpreadDegrees,
            recoilIntensity);
        float horizontalSpread =
            MBRandom.RandomFloatRanged(-spreadDegrees, spreadDegrees)
                .ToRadians();
        float verticalSpread =
            MBRandom.RandomFloatRanged(-spreadDegrees, spreadDegrees)
                .ToRadians();

        Mat3 orientation = Mat3.Identity;
        orientation.f = direction;
        orientation.u = Vec3.Up;
        orientation.Orthonormalize();
        orientation.RotateAboutUp(horizontalSpread);
        orientation.RotateAboutSide(verticalSpread);
        direction = orientation.f;
        position += direction * 0.7f;

        float missileSpeed =
            rifle.GetModifiedMissileSpeedForCurrentUsage();
        Vec3 velocity = direction * missileSpeed;

        (_shootMissile ?? throw new InvalidOperationException(
            "Druglord rifle shooting was not initialized."))(
            Mission,
            agent,
            rifleSlot,
            position,
            velocity,
            orientation,
            false,
            true,
            -1);

        short remainingAmmo = (short)(rifle.Ammo - 1);
        agent.Equipment.SetConsumedAmmoOfSlot(
            rifleSlot,
            remainingAmmo);

        agent.SetActionChannel(
            1,
            _releaseAction,
            ignorePriority: true,
            actionSpeed: 2.5f,
            blendInPeriod: 0.02f,
            blendOutPeriod: 0.08f);

        _weaponState = WeaponState.Firing;
        _shotQueued = false;
        _firingCompletionTime =
            Mission.CurrentTime + settings.RecoilDuration;
        _nextShotTime =
            Mission.CurrentTime + settings.ShotInterval;
        _outOfAmmoNotified = false;
        ApplyCameraRecoil(recoilIntensity);

        Debug.Print(
            $"Druglord: {_activeRifleName} fired; magazine " +
            $"{remainingAmmo}/{settings.MagazineSize}; recoil " +
            $"{recoilShotCount}/{settings.PeakRecoilShotCount}.");
    }

    private void BeginReload(Agent agent)
    {
        if (_weaponState == WeaponState.Reloading)
        {
            return;
        }

        if (!TryFindAmmunitionSlot(agent, out _))
        {
            if (!_outOfAmmoNotified)
            {
                _outOfAmmoNotified = true;
                InformationManager.DisplayMessage(
                    new InformationMessage(
                        $"{_activeRifleName}: out of ammunition."));
            }

            LowerRifle(agent);
            return;
        }

        _weaponState = WeaponState.Reloading;
        _shotQueued = false;
        _reloadCompletionTime =
            Mission.CurrentTime + Settings.ReloadDuration;

        agent.SetActionChannel(
            1,
            _reloadAction,
            ignorePriority: true,
            actionSpeed: 1f,
            blendInPeriod: 0.05f,
            blendOutPeriod: 0.15f);

        InformationManager.DisplayMessage(
            new InformationMessage(
                $"{_activeRifleName} reloading..."));
        Debug.Print(
            $"Druglord: {_activeRifleName} reload started.");
    }

    private void CompleteReload(
        Agent agent,
        EquipmentIndex rifleSlot)
    {
        RifleSettings settings = Settings;
        if (!TryFindAmmunitionSlot(
                agent,
                out EquipmentIndex ammunitionSlot))
        {
            _outOfAmmoNotified = true;
            InformationManager.DisplayMessage(
                new InformationMessage(
                    $"{_activeRifleName}: out of ammunition."));
            LowerRifle(agent);
            return;
        }

        MissionWeapon reserve = agent.Equipment[ammunitionSlot];
        short roundsToLoad = (short)Math.Min(
            settings.MagazineSize,
            reserve.Amount);
        MissionWeapon loadedRounds = reserve.Consume(roundsToLoad);

        MissionWeapon rifle = agent.Equipment[rifleSlot];
        rifle.ReloadAmmo(
            loadedRounds,
            rifle.ReloadPhaseCount);

        agent.EquipWeaponWithNewEntity(rifleSlot, ref rifle);
        agent.EquipWeaponWithNewEntity(
            ammunitionSlot,
            ref reserve);
        agent.TryToWieldWeaponInSlot(
            rifleSlot,
            Agent.WeaponWieldActionType.InstantAfterPickUp,
            false);

        _weaponState = WeaponState.Lowered;
        _nextShotTime =
            Mission.CurrentTime + settings.ShotInterval;
        _outOfAmmoNotified = false;

        InformationManager.DisplayMessage(
            new InformationMessage(
                $"{_activeRifleName} reloaded: " +
                $"{roundsToLoad}/{settings.MagazineSize}"));
        Debug.Print(
            $"Druglord: {_activeRifleName} reloaded with " +
            $"{roundsToLoad} rounds.");

        if (_aimHeld || _triggerHeld)
        {
            _shotQueued =
                settings.FireMode == RifleFireMode.Automatic &&
                _triggerHeld;
            BeginRaise(agent);
        }
    }

    private void LowerRifle(Agent agent)
    {
        _weaponState = WeaponState.Lowered;
        _shotQueued = false;
        agent.SetActionChannel(
            1,
            ActionIndexCache.act_none,
            ignorePriority: true,
            blendInPeriod: 0.08f,
            blendOutPeriodToNoAnim: 0.2f,
            blendOutPeriod: 0.1f);
    }

    private void SuppressNativeRifleControls(Agent agent)
    {
        agent.MovementFlags &=
            ~(Agent.MovementControlFlag.AttackMask |
              Agent.MovementControlFlag.DefendMask);
        agent.EventControlFlags &=
            ~Agent.EventControlFlag.ToggleAlternativeWeapon;
    }

    private void OnRifleNoLongerWielded()
    {
        _weaponState = WeaponState.Lowered;
        _aimHeld = false;
        _triggerHeld = false;
        _shotQueued = false;
        _wasRifleWielded = false;
        _activeSettings = null;
        _activeRifleName = "Rifle";
        _consecutiveShotCount = 0;
        _missionScreen = null;
    }

    private static bool TryGetWieldedRifle(
        Agent agent,
        out EquipmentIndex rifleSlot,
        out RifleSettings? settings)
    {
        settings = null;
        rifleSlot = agent.GetPrimaryWieldedItemIndex();
        if (rifleSlot == EquipmentIndex.None)
        {
            return false;
        }

        MissionWeapon weapon = agent.Equipment[rifleSlot];
        return !weapon.IsEmpty &&
               RifleSettingsRegistry.TryGet(
                   Game.Current,
                   weapon.Item.StringId,
                   out settings);
    }

    private bool TryFindAmmunitionSlot(
        Agent agent,
        out EquipmentIndex ammunitionSlot)
    {
        for (EquipmentIndex slot = EquipmentIndex.WeaponItemBeginSlot;
             slot < EquipmentIndex.NumAllWeaponSlots;
             slot++)
        {
            MissionWeapon weapon = agent.Equipment[slot];
            if (!weapon.IsEmpty &&
                weapon.Item.StringId ==
                    Settings.AmmunitionItemId &&
                weapon.Amount > 0)
            {
                ammunitionSlot = slot;
                return true;
            }
        }

        ammunitionSlot = EquipmentIndex.None;
        return false;
    }

    private static bool IsAttackButtonDown(IInputContext input)
    {
        return input.IsKeyDown(InputKey.LeftMouseButton) ||
               input.IsGameKeyDown(9);
    }

    private static bool IsAttackButtonPressed(IInputContext input)
    {
        return input.IsKeyPressed(InputKey.LeftMouseButton) ||
               input.IsGameKeyPressed(9);
    }

    private static Vec3 GetMuzzlePosition(
        Agent agent,
        EquipmentIndex rifleSlot,
        string metaMeshName,
        RifleSettings settings)
    {
        WeakGameEntity visualRoot =
            agent.GetWeaponEntityFromEquipmentSlot(rifleSlot);
        if (!visualRoot.IsValid)
        {
            throw new InvalidOperationException(
                "Druglord could not resolve the rifle visual root.");
        }

        MatrixFrame rootFrame = visualRoot.GetGlobalFrame();
        for (int index = 0;
             index < visualRoot.MultiMeshComponentCount;
             index++)
        {
            MetaMesh metaMesh = visualRoot.GetMetaMesh(index);
            if (!string.Equals(
                    metaMesh.GetName(),
                    metaMeshName,
                    StringComparison.Ordinal))
            {
                continue;
            }

            MatrixFrame metaMeshFrame = metaMesh.Frame;
            for (int meshIndex = 0;
                 meshIndex < metaMesh.MeshCount;
                 meshIndex++)
            {
                Mesh mesh = metaMesh.GetMeshAtIndex(meshIndex);
                if (mesh.Name.IndexOf(
                        ".lod",
                        StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    continue;
                }

                Material material = mesh.GetMaterial();
                if (material is null ||
                    !string.Equals(
                        material.Name,
                        settings.MuzzleMeshMaterial,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                BoundingBox bounds = new BoundingBox(
                    mesh.GetBoundingBoxMin());
                bounds.max = mesh.GetBoundingBoxMax();
                bounds.center = (bounds.min + bounds.max) * 0.5f;

                Vec3 localMuzzlePosition =
                    GetFaceCenter(bounds, settings.MuzzleFace) +
                    settings.MuzzleOffset;
                MatrixFrame meshFrame = mesh.GetLocalFrame();
                return TransformWeaponPoint(
                    rootFrame,
                    metaMeshFrame,
                    meshFrame,
                    localMuzzlePosition);
            }

            throw new InvalidOperationException(
                $"Druglord could not find muzzle material " +
                $"'{settings.MuzzleMeshMaterial}' on rifle mesh " +
                $"'{metaMeshName}'.");
        }

        throw new InvalidOperationException(
            $"Druglord could not find rifle mesh '{metaMeshName}' " +
            "on the agent visual root.");
    }

    private static Vec3 GetConstrainedAimDirection(Agent agent)
    {
        Vec3 aimDirection = agent.LookDirection;
        aimDirection.Normalize();

        if (!agent.HasMount)
        {
            return aimDirection;
        }

        Vec2 bodyRotationConstraint =
            agent.GetBodyRotationConstraint(1);
        bool constraintIsActive =
            bodyRotationConstraint.x < -0.1f ||
            bodyRotationConstraint.y > 0.1f;
        if (!constraintIsActive)
        {
            return aimDirection;
        }

        Vec2 movementDirection = agent.GetMovementDirection();
        Vec2 horizontalAim = aimDirection.AsVec2;
        if (!movementDirection.IsNonZero() ||
            !horizontalAim.IsNonZero())
        {
            return aimDirection;
        }

        float movementAngle = movementDirection.RotationInRadians;
        float relativeAimAngle = MBMath.WrapAngle(
            horizontalAim.RotationInRadians - movementAngle);
        if (MBMath.IsBetween(
                relativeAimAngle,
                bodyRotationConstraint.x,
                bodyRotationConstraint.y))
        {
            return aimDirection;
        }

        float distanceToMinimum =
            TaleWorlds.Library.MathF.Abs(
                MBMath.WrapAngle(
                    relativeAimAngle -
                    bodyRotationConstraint.x));
        float distanceToMaximum =
            TaleWorlds.Library.MathF.Abs(
                MBMath.WrapAngle(
                    relativeAimAngle -
                    bodyRotationConstraint.y));
        float constrainedRelativeAngle =
            distanceToMinimum <= distanceToMaximum
                ? bodyRotationConstraint.x
                : bodyRotationConstraint.y;
        Vec2 constrainedHorizontal = Vec2.FromRotation(
            movementAngle + constrainedRelativeAngle);
        constrainedHorizontal *= horizontalAim.Length;

        Vec3 constrainedAim = new Vec3(
            constrainedHorizontal.x,
            constrainedHorizontal.y,
            aimDirection.z);
        constrainedAim.Normalize();
        return constrainedAim;
    }

    private static Vec3 TransformWeaponPoint(
        MatrixFrame rootFrame,
        MatrixFrame metaMeshFrame,
        MatrixFrame meshFrame,
        Vec3 point)
    {
        Vec3 meshPosition = meshFrame.TransformToParent(point);
        Vec3 metaMeshPosition =
            metaMeshFrame.TransformToParent(meshPosition);
        return rootFrame.TransformToParent(metaMeshPosition);
    }

    private static Vec3 GetFaceCenter(
        BoundingBox bounds,
        RifleMuzzleFace face)
    {
        switch (face)
        {
        case RifleMuzzleFace.MinX:
            return new Vec3(
                bounds.min.x,
                bounds.center.y,
                bounds.center.z);
        case RifleMuzzleFace.MaxX:
            return new Vec3(
                bounds.max.x,
                bounds.center.y,
                bounds.center.z);
        case RifleMuzzleFace.MinY:
            return new Vec3(
                bounds.center.x,
                bounds.min.y,
                bounds.center.z);
        case RifleMuzzleFace.MaxY:
            return new Vec3(
                bounds.center.x,
                bounds.max.y,
                bounds.center.z);
        case RifleMuzzleFace.MinZ:
            return new Vec3(
                bounds.center.x,
                bounds.center.y,
                bounds.min.z);
        case RifleMuzzleFace.MaxZ:
            return new Vec3(
                bounds.center.x,
                bounds.center.y,
                bounds.max.z);
        default:
            throw new ArgumentOutOfRangeException(
                nameof(face),
                face,
                "Unknown rifle muzzle face.");
        }
    }


    private int UpdateRecoilShotCount()
    {
        if (Mission.CurrentTime - _lastShotTime >
            Settings.RecoilResetDelay)
        {
            _consecutiveShotCount = 0;
        }

        _lastShotTime = Mission.CurrentTime;
        _consecutiveShotCount = Math.Min(
            _consecutiveShotCount + 1,
            Settings.PeakRecoilShotCount);
        return _consecutiveShotCount;
    }

    private float GetRecoilIntensity(int shotCount)
    {
        return (float)(shotCount - 1) /
               (Settings.PeakRecoilShotCount - 1);
    }

    private void ApplyCameraRecoil(float recoilIntensity)
    {
        if (_missionScreen is null)
        {
            return;
        }

        float verticalKick = MathF.Lerp(
            Settings.MinimumVerticalKickDegrees,
            Settings.MaximumVerticalKickDegrees,
            recoilIntensity).ToRadians();
        float horizontalKick =
            MBRandom.RandomFloatRanged(
                -Settings.MaximumHorizontalKickDegrees,
                Settings.MaximumHorizontalKickDegrees) *
            recoilIntensity;

        HarmonyPatches.ApplyCameraRecoil(
            _missionScreen,
            verticalKick,
            horizontalKick.ToRadians());
    }

    private static string GetFireModeLabel(RifleSettings settings)
    {
        return settings.FireMode == RifleFireMode.Automatic
            ? "AUTO"
            : "SEMI";
    }
}
