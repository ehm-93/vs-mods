using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Ehm93.VS.World.InterestingMobs;

/// <summary>
/// Client-side cosmetic head tracking: eases the wader's craned neck + hanging head toward
/// the nearest living creature by scrubbing two permanently-active, zero-speed "driver"
/// animations (headtrackyaw frame = 175 + yawDeg, headtrackpitch frame = 25 + pitchDeg).
/// This is the vanilla EntityBoat-weathervane pattern. CRITICAL ENGINE QUIRKS (verified by
/// decompile): at AnimationSpeed==0 the engine neither ramps EasingFactor (Progress()
/// early-returns) nor computes BlendedWeight (CalcBlendedWeight is gated on speed != 0) —
/// both stay 0 and every keyframe contribution is multiplied by them, so WE must write
/// BlendedWeight/EasingFactor = 1 every tick. ClientSide=true exempts the driver anims
/// from the OnReceivedServerAnimations purge that removes client-started animations
/// whenever the server AI starts/stops an animation.
/// </summary>
public class EntityBehaviorGloamHeadTracking : EntityBehavior
{
    float searchRange;
    float yawClampRad;
    float pitchClampRad;
    float easePerSec;
    float eyeHeight;
    bool debug;

    Entity? target;
    float sinceSearchSec = 1000;
    float sinceDebugSec;
    float curYaw;
    float curPitch;
    float heldYaw;
    float heldPitch;

    static readonly AnimationMetaData YawAnim = new()
    {
        Animation = "headtrackyaw",
        Code = "headtrackyaw",
        AnimationSpeed = 0f,
        BlendMode = EnumAnimationBlendMode.Add,
        Weight = 1f,
        ClientSide = true,
    };
    static readonly AnimationMetaData PitchAnim = new()
    {
        Animation = "headtrackpitch",
        Code = "headtrackpitch",
        AnimationSpeed = 0f,
        BlendMode = EnumAnimationBlendMode.Add,
        Weight = 1f,
        ClientSide = true,
    };

    public EntityBehaviorGloamHeadTracking(Entity entity) : base(entity) { }

    public override string PropertyName() => "gloamheadtracking";

    public override void Initialize(EntityProperties properties, JsonObject attributes)
    {
        searchRange = attributes["searchRange"].AsFloat(12);
        yawClampRad = attributes["yawClampDeg"].AsFloat(110) * GameMath.DEG2RAD;
        pitchClampRad = attributes["pitchClampDeg"].AsFloat(25) * GameMath.DEG2RAD;
        easePerSec = attributes["easePerSec"].AsFloat(4);
        eyeHeight = (float)properties.EyeHeight;
        debug = attributes["debug"].AsBool(false);
        if (debug) entity.World.Logger.Notification("[gloamheadtracking] initialized, side={0}", entity.World.Side);
    }

