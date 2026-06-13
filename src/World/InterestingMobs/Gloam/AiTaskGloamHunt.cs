using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Ehm93.VS.World.InterestingMobs;

/// <summary>
/// The whole gloam hunt as one state machine over three concentric ranges
/// (detection > stalking > engagement):
///
///  Approach — player crossed into detection: sprint to the stalking ring.
///  Stalk    — circle the player (L or R, 50/50) at the stalking radius, prefer
///             dark standpoints, occasionally divert to snuff a torch. Two timers
///             run from when stalking began: ATTACK and FORCEATTACK.
///               • player crosses into engagement BEFORE the attack timer → Flee
///               • player crosses into engagement AFTER  the attack timer → Attack
///               • force-attack timer elapses                             → Attack
///               • player has no light at all                             → Attack now
///  Flee     — sprint to just outside the stalking ring, then re-Stalk (timers reset).
///             Any damage taken outside of an attack drops straight to Flee.
///  Attack   — charge → secretary-bird stomp → melt away (into Flee). Committed:
///             damage does not break a running attack.
///
/// One task instead of four competing on priority — the cross-state timer logic
/// ("did the player cross engagement before or after the attack timer") only works
/// cleanly when a single owner holds the state.
/// </summary>
public class AiTaskGloamHunt : AiTaskBaseTargetable
{
    private enum State { Approach, Stalk, Snuff, Flee, Attack }
    private enum AtkPhase { Charge, Stomp }

    // ranges
    private readonly float detectionRange;
    private readonly float stalkRange;
    private readonly float engageRange;

    // timers (from when the current stalk began)
    private readonly int attackTimerMs;
    private readonly int forceAttackMs;

    // speeds
    private readonly float stalkSpeed;
    private readonly float sprintSpeed;
    private readonly float chargeSpeed;

    // light
    private readonly int lightThreshold;       // dark-preference for standpoints
    private readonly int playerDarkThreshold;  // player this dark → pounce immediately

    // stalk shape
    private readonly float stalkStepRad;
    private readonly double dirFlipChance;

    // snuff
    private readonly float torchSearchRange;
    private readonly float minPlayerDistToTorch;
    private readonly int snuffCooldownMs;

    // attack
    private readonly float strikeReach;
    private readonly int stompMs;
    private readonly int damageAtMs;
    private readonly float damage;
    private readonly int damageTier;
    private readonly float knockback;

    private readonly int fleeMaxMs;
    private readonly bool debug;

    private readonly AnimationMetaData strideAnim;
    private readonly AnimationMetaData sprintAnim;
    private readonly AnimationMetaData stompAnim;

    private State state;
    private AtkPhase atkPhase;
    private long stateStartMs;
    private long stalkStartMs;
    private int circleDir = 1;
    private long lastPlanMs;
    private long lastPathMs;
    private long lastDebugMs;
    private long lastSnuffMs = -100000;
    private bool moving;
    private string activeMoveAnim = "";

    private Entity? player;
    private Entity? hurtBy;
    private bool pendingFlee;

    private BlockPos? torchPos;
    private bool damageDealt;
    private long snuffArrivedMs = -1;
    private bool snuffed;
    private Vec3d? fleeGoal;

    public AiTaskGloamHunt(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig)
        : base(entity, taskConfig, aiConfig)
    {
        detectionRange = taskConfig["detectionRange"].AsFloat(22f);
        stalkRange = taskConfig["stalkRange"].AsFloat(13f);
        engageRange = taskConfig["engageRange"].AsFloat(5f);

        attackTimerMs = taskConfig["attackTimerMs"].AsInt(6000);
        forceAttackMs = taskConfig["forceAttackMs"].AsInt(14000);

        stalkSpeed = taskConfig["stalkSpeed"].AsFloat(0.0056f);
        sprintSpeed = taskConfig["sprintSpeed"].AsFloat(0.045f);
        chargeSpeed = taskConfig["chargeSpeed"].AsFloat(0.052f);

        lightThreshold = taskConfig["lightThreshold"].AsInt(7);
        playerDarkThreshold = taskConfig["playerDarkThreshold"].AsInt(4);

        stalkStepRad = taskConfig["stalkStepDeg"].AsFloat(22f) * GameMath.DEG2RAD;
        dirFlipChance = taskConfig["dirFlipChance"].AsFloat(0.05f);

        torchSearchRange = taskConfig["torchSearchRange"].AsFloat(14f);
        minPlayerDistToTorch = taskConfig["minPlayerDistToTorch"].AsFloat(6f);
        snuffCooldownMs = taskConfig["snuffCooldownMs"].AsInt(9000);

        strikeReach = taskConfig["strikeReach"].AsFloat(2.6f);
        stompMs = taskConfig["stompMs"].AsInt(930);
        damageAtMs = taskConfig["damageAtMs"].AsInt(400);
        damage = taskConfig["damage"].AsFloat(8f);
        damageTier = taskConfig["damageTier"].AsInt(2);
        knockback = taskConfig["knockbackStrength"].AsFloat(1.5f);

        fleeMaxMs = taskConfig["fleeMaxMs"].AsInt(3000);
        debug = taskConfig["debug"].AsBool(false);

        strideAnim = Anim(taskConfig["stalkAnimation"].AsString("stride"), taskConfig["stalkAnimationSpeed"].AsFloat(1f));
        sprintAnim = Anim(taskConfig["sprintAnimation"].AsString("sprint"), taskConfig["sprintAnimationSpeed"].AsFloat(2.7f));
        stompAnim = Anim(taskConfig["stompAnimation"].AsString("stomp"), 1f);
    }

