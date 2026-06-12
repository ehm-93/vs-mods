using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Ehm93.VS.World.InterestingMobs;

/// <summary>
/// Hide-and-seek stalking, weeping-angel rules keyed on the player's view:
///  - off-screen (outside the view cone): free to pursue, cover-biased;
///  - on-screen but LOS-blocked (player facing its cover): hold perfectly still;
///  - on-screen and visible: slip into cover; stared at too long -> break for
///    fresh cover at run speed.
/// Cover candidates are sampled around the player and scored by whether
/// terrain blocks the ray from the player's eyes. Yields permanently once the
/// fuse arms (bloatArmed watched attribute).
/// </summary>
public class AiTaskBloatStalk : AiTaskBaseTargetable
{
    private readonly float seekingRange;
    private readonly float minRange;
    private readonly float maxRange;
    private readonly float moveSpeed;
    private readonly float runSpeed;
    private readonly int watchedToleranceMs;
    private readonly int coverSamples;
    private readonly float approachRange;

    // cos thresholds against the player's view direction:
    // on-screen test (~120 degree FOV) vs actively-staring-at-me (~25 degrees)
    private const double FrustumDot = 0.5;
    private const double StareDot = 0.9;

    private float watchedMs;
    private long lastPlanMs;
    private long lastDebugMs;
    private bool moving;
    private readonly bool debug;

    public AiTaskBloatStalk(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig)
        : base(entity, taskConfig, aiConfig)
    {
        seekingRange = taskConfig["seekingRange"].AsFloat(20f);
        minRange = taskConfig["minRange"].AsFloat(7f);
        maxRange = taskConfig["maxRange"].AsFloat(14f);
        moveSpeed = taskConfig["movespeed"].AsFloat(0.012f);
        runSpeed = taskConfig["runspeed"].AsFloat(0.02f);
        watchedToleranceMs = taskConfig["watchedToleranceMs"].AsInt(1600);
        coverSamples = taskConfig["coverSamples"].AsInt(10);
        approachRange = taskConfig["approachRange"].AsFloat(4f);
        debug = taskConfig["debug"].AsBool(false);
    }

    private void DebugLog(string msg)
    {
        if (!debug) return;
        long now = world.ElapsedMilliseconds;
        if (now - lastDebugMs < 1200) return;
        lastDebugMs = now;
        world.Logger.Notification("[bloatstalk {0}] {1}", entity.EntityId, msg);
    }

    public override bool ShouldExecute()
    {
        if (world.ElapsedMilliseconds < cooldownUntilMs) return false;
        if (entity.WatchedAttributes.GetBool("bloatArmed")) return false;

        IPlayer? plr = world.NearestPlayer(entity.Pos.X, entity.Pos.Y, entity.Pos.Z);
        Entity? target = plr?.Entity;
        if (target == null || !target.Alive) return false;
        if (entity.Pos.DistanceTo(target.Pos.XYZ) > seekingRange) return false;
        if (!CanSense(target, seekingRange))
        {
            DebugLog("player in range but CanSense=false (creative/spectator gamemode? world hostility?)");
            return false;
        }

        targetEntity = target;
        return true;
    }

    public override void StartExecute()
    {
        base.StartExecute();
        // base starts the walk animation for the whole task lifetime; we only
        // want it while actually moving - stationary-in-cover is an idle pose,
        // not a treadmill
        StopMoveAnimation();
        watchedMs = 0;
        lastPlanMs = 0;
        moving = false;
    }

    public override bool ContinueExecute(float dt)
    {
        if (!base.ContinueExecute(dt)) return false;
        Entity? target = targetEntity;
        if (target == null || !target.Alive) return false;
        if (entity.WatchedAttributes.GetBool("bloatArmed")) return false;

        double dist = entity.Pos.DistanceTo(target.Pos.XYZ);
        if (dist > seekingRange * 1.5) return false;

        double align = ViewAlignment(target);
        bool losOpen = LosOpen(EyesOf(target), CenterOf(entity));
        watchedMs = losOpen && align > StareDot
            ? watchedMs + dt * 1000f
            : Math.Max(0f, watchedMs - dt * 1500f);

        long now = world.ElapsedMilliseconds;
        if (now - lastPlanMs > 700)
        {
            lastPlanMs = now;
            DebugLog($"dist={dist:F1} align={align:F2} inFrustum={align > FrustumDot} losOpen={losOpen} watched={watchedMs:F0} moving={moving}");
            Plan(target, dist, align > FrustumDot, losOpen);
        }

        return true;
    }

    public override void FinishExecute(bool cancelled)
    {
        base.FinishExecute(cancelled);
        pathTraverser.Stop();
        moving = false;
    }