    public override void OnGameTick(float dt)
    {
        if (entity.World.Side != EnumAppSide.Client) return;
        if (entity is not EntityAgent) return;

        IAnimator? animator = entity.AnimManager?.Animator;
        if (animator == null) return;

        // keep the frozen driver animations running (self-heals if the engine drops them)
        var anim = entity.AnimManager!;
        if (!anim.IsAnimationActive("headtrackyaw")) anim.StartAnimation(YawAnim.Clone());
        if (!anim.IsAnimationActive("headtrackpitch")) anim.StartAnimation(PitchAnim.Clone());

        // hand the neck back to the deliberate poses during the stomp/sprint — the
        // additive head-track would fight the reared/speared neck those animations set.
        // ease to neutral (curYaw/Pitch → 0 = the driver's no-offset frame) fast so the
        // pose wins immediately; tracking resumes smoothly when they end.
        bool suppressed = anim.IsAnimationActive("stomp") || anim.IsAnimationActive("sprint");

        float desiredYaw = 0;
        float desiredPitch = 0;

        if (entity.Alive && !suppressed)
        {
            sinceSearchSec += dt;
            if (sinceSearchSec > 0.25f)
            {
                sinceSearchSec = 0;
                Entity? found = entity.World.GetNearestEntity(
                    entity.Pos.XYZ, searchRange, searchRange,
                    e => e != entity && e.Alive && e is EntityAgent
                );
                if (found != null) target = found;
                else if (target != null && (!target.Alive
                    || target.Pos.XYZ.SquareDistanceTo(entity.Pos.XYZ) > searchRange * searchRange * 1.5))
                {
                    target = null;
                }
            }

            if (target != null && target.Alive)
            {
                double dx = target.Pos.X - entity.Pos.X;
                double dz = target.Pos.Z - entity.Pos.Z;
                float targetYaw = (float)Math.Atan2(dx, dz);
                // creatures render at Pos.Yaw — BodyYaw is never maintained client-side
                float relYaw = GameMath.AngleRadDistance(entity.Pos.Yaw, targetYaw);

                if (Math.Abs(relYaw) <= yawClampRad)
                {
                    heldYaw = relYaw;
                    double dy = (target.Pos.Y + target.LocalEyePos.Y) - (entity.Pos.Y + eyeHeight);
                    double horDist = Math.Sqrt(dx * dx + dz * dz);
                    heldPitch = GameMath.Clamp(
                        -(float)Math.Atan2(dy, Math.Max(0.5, horDist)),
                        -pitchClampRad, pitchClampRad
                    );
                }
                // target inside the blind cone behind: hold the strain at the last
                // trackable angle (no side-flip whip while it hovers at dead-180)
                desiredYaw = heldYaw;
                desiredPitch = heldPitch;
            }
            else
            {
                heldYaw = 0;
                heldPitch = 0;
            }
        }
        else if (suppressed)
        {
            // drop any held strain so the neck returns cleanly to the pose's neutral
            heldYaw = 0;
            heldPitch = 0;
        }

        // ease out ~3x faster when suppressed so the stomp/sprint neck reads immediately
        float t = Math.Min(1, (suppressed ? easePerSec * 3f : easePerSec) * dt);
        // LINEAR ease, not shortest-angular-path: the neck cannot rotate through its own
        // back — when the target crosses behind, it must unwind the long way around the
        // front. Shortest-path easing winds curYaw past the clamp and pins the head.
        curYaw += (desiredYaw - curYaw) * t;
        curPitch += (desiredPitch - curPitch) * t;

        RunningAnimation? yawState = animator.GetAnimationState("headtrackyaw");
        RunningAnimation? pitchState = animator.GetAnimationState("headtrackpitch");
        if (yawState != null)
        {
            yawState.CurrentFrame = GameMath.Clamp(175f + curYaw * GameMath.RAD2DEG, 0f, 350f);
            // at speed 0 the engine leaves both at 0 (= zero contribution) and never
            // overwrites our values; rewrite every tick — re-tesselation resets them
            yawState.BlendedWeight = 1f;
            yawState.EasingFactor = 1f;
        }
        if (pitchState != null)
        {
            pitchState.CurrentFrame = GameMath.Clamp(25f + curPitch * GameMath.RAD2DEG, 0f, 50f);
            pitchState.BlendedWeight = 1f;
            pitchState.EasingFactor = 1f;
        }

        if (debug && (sinceDebugSec += dt) > 2)
        {
            sinceDebugSec = 0;
            entity.World.Logger.Notification(
                "[gloamheadtracking] target={0} yawDeg={1:0.0} posYawDeg={2:0.0} yawFrame={3:0.0} pitchFrame={4:0.0} anims={5}",
                target?.Code.ToString() ?? "none", curYaw * GameMath.RAD2DEG, entity.Pos.Yaw * GameMath.RAD2DEG,
                yawState?.CurrentFrame ?? -1, pitchState?.CurrentFrame ?? -1,
                string.Join(",", anim.ActiveAnimationsByAnimCode.Keys)
            );
        }
    }
}