    private static AnimationMetaData Anim(string code, float speed)
        => new AnimationMetaData() { Animation = code, Code = code, AnimationSpeed = speed }.Init();

    private void DebugLog(string msg)
    {
        if (!debug) return;
        long now = world.ElapsedMilliseconds;
        if (now - lastDebugMs < 700) return;
        lastDebugMs = now;
        world.Logger.Notification("[gloamhunt {0}] {1}", entity.EntityId, msg);
    }

    public override bool ShouldExecute()
    {
        IPlayer? plr = world.NearestPlayer(entity.Pos.X, entity.Pos.Y, entity.Pos.Z);
        Entity? t = plr?.Entity;
        if (t == null || !t.Alive) return false;
        if (entity.Pos.DistanceTo(t.Pos.XYZ) > detectionRange) return false;
        if (!CanSense(t, detectionRange)) return false;

        player = t;
        targetEntity = t;
        return true;
    }

    public override void StartExecute()
    {
        base.StartExecute();
        moving = false;
        activeMoveAnim = "";
        pendingFlee = false;
        // detected in darkness → pounce on sight, no stalk; else close in / stalk
        if (player != null && GloamSenses.LightAt(world, player.Pos.AsBlockPos, player) <= playerDarkThreshold)
            EnterAttack();
        else if (player != null && entity.Pos.DistanceTo(player.Pos.XYZ) <= stalkRange) EnterStalk();
        else EnterApproach();
    }

    public override bool ContinueExecute(float dt)
    {
        if (!base.ContinueExecute(dt)) return false;

        // re-acquire the nearest player each tick so it tracks the real threat
        IPlayer? plr = world.NearestPlayer(entity.Pos.X, entity.Pos.Y, entity.Pos.Z);
        player = plr?.Entity;
        if (player == null || !player.Alive) return false;

        double dist = entity.Pos.DistanceTo(player.Pos.XYZ);
        if (dist > detectionRange * 1.5) return false;   // lost interest

        // damage outside of a committed attack → bail to flee
        if (pendingFlee)
        {
            pendingFlee = false;
            if (state != State.Attack) EnterFlee();
        }

        long now = world.ElapsedMilliseconds;
        int playerLight = GloamSenses.LightAt(world, player.Pos.AsBlockPos, player);
        DebugLog($"state={state} dist={dist:F1} playerLight={playerLight} (dark<={playerDarkThreshold}) held={GloamSenses.HeldLightLevel(world, player)} stalked={now - stalkStartMs}ms");

        // player in darkness → pounce immediately, from any watching state (no stalk).
        // Doesn't interrupt a committed attack or a (brief, post-attack/damage) flee.
        if (playerLight <= playerDarkThreshold && state != State.Attack && state != State.Flee)
        {
            EnterAttack();
        }

        switch (state)
        {
            case State.Approach: TickApproach(dist, playerLight, now); break;
            case State.Stalk: TickStalk(dist, playerLight, now); break;
            case State.Snuff: TickSnuff(dist, now); break;
            case State.Flee: TickFlee(dist, now); break;
            case State.Attack: TickAttack(dist, now); break;
        }

        return true;
    }

    public override void FinishExecute(bool cancelled)
    {
        base.FinishExecute(cancelled);
        pathTraverser.Stop();
        StopMoveAnims();
        entity.AnimManager.StopAnimation(stompAnim.Code);
        moving = false;
    }

