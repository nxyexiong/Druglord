using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace Druglord;

internal sealed class AutomaticRifleAiMissionLogic : MissionLogic
{
    private readonly struct AiInputSignal
    {
        internal AiInputSignal(
            Agent agent,
            bool hasAutomaticRifle,
            bool attackRequested)
        {
            Agent = agent;
            HasAutomaticRifle = hasAutomaticRifle;
            AttackRequested = attackRequested;
        }

        internal Agent Agent { get; }
        internal bool HasAutomaticRifle { get; }
        internal bool AttackRequested { get; }
    }

    private enum AutomaticRifleState
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

    private sealed class AutomaticRifleAiComponent : AgentComponent
    {
        private readonly AutomaticRifleAiMissionLogic _logic;

        internal AutomaticRifleAiComponent(
            Agent agent,
            AutomaticRifleAiMissionLogic logic)
            : base(agent)
        {
            _logic = logic;
        }

        public override void OnAIInputSet(
            ref Agent.EventControlFlag eventFlag,
            ref Agent.MovementControlFlag movementFlag,
            ref Vec2 inputVector)
        {
            if (_logic.QueueAiInput(Agent, movementFlag))
            {
                movementFlag &=
                    ~Agent.MovementControlFlag.AttackMask;
            }
        }

        public override void OnComponentRemoved()
        {
            _logic.QueueRemoval(Agent);
        }
    }

    private sealed class AutomaticFireState
    {
        internal AutomaticFireState(
            RifleSettings settings,
            EquipmentIndex rifleSlot)
        {
            Settings = settings;
            RifleSlot = rifleSlot;
            State = AutomaticRifleState.Lowered;
            LastShotTime = float.MinValue;
        }

        internal RifleSettings Settings { get; set; }
        internal EquipmentIndex RifleSlot { get; set; }
        internal AutomaticRifleState State { get; set; }
        internal float RaiseCompletionTime { get; set; }
        internal float FiringCompletionTime { get; set; }
        internal float NextShotTime { get; set; }
        internal float ReloadCompletionTime { get; set; }
        internal int ConsecutiveShotCount { get; set; }
        internal float LastShotTime { get; set; }
        internal int AutomaticShotCount { get; set; }
    }

    private readonly Dictionary<Agent, AutomaticFireState> _states =
        new Dictionary<Agent, AutomaticFireState>();
    private readonly ConcurrentQueue<AiInputSignal> _inputSignals =
        new ConcurrentQueue<AiInputSignal>();
    private readonly List<KeyValuePair<Agent, AutomaticFireState>>
        _tickBuffer =
            new List<KeyValuePair<Agent, AutomaticFireState>>();
    private readonly HashSet<Agent> _outOfAmmoAgents =
        new HashSet<Agent>();
    private readonly List<Agent> _removalBuffer =
        new List<Agent>();

    private ShootMissileDelegate? _shootMissile;
    private ActionIndexCache _readyAction;
    private ActionIndexCache _releaseAction;
    private ActionIndexCache _reloadAction;

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
        _readyAction =
            ActionIndexCache.Create("act_ready_continue_crossbow");
        _releaseAction = ActionIndexCache.Create("act_release_crossbow");
        _reloadAction =
            ActionIndexCache.Create("act_reload_crossbow_light");

