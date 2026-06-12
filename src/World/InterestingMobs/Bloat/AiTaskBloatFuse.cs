using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace Ehm93.VS.World.InterestingMobs;

/// <summary>
/// The point of no return. Arms when a player comes within triggerRange
/// (sensed through walls) or when the bloat takes any damage. Once armed it
/// sprints at the nearest player, cannot be cancelled (give it a high
/// priorityForCancel), and detonates when the fuse runs out: an explosion
/// plus a temporal-stability drain on nearby players. Only death stops it.
/// </summary>
public class AiTaskBloatFuse : AiTaskBaseTargetable
{
    private readonly float triggerRange;
    private readonly int fuseMs;
    private readonly float moveSpeed;
    private readonly double destructionRadius;
    private readonly double injureRadius;
    private readonly double stabilityDrain;

    private bool armed;
    private float fuseAccumMs;
    private long lastRetargetMs;

    public AiTaskBloatFuse(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig)
        : base(entity, taskConfig, aiConfig)
    {
        triggerRange = taskConfig["triggerRange"].AsFloat(5f);
        fuseMs = taskConfig["fuseMs"].AsInt(3000);
        moveSpeed = taskConfig["movespeed"].AsFloat(0.022f);
        destructionRadius = taskConfig["destructionRadius"].AsDouble(2.5);
        injureRadius = taskConfig["injureRadius"].AsDouble(5);
        stabilityDrain = taskConfig["stabilityDrain"].AsDouble(0.1);
    }

    public override void OnEntityHurt(DamageSource source, float damage)
    {
        base.OnEntityHurt(source, damage);
        if (damage > 0 && entity.Alive && !armed) Arm();
    }

    public override bool ShouldExecute()
    {
        if (!entity.Alive) return false;
        if (armed) return true;

        IPlayer? plr = world.NearestPlayer(entity.Pos.X, entity.Pos.Y, entity.Pos.Z);
        Entity? target = plr?.Entity;
        if (target == null || !target.Alive) return false;
        // proximity fuse: senses through walls, line of sight irrelevant
        if (entity.Pos.DistanceTo(target.Pos.XYZ) > triggerRange) return false;
        if (!CanSense(target, triggerRange)) return false;

        targetEntity = target;
        Arm();
        return true;
    }

    public override void StartExecute()
    {
        base.StartExecute();
        lastRetargetMs = 0;
    }

    public override bool ContinueExecute(float dt)
    {
        base.ContinueExecute(dt); // keeps the repeating shriek going; ignore its timeout
        if (!entity.Alive) return false;

        fuseAccumMs += dt * 1000f;
        if (fuseAccumMs >= fuseMs)
        {
            Detonate();
            return false;
        }

        long now = world.ElapsedMilliseconds;
        if (now - lastRetargetMs > 500)
        {
            lastRetargetMs = now;
            IPlayer? plr = world.NearestPlayer(entity.Pos.X, entity.Pos.Y, entity.Pos.Z);
            Entity? target = plr?.Entity;
            if (target != null && target.Alive)
            {
                targetEntity = target;
                pathTraverser.NavigateTo_Async(target.Pos.XYZ.Clone(), moveSpeed, 0.3f,
                    OnGoalReached, OnStuck, null, 2000, 1);
            }
        }

        return true;
    }

    public override void FinishExecute(bool cancelled)
    {
        base.FinishExecute(cancelled);
        pathTraverser.Stop();
    }

    private void Arm()
    {
        armed = true;
        fuseAccumMs = 0;
        entity.WatchedAttributes.SetBool("bloatArmed", true);
    }

    private void Detonate()
    {
        if (world is not IServerWorldAccessor sworld) return;

        // temporal rupture: drain stability on nearby players, strongest at the center
        Vec3d center = entity.Pos.XYZ;
        foreach (IPlayer plr in world.GetPlayersAround(center, (float)injureRadius * 2f, (float)injureRadius * 2f))
        {
            var beh = plr.Entity?.GetBehavior<EntityBehaviorTemporalStabilityAffected>();
            if (beh == null) continue;
            double dist = plr.Entity!.Pos.DistanceTo(center);
            double falloff = GameMath.Clamp(1 - dist / (injureRadius * 2), 0.25, 1);
            beh.OwnStability = Math.Max(0, beh.OwnStability - stabilityDrain * falloff);
        }

        world.PlaySoundAt(new AssetLocation("worldinterestingmobs", "sounds/creature/bloat/rupture"),
            center.X, center.Y, center.Z, null, randomizePitch: false, range: 48f);

        sworld.CreateExplosion(entity.Pos.AsBlockPos, EnumBlastType.EntityBlast,
            destructionRadius, injureRadius, 1f, null);

        entity.Die(EnumDespawnReason.Removed);
    }

    private void OnGoalReached() { }
    private void OnStuck() { }
}