    // any hit arms a flee; consumed next tick (and ignored if mid-attack)
    public override void OnEntityHurt(DamageSource source, float damage)
    {
        base.OnEntityHurt(source, damage);
        hurtBy = source?.GetCauseEntity();
        pendingFlee = true;
    }

    // ---------------- states ----------------

    private void EnterApproach()
    {
        state = State.Approach;
        stateStartMs = world.ElapsedMilliseconds;
        lastPlanMs = 0;
        DebugLog("approach");
    }

    private void TickApproach(double dist, int playerLight, long now)
    {
        // darkness pounce handled globally in ContinueExecute
        if (dist <= stalkRange) { EnterStalk(); return; }

        if (now - lastPlanMs > 400 && !moving)
        {
            lastPlanMs = now;
            Vec3d toEdge = entity.Pos.XYZ.SubCopy(player!.Pos.XYZ).Normalize();
            Vec3d ringPt = player.Pos.XYZ.AddCopy(toEdge.X * (stalkRange - 1), 0, toEdge.Z * (stalkRange - 1));
            Drive(DarkStandpoint(ringPt.X, ringPt.Z, player) ?? ringPt, sprintSpeed, sprintAnim);
        }
    }

    // resetTimers: true for a fresh stalk engagement (from approach/flee/attack);
    // false when returning from a snuff diversion — snuff is PART of stalking, so
    // resetting the attack/forceAttack clock on every torch pinch made forceAttack
    // rarely accumulate (the "feels flaky").
    private void EnterStalk(bool resetTimers = true)
    {
        state = State.Stalk;
        stateStartMs = world.ElapsedMilliseconds;
        if (resetTimers)
        {
            stalkStartMs = world.ElapsedMilliseconds;   // attack + forceAttack run from here
            circleDir = rand.NextDouble() < 0.5 ? 1 : -1;
        }
        lastPlanMs = 0;
        StopMoving();
        DebugLog($"stalk (dir={circleDir}, reset={resetTimers}, stalked={world.ElapsedMilliseconds - stalkStartMs}ms)");
    }

    private void TickStalk(double dist, int playerLight, long now)
    {
        long stalked = now - stalkStartMs;

        // (darkness pounce handled globally in ContinueExecute)
        if (dist <= engageRange)
        {
            if (stalked < attackTimerMs) EnterFlee();   // skittish: crowded too early
            else EnterAttack();                          // emboldened: now it strikes
            return;
        }
        if (stalked >= forceAttackMs) { EnterAttack(); return; }

        // drifted out of the ring (but still detected) → close back up
        if (dist > stalkRange + 2) { EnterApproach(); return; }

        // opportunistic torch snuff while circling
        if (now - lastSnuffMs > snuffCooldownMs)
        {
            BlockPos? torch = FindTorch();
            if (torch != null) { torchPos = torch; EnterSnuff(); return; }
        }

        // circle
        if (now - lastPlanMs > 650 && !moving)
        {
            lastPlanMs = now;
            if (rand.NextDouble() < dirFlipChance) circleDir = -circleDir;
            Vec3d? wp = CirclePoint();
            if (wp != null) Drive(wp, stalkSpeed, strideAnim);
            else StopMoving();
        }
    }

    private void EnterSnuff()
    {
        state = State.Snuff;
        stateStartMs = world.ElapsedMilliseconds;
        snuffArrivedMs = -1;
        snuffed = false;
        moving = true;
        Drive(TorchCenter(), sprintSpeed, sprintAnim);   // sprint at the torch
        DebugLog($"snuff dart {torchPos}");
    }

    private void TickSnuff(double dist, long now)
    {
        // returning to stalk from a snuff does NOT reset the timers (snuff is mid-stalk)
        if (torchPos == null || !IsLitTorch(world.BlockAccessor.GetBlock(torchPos))) { EnterStalk(false); return; }
        if (dist < engageRange) { lastSnuffMs = now; EnterFlee(); return; } // player closed in — abort

        double td = entity.Pos.DistanceTo(TorchCenter());
        if (td <= 1.6)
        {
            // arrived: stomp the torch out — same animation as the attack, snuff
            // lands on the impact frame
            if (snuffArrivedMs < 0)
            {
                snuffArrivedMs = now;
                snuffed = false;
                StopMoving();
                entity.AnimManager.StartAnimation(stompAnim);
            }
            FaceXZ(torchPos!.X + 0.5, torchPos.Z + 0.5, (float)(now - lastPlanMs));
            lastPlanMs = now;
            if (!snuffed && now - snuffArrivedMs >= damageAtMs) { Snuff(); snuffed = true; }
            if (now - snuffArrivedMs >= stompMs)
            {
                entity.AnimManager.StopAnimation(stompAnim.Code);
                lastSnuffMs = now;
                EnterStalk(false);
            }
        }
        else if (!moving)
        {
            Drive(TorchCenter(), sprintSpeed, sprintAnim);
        }
    }