    private void Plan(Entity target, double dist, bool inFrustum, bool losOpen)
    {
        if (!inFrustum)
        {
            // off-screen: the hunt. Abandon the band and close to kill
            // distance - the fuse arms on the way in. Hurry while far,
            // creep once near.
            if (moving) return;

            if (dist > approachRange)
            {
                MoveTo(PickCover(target, approachRange), dist > maxRange ? runSpeed : moveSpeed);
            }
            else
            {
                StopMoving();
            }
            return;
        }

        if (!losOpen)
        {
            // on-screen but behind cover they're facing: hold perfectly still -
            // movement across gaps is what gets glimpsed
            StopMoving();
            return;
        }

        // on-screen and visible
        if (watchedMs > watchedToleranceMs)
        {
            // they've been staring at us - break line of sight, fast
            watchedMs = 0;
            MoveTo(PickCover(target, (minRange + maxRange) / 2f), runSpeed);
            return;
        }

        // glimpsed but not stared down yet: slip into cover - commit to the
        // current move instead of re-deciding mid-path (reads as wiggling)
        if (moving) return;

        if (dist < minRange)
        {
            MoveTo(PickCover(target, maxRange), runSpeed);
        }
        else
        {
            MoveTo(PickCover(target, GameMath.Clamp((float)dist, minRange, maxRange)), moveSpeed);
        }
    }

    private void MoveTo(Vec3d? goal, float speed)
    {
        if (goal == null) return;
        moving = true;
        if (animMeta != null) entity.AnimManager.StartAnimation(animMeta);
        pathTraverser.NavigateTo_Async(goal, speed, 0.8f, OnGoalReached, OnStuck, OnNoPath, 3500, 1);
    }

    private void StopMoving()
    {
        if (!moving) return;
        moving = false;
        pathTraverser.Stop();
        StopMoveAnimation();
    }

    private void StopMoveAnimation()
    {
        if (animMeta != null) entity.AnimManager.StopAnimation(animMeta.Code);
    }

    private void OnGoalReached() { moving = false; StopMoveAnimation(); }
    private void OnStuck() { moving = false; StopMoveAnimation(); }
    private void OnNoPath() { moving = false; StopMoveAnimation(); }

    /// <summary>
    /// Sample points on a ring around the target and pick the best-hidden,
    /// cheapest-to-reach one. Hidden = terrain blocks the ray from the
    /// target's eyes to the candidate.
    /// </summary>
    private Vec3d? PickCover(Entity target, float radius)
    {
        Vec3d eyes = EyesOf(target);
        Vec3d? best = null;
        double bestScore = double.MinValue;

        for (int i = 0; i < coverSamples; i++)
        {
            double ang = rand.NextDouble() * GameMath.TWOPI;
            double rad = radius + (rand.NextDouble() - 0.5) * 4;
            double x = target.Pos.X + Math.Sin(ang) * rad;
            double z = target.Pos.Z + Math.Cos(ang) * rad;

            if (!TryFindGround(x, (int)target.Pos.Y, z, out int groundY)) continue;
            Vec3d candidate = new Vec3d(x, groundY, z);

            double score = 0;
            if (!LosOpen(eyes, candidate.AddCopy(0, 0.6, 0))) score += 3;
            score += 1 - Math.Abs(rad - radius) / Math.Max(1, radius);
            score -= entity.Pos.DistanceTo(candidate) / 40.0;

            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return best;
    }

    /// <summary>Scan a small column near refY for standable ground (solid top below, passable space at/above).</summary>
    private bool TryFindGround(double x, int refY, double z, out int groundY)
    {
        var ba = world.BlockAccessor;
        int dim = entity.Pos.Dimension;
        var pos = new BlockPos((int)x, 0, (int)z, dim);

        for (int dy = 2; dy >= -6; dy--)
        {
            int y = refY + dy;
            pos.Y = y - 1;
            if (!ba.GetBlock(pos).SideSolid[BlockFacing.UP.Index]) continue;
            pos.Y = y;
            if (HasCollision(ba, pos)) continue;
            pos.Y = y + 1;
            if (HasCollision(ba, pos)) continue;

            groundY = y;
            return true;
        }

        groundY = 0;
        return false;
    }

    private static bool HasCollision(IBlockAccessor ba, BlockPos pos)
    {
        var boxes = ba.GetBlock(pos).CollisionBoxes;
        return boxes != null && boxes.Length > 0;
    }

    /// <summary>Cosine of the angle between the target's view direction and the direction from their eyes to this entity.</summary>
    private double ViewAlignment(Entity target)
    {
        Vec3f view = target.Pos.GetViewVector();
        Vec3d toMe = CenterOf(entity).Sub(EyesOf(target)).Normalize();
        return view.X * toMe.X + view.Y * toMe.Y + view.Z * toMe.Z;
    }

    /// <summary>
    /// True when no terrain blocks the ray between the two points. Only
    /// collidable blocks count as cover - selection-box tracing would treat
    /// grass tufts and snow layers as walls.
    /// </summary>
    private bool LosOpen(Vec3d from, Vec3d to)
    {
        BlockSelection? bSel = null;
        EntitySelection? eSel = null;
        world.RayTraceForSelection(this, from, to, ref bSel, ref eSel,
            (pos, block) => block?.CollisionBoxes != null && block.CollisionBoxes.Length > 0,
            (e) => false);
        return bSel == null;
    }

    private static Vec3d EyesOf(Entity e) => e.Pos.XYZ.AddCopy(e.LocalEyePos);

    private static Vec3d CenterOf(Entity e) => e.Pos.XYZ.AddCopy(0, e.SelectionBox.Y2 / 2f, 0);
}