        Debug.Print("Druglord: automatic-rifle AI controller initialized.");
    }

    public override void OnMissionTick(float dt)
    {
        base.OnMissionTick(dt);
        EnsureAiInputHooks();
        ProcessAiInputSignals();

        if (_states.Count == 0)
        {
            return;
        }

        _removalBuffer.Clear();
        _tickBuffer.Clear();
        foreach (KeyValuePair<Agent, AutomaticFireState> entry in _states)
        {
            _tickBuffer.Add(entry);
        }

        foreach (KeyValuePair<Agent, AutomaticFireState> entry
                 in _tickBuffer)
        {
            if (!_states.TryGetValue(
                    entry.Key,
                    out AutomaticFireState currentState) ||
                !ReferenceEquals(currentState, entry.Value))
            {
                continue;
            }

            if (!TickAgent(entry.Key, entry.Value))
            {
                _removalBuffer.Add(entry.Key);
            }
        }

        foreach (Agent agent in _removalBuffer)
        {
            _states.Remove(agent);
        }
    }

    public override void OnAgentRemoved(
        Agent affectedAgent,
        Agent affectorAgent,
        AgentState agentState,
        KillingBlow blow)
    {
        RemoveState(affectedAgent);
        _outOfAmmoAgents.Remove(affectedAgent);
        base.OnAgentRemoved(
            affectedAgent,
            affectorAgent,
            agentState,
            blow);
    }

    public override void OnAgentDeleted(Agent affectedAgent)
    {
        RemoveState(affectedAgent);
        _outOfAmmoAgents.Remove(affectedAgent);
        base.OnAgentDeleted(affectedAgent);
    }

    protected override void OnEndMission()
    {
        _states.Clear();
        _outOfAmmoAgents.Clear();
        while (_inputSignals.TryDequeue(out _))
        {
        }

        base.OnEndMission();
    }

    private bool QueueAiInput(
        Agent agent,
        Agent.MovementControlFlag movementFlag)
    {
        bool hasAutomaticRifle =
            agent.IsActive() &&
            agent.IsAIControlled &&
            !ReferenceEquals(agent, Mission.MainAgent) &&
            TryGetWieldedAutomaticRifle(
                agent,
                out _,
                out _);

        bool attackRequested =
            (movementFlag & Agent.MovementControlFlag.AttackMask) != 0;
        _inputSignals.Enqueue(
            new AiInputSignal(
                agent,
                hasAutomaticRifle,
                attackRequested));
        return hasAutomaticRifle;
    }

    private void QueueRemoval(Agent agent)
    {
        _inputSignals.Enqueue(
            new AiInputSignal(
                agent,
                hasAutomaticRifle: false,
                attackRequested: false));
    }

    private void ProcessAiInputSignals()
    {
        while (_inputSignals.TryDequeue(out AiInputSignal signal))
        {
            if (!signal.HasAutomaticRifle)
            {
                _states.Remove(signal.Agent);
                continue;
            }

            if (!signal.AttackRequested ||
                _states.ContainsKey(signal.Agent) ||
                !HasActiveEnemyTarget(signal.Agent) ||
                !TryGetWieldedAutomaticRifle(
                    signal.Agent,
                    out EquipmentIndex rifleSlot,
                    out RifleSettings? settings) ||
                settings is null)
            {
                continue;
            }

            if (_outOfAmmoAgents.Contains(signal.Agent))
            {
                if (!TryFindAmmunitionSlot(
                        signal.Agent,
                        settings,
                        out _))
                {
                    continue;
                }

                _outOfAmmoAgents.Remove(signal.Agent);
            }

            _states.Add(
                signal.Agent,
                new AutomaticFireState(settings, rifleSlot));
            Debug.Print(
                $"Druglord: AI automatic trigger engaged for agent " +
                $"{signal.Agent.Index} with '{settings.ItemId}'.");
        }
    }

    private bool TickAgent(
        Agent agent,
        AutomaticFireState state)
    {
        if (!agent.IsActive() ||
            !agent.IsAIControlled ||
            ReferenceEquals(agent, Mission.MainAgent) ||
            agent.GetPrimaryWieldedItemIndex() != state.RifleSlot ||
            !TryGetRifleInSlot(
                agent,
                state.RifleSlot,
                out MissionWeapon rifle,
                out RifleSettings? settings) ||
            settings is null ||
            settings.FireMode != RifleFireMode.Automatic)
        {
            return false;
        }

        if (Mission.MissionEnded ||
            Mission.IsMissionEnding ||
            !HasActiveEnemyTarget(agent))
        {
            return false;
        }

        state.Settings = settings;
        if (state.State != AutomaticRifleState.Reloading &&
            (rifle.Ammo <= 0 || rifle.AmmoWeapon.IsEmpty))
        {
            return BeginReload(agent, state);
        }

        switch (state.State)
        {
        case AutomaticRifleState.Lowered:
            BeginRaise(agent, state);
            break;

        case AutomaticRifleState.Raising:
            if (Mission.CurrentTime >= state.RaiseCompletionTime)
            {
                EnterReady(agent, state);
                if (!FireAutomaticShot(agent, state, rifle))
                {
                    return false;
                }
            }
            break;

        case AutomaticRifleState.Ready:
            if (Mission.CurrentTime >= state.NextShotTime)
            {
                if (!FireAutomaticShot(agent, state, rifle))
                {
                    return false;
                }
            }
            break;

        case AutomaticRifleState.Firing:
            if (Mission.CurrentTime >= state.FiringCompletionTime)
            {
                rifle = agent.Equipment[state.RifleSlot];
                if (rifle.Ammo <= 0 || rifle.AmmoWeapon.IsEmpty)
                {
                    if (!BeginReload(agent, state))
                    {
                        return false;
                    }
                }
                else
                {
                    EnterReady(agent, state);
                    if (Mission.CurrentTime >= state.NextShotTime)
                    {
                        if (!FireAutomaticShot(agent, state, rifle))
                        {
                            return false;
                        }
                    }
                }
            }
            break;

        case AutomaticRifleState.Reloading:
            if (Mission.CurrentTime >= state.ReloadCompletionTime)
            {
                CompleteReload(agent, state);
                return false;
            }
            break;
        }

        return true;
    }

    private void BeginRaise(
        Agent agent,
        AutomaticFireState state)
    {
        state.State = AutomaticRifleState.Raising;
        state.RaiseCompletionTime =
            Mission.CurrentTime + state.Settings.RaiseDuration;
        agent.SetActionChannel(
            1,
            _readyAction,
            ignorePriority: true,
            actionSpeed: 1f,
            blendInPeriod: 0.08f,
            blendOutPeriod: 0.08f);
    }

    private void EnterReady(
        Agent agent,
        AutomaticFireState state)
    {
        state.State = AutomaticRifleState.Ready;
        agent.SetActionChannel(
            1,
            _readyAction,
            ignorePriority: true,
            actionSpeed: 1f,
            blendInPeriod: 0.04f,
            blendOutPeriod: 0.08f);
    }

    private bool FireAutomaticShot(
        Agent agent,
        AutomaticFireState state,
        MissionWeapon rifle)
    {
        if (agent.GetPrimaryWieldedItemIndex() != state.RifleSlot)
        {
            Debug.Print(
                $"Druglord: canceled queued AI shot for agent " +
                $"{agent.Index} because the rifle is no longer wielded.");
            return false;
        }

        RifleSettings settings = state.Settings;
        Vec3 position = GetMuzzlePosition(
            agent,
            state.RifleSlot,
            rifle.Item.MultiMeshName,
            settings);
        Vec3 direction = GetConstrainedAimDirection(agent);

        if (Mission.CurrentTime - state.LastShotTime >
            settings.RecoilResetDelay)
        {
            state.ConsecutiveShotCount = 0;
        }

        state.LastShotTime = Mission.CurrentTime;
        state.ConsecutiveShotCount = Math.Min(
            state.ConsecutiveShotCount + 1,
            settings.PeakRecoilShotCount);
        float recoilIntensity =
            (float)(state.ConsecutiveShotCount - 1) /
            (settings.PeakRecoilShotCount - 1);
        float spreadDegrees = MathF.Lerp(
            settings.MinimumSpreadDegrees,
            settings.MaximumSpreadDegrees,
            recoilIntensity);

        Mat3 orientation = Mat3.Identity;
        orientation.f = direction;
        orientation.u = Vec3.Up;
        orientation.Orthonormalize();
        orientation.RotateAboutUp(
            MBRandom.RandomFloatRanged(
                -spreadDegrees,
                spreadDegrees).ToRadians());
        orientation.RotateAboutSide(
            MBRandom.RandomFloatRanged(
                -spreadDegrees,
                spreadDegrees).ToRadians());
        direction = orientation.f;
        position += direction * 0.05f;

        float missileSpeed =
            rifle.GetModifiedMissileSpeedForCurrentUsage();
        Vec3 velocity = direction * missileSpeed;

        (_shootMissile ?? throw new InvalidOperationException(
            "Druglord automatic-rifle AI shooting was not initialized."))(
            Mission,
            agent,
            state.RifleSlot,
            position,
            velocity,
            orientation,
            false,
            true,
            -1);

        short remainingAmmo = (short)(rifle.Ammo - 1);
        agent.Equipment.SetConsumedAmmoOfSlot(
            state.RifleSlot,
            remainingAmmo);
        agent.SetActionChannel(
            1,
            _releaseAction,
            ignorePriority: true,
            actionSpeed: 2.5f,
            blendInPeriod: 0.02f,
            blendOutPeriod: 0.08f);

        state.State = AutomaticRifleState.Firing;
        state.FiringCompletionTime =
            Mission.CurrentTime + settings.RecoilDuration;
        state.NextShotTime =
            Mission.CurrentTime + settings.ShotInterval;
        state.AutomaticShotCount++;

        if (state.AutomaticShotCount <= 3 || remainingAmmo == 0)
        {
            Debug.Print(
                $"Druglord: AI agent {agent.Index} fired automatic " +
                $"shot {state.AutomaticShotCount}; magazine " +
                $"{remainingAmmo}/{settings.MagazineSize}.");
        }

        return true;
    }

    private bool BeginReload(
        Agent agent,
        AutomaticFireState state)
    {
        if (!TryFindAmmunitionSlot(
                agent,
                state.Settings,
                out _))
        {
            Debug.Print(
                $"Druglord: AI agent {agent.Index} is out of " +
                $"ammunition for '{state.Settings.ItemId}'.");
            _outOfAmmoAgents.Add(agent);
            return false;
        }

        state.State = AutomaticRifleState.Reloading;
        state.ReloadCompletionTime =
            Mission.CurrentTime + state.Settings.ReloadDuration;
        agent.SetActionChannel(
            1,
            _reloadAction,
            ignorePriority: true,
            actionSpeed: 1f,
            blendInPeriod: 0.05f,
            blendOutPeriod: 0.15f);
        return true;
    }

    private void CompleteReload(
        Agent agent,
        AutomaticFireState state)
    {
        if (!TryGetRifleInSlot(
                agent,
                state.RifleSlot,
                out MissionWeapon rifle,
                out RifleSettings? settings) ||
            settings is null ||
            !TryFindAmmunitionSlot(
                agent,
                settings,
                out EquipmentIndex ammunitionSlot))
        {
            return;
        }

        MissionWeapon reserve = agent.Equipment[ammunitionSlot];
        short reserveAmount = reserve.Amount;
        short roundsToLoad = (short)Math.Min(
            settings.MagazineSize,
            reserveAmount);
        if (roundsToLoad <= 0)
        {
            return;
        }

        MissionWeapon loadedRounds = reserve.Consume(roundsToLoad);
        reserve.Amount = reserveAmount;
        rifle.ReloadAmmo(loadedRounds, rifle.ReloadPhaseCount);
        agent.EquipWeaponWithNewEntity(
            state.RifleSlot,
            ref rifle);
        agent.EquipWeaponWithNewEntity(
            ammunitionSlot,
            ref reserve);
        agent.TryToWieldWeaponInSlot(
            state.RifleSlot,
            Agent.WeaponWieldActionType.InstantAfterPickUp,
            false);
        Debug.Print(
            $"Druglord: AI agent {agent.Index} reloaded " +
            $"{roundsToLoad} rounds for '{settings.ItemId}' " +
            "without consuming reserve ammunition.");
        _outOfAmmoAgents.Remove(agent);
    }

    private void EnsureAiInputHooks()
    {
        foreach (Agent agent in Mission.Agents)
        {
            if (!agent.IsActive() ||
                !agent.IsHuman ||
                !agent.IsAIControlled ||
                ReferenceEquals(agent, Mission.MainAgent) ||
                agent.GetComponent<AutomaticRifleAiComponent>() is not null ||
                !HasConfiguredAutomaticRifle(agent))
            {
                continue;
            }

            AutomaticRifleAiComponent component =
                new AutomaticRifleAiComponent(agent, this);
            agent.AddComponent(component);
            component.Initialize();
            agent.SetHasOnAiInputSetCallback(true);
        }
    }

    private static bool HasConfiguredAutomaticRifle(Agent agent)
    {
        for (EquipmentIndex slot = EquipmentIndex.WeaponItemBeginSlot;
             slot < EquipmentIndex.NumAllWeaponSlots;
             slot++)
        {
            if (TryGetRifleInSlot(
                    agent,
                    slot,
                    out _,
                    out RifleSettings? settings) &&
                settings?.FireMode == RifleFireMode.Automatic)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetWieldedAutomaticRifle(
        Agent agent,
        out EquipmentIndex rifleSlot,
        out RifleSettings? settings)
    {
        rifleSlot = agent.GetPrimaryWieldedItemIndex();
        if (!TryGetRifleInSlot(
                agent,
                rifleSlot,
                out _,
                out settings))
        {
            return false;
        }

        return settings?.FireMode == RifleFireMode.Automatic;
    }

    private static bool TryGetRifleInSlot(
        Agent agent,
        EquipmentIndex rifleSlot,
        out MissionWeapon rifle,
        out RifleSettings? settings)
    {
        rifle = MissionWeapon.Invalid;
        settings = null;
        if (rifleSlot == EquipmentIndex.None)
        {
            return false;
        }

        rifle = agent.Equipment[rifleSlot];
        return !rifle.IsEmpty &&
               RifleSettingsRegistry.TryGet(
                   Game.Current,
                   rifle.Item.StringId,
                   out settings);
    }

    private static bool TryFindAmmunitionSlot(
        Agent agent,
        RifleSettings settings,
        out EquipmentIndex ammunitionSlot)
    {
        for (EquipmentIndex slot = EquipmentIndex.WeaponItemBeginSlot;
             slot < EquipmentIndex.NumAllWeaponSlots;
             slot++)
        {
            MissionWeapon weapon = agent.Equipment[slot];
            if (!weapon.IsEmpty &&
                weapon.Item.StringId == settings.AmmunitionItemId &&
                weapon.Amount > 0)
            {
                ammunitionSlot = slot;
                return true;
            }
        }

        ammunitionSlot = EquipmentIndex.None;
        return false;
    }

    private static bool HasActiveEnemyTarget(Agent agent)
    {
        Agent? target = agent.GetTargetAgent();
        if (target is null || !target.IsActive())
        {
            return false;
        }

        Team? agentTeam = agent.Team;
        Team? targetTeam = target.Team;
        return agentTeam is not null &&
               targetTeam is not null &&
               agentTeam.IsEnemyOf(targetTeam);
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
                "Druglord could not resolve the AI rifle visual root.");
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
                Vec3 meshPosition =
                    meshFrame.TransformToParent(localMuzzlePosition);
                Vec3 metaMeshPosition =
                    metaMeshFrame.TransformToParent(meshPosition);
                return rootFrame.TransformToParent(metaMeshPosition);
            }

            throw new InvalidOperationException(
                $"Druglord could not find AI muzzle material " +
                $"'{settings.MuzzleMeshMaterial}' on '{metaMeshName}'.");
        }

        throw new InvalidOperationException(
            $"Druglord could not find AI rifle mesh '{metaMeshName}'.");
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

        float distanceToMinimum = MathF.Abs(
            MBMath.WrapAngle(
                relativeAimAngle - bodyRotationConstraint.x));
        float distanceToMaximum = MathF.Abs(
            MBMath.WrapAngle(
                relativeAimAngle - bodyRotationConstraint.y));
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
                "Unknown AI rifle muzzle face.");
        }
    }

    private void RemoveState(Agent agent)
    {
        _states.Remove(agent);
    }
}