    private void EnterFlee()
    {
        state = State.Flee;
        stateStartMs = world.ElapsedMilliseconds;
        Entity from = (hurtBy != null && hurtBy.Alive) ? hurtBy : player!;
        Vec3d away = entity.Pos.XYZ.SubCopy(from.Pos.XYZ).Normalize();
        // just outside the stalking ring, measured from the player
        Vec3d goal = player!.Pos.XYZ.AddCopy(away.X * (stalkRange + 3), 0, away.Z * (stalkRange + 3));
        fleeGoal = DarkStandpoint(goal.X, goal.Z, player) ?? goal;
        Drive(fleeGoal, sprintSpeed, sprintAnim);
        DebugLog("flee");
    }

    private void TickFlee(double dist, long now)
    {
        bool arrived = fleeGoal != null && entity.Pos.DistanceTo(fleeGoal) < 1.8;
        if (arrived || !moving || now - stateStartMs > fleeMaxMs)
        {
            EnterStalk();   // re-stalk; timers reset
        }
    }

    private void EnterAttack()
    {
        state = State.Attack;
        atkPhase = AtkPhase.Charge;
        stateStartMs = world.ElapsedMilliseconds;
        damageDealt = false;
        lastPathMs = 0;
        Drive(player!.Pos.XYZ.Clone(), chargeSpeed, sprintAnim);
        DebugLog("attack: charge");
    }

    private void TickAttack(double dist, long now)
    {
        switch (atkPhase)
        {
            case AtkPhase.Charge:
                if (now - stateStartMs > 6000) { EnterFlee(); return; }   // couldn't reach
                if (dist <= strikeReach)
                {
                    atkPhase = AtkPhase.Stomp;
                    stateStartMs = now;
                    StopMoving();
                    entity.AnimManager.StartAnimation(stompAnim);
                    DebugLog("attack: stomp");
                    return;
                }
                if (now - lastPathMs > 400)
                {
                    lastPathMs = now;
                    Drive(player!.Pos.XYZ.Clone(), chargeSpeed, sprintAnim);
                }
                break;

            case AtkPhase.Stomp:
                FaceTarget(player!, (float)(now - lastPlanMs));
                lastPlanMs = now;
                if (!damageDealt && now - stateStartMs >= damageAtMs)
                {
                    damageDealt = true;
                    if (player!.Alive && entity.Pos.DistanceTo(player.Pos.XYZ) <= strikeReach + 0.6)
                    {
                        player.ReceiveDamage(new DamageSource()
                        {
                            Source = EnumDamageSource.Entity,
                            SourceEntity = entity,
                            Type = EnumDamageType.BluntAttack,
                            DamageTier = damageTier,
                            KnockbackStrength = knockback,
                        }, damage);
                        world.PlaySoundAt(new AssetLocation("game", "sounds/player/projectilehit"),
                            player.Pos.X, player.Pos.Y, player.Pos.Z, null, true, 16f);
                        DebugLog("stomped");
                    }
                }
                if (now - stateStartMs >= stompMs)
                {
                    entity.AnimManager.StopAnimation(stompAnim.Code);
                    EnterFlee();   // melt away
                }
                break;
        }
    }

    // ---------------- helpers ----------------

    /// <summary>Next point along the circle around the player at the stalking radius, dark-preferred.</summary>
    private Vec3d? CirclePoint()
    {
        double thetaSelf = Math.Atan2(entity.Pos.X - player!.Pos.X, entity.Pos.Z - player.Pos.Z);
        double thetaNext = thetaSelf + circleDir * stalkStepRad;
        double x = player.Pos.X + Math.Sin(thetaNext) * stalkRange;
        double z = player.Pos.Z + Math.Cos(thetaNext) * stalkRange;
        return DarkStandpoint(x, z, player);
    }

    /// <summary>Standable spot near (x,z), nudged to the darkest of a few nearby samples; null if no ground.</summary>
    private Vec3d? DarkStandpoint(double x, double z, Entity refPlayer)
    {
        int dim = entity.Pos.Dimension;
        Vec3d? best = null;
        double bestScore = double.MinValue;
        for (int i = 0; i < 6; i++)
        {
            double ox = x + (rand.NextDouble() - 0.5) * 4;
            double oz = z + (rand.NextDouble() - 0.5) * 4;
            if (!GloamSenses.TryFindGround(world, dim, ox, (int)player!.Pos.Y, oz, out int gy)) continue;
            int light = GloamSenses.LightAt(world, new BlockPos((int)ox, gy, (int)oz, dim), refPlayer);
            // strongly avoid crossing the dark edge (light is its wall), then prefer dark + short travel
            double score = -light - entity.Pos.DistanceTo(new Vec3d(ox, gy, oz)) * 0.05;
            if (light > lightThreshold) score -= 100;
            if (score > bestScore) { bestScore = score; best = new Vec3d(ox, gy, oz); }
        }
        return best;
    }

    private BlockPos? FindTorch()
    {
        int r = (int)torchSearchRange;
        int dim = entity.Pos.Dimension;
        BlockPos c = entity.Pos.AsBlockPos;
        BlockPos min = new BlockPos(c.X - r, c.Y - 5, c.Z - r, dim);
        BlockPos max = new BlockPos(c.X + r, c.Y + 5, c.Z + r, dim);
        BlockPos? best = null;
        double bestDistSq = double.MaxValue;
        world.BlockAccessor.WalkBlocks(min, max, (block, x, y, z) =>
        {
            if (!IsLitTorch(block)) return;
            if (player!.Pos.XYZ.SquareDistanceTo(x + 0.5, y + 0.5, z + 0.5) < minPlayerDistToTorch * minPlayerDistToTorch) return;
            double d = entity.Pos.XYZ.SquareDistanceTo(x + 0.5, y + 0.5, z + 0.5);
            if (d < bestDistSq) { bestDistSq = d; best = new BlockPos(x, y, z, dim); }
        });
        return best;
    }

    private void Snuff()
    {
        if (torchPos == null) return;
        Block lit = world.BlockAccessor.GetBlock(torchPos);
        if (!IsLitTorch(lit)) return;
        Block? extinct = world.GetBlock(new AssetLocation(lit.Code.Domain, lit.Code.Path.Replace("-lit-", "-extinct-")));
        world.BlockAccessor.SetBlock(extinct?.BlockId ?? 0, torchPos);
        world.PlaySoundAt(new AssetLocation("game", "sounds/effect/extinguish"),
            torchPos.X + 0.5, torchPos.Y + 0.5, torchPos.Z + 0.5, null, true, 18f);
        DebugLog($"snuffed {lit.Code}");
    }

    private static bool IsLitTorch(Block? b)
        => b?.Code != null && b.Code.Path.StartsWith("torch-") && b.Code.Path.Contains("-lit-");

    private Vec3d TorchCenter() => new Vec3d(torchPos!.X + 0.5, torchPos.Y, torchPos.Z + 0.5);

    private void FaceTarget(Entity t, float dtMs) => FaceXZ(t.Pos.X, t.Pos.Z, dtMs);

    private void FaceXZ(double x, double z, float dtMs)
    {
        float targetYaw = (float)Math.Atan2(x - entity.Pos.X, z - entity.Pos.Z);
        float diff = GameMath.AngleRadDistance(entity.Pos.Yaw, targetYaw);
        entity.Pos.Yaw += diff * Math.Min(1f, dtMs / 1000f * 8f);
    }

    private void Drive(Vec3d? goal, float speed, AnimationMetaData anim)
    {
        if (goal == null) return;
        moving = true;
        if (activeMoveAnim != anim.Code)
        {
            StopMoveAnims();
            entity.AnimManager.StartAnimation(anim);
            activeMoveAnim = anim.Code;
        }
        pathTraverser.NavigateTo_Async(goal, speed, 0.6f, OnGoalReached, OnStuck, OnNoPath, 3500, 1);
    }

    private void StopMoving()
    {
        if (!moving) return;
        moving = false;
        pathTraverser.Stop();
        StopMoveAnims();
    }

    private void StopMoveAnims()
    {
        if (activeMoveAnim != "") entity.AnimManager.StopAnimation(activeMoveAnim);
        activeMoveAnim = "";
    }

    private void OnGoalReached() { moving = false; StopMoveAnims(); }
    private void OnStuck() { moving = false; StopMoveAnims(); }
    private void OnNoPath() { moving = false; StopMoveAnims(); }
}
