using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace Ehm93.VS.World.InterestingMobs;

/// <summary>
/// Shared sensing/terrain primitives for the gloam's AI tasks: the observed-check
/// (view cone + LOS), the light field it treats as a wall, and dark-spot sampling.
/// Light is measured as max(time-of-day sunlight, block light) PLUS an estimate of
/// light carried by the player — held torches emit client-side dynamic light, not
/// block light, so GetLightLevel alone can't see what the player sees by.
/// </summary>
public static class GloamSenses
{
    // on-screen test (~120 degree FOV) — same constant the bloat stalker uses
    public const double FrustumDot = 0.5;

    /// <summary>Within the observer's view cone AND unoccluded by terrain.</summary>
    public static bool Observed(IWorldAccessor world, Entity observer, Entity observed)
        => ViewAlignment(observer, observed) > FrustumDot
            && LosOpen(world, EyesOf(observer), CenterOf(observed));

    /// <summary>Cosine of the angle between the observer's view direction and the direction from their eyes to the observed entity.</summary>
    public static double ViewAlignment(Entity observer, Entity observed)
    {
        Vec3f view = observer.Pos.GetViewVector();
        Vec3d toThem = CenterOf(observed).Sub(EyesOf(observer)).Normalize();
        return view.X * toThem.X + view.Y * toThem.Y + view.Z * toThem.Z;
    }

    /// <summary>
    /// True when no terrain blocks the ray between the two points. Only
    /// collidable blocks count as cover — selection-box tracing would treat
    /// grass tufts and snow layers as walls.
    /// </summary>
    public static bool LosOpen(IWorldAccessor world, Vec3d from, Vec3d to)
    {
        BlockSelection? bSel = null;
        EntitySelection? eSel = null;
        world.RayTraceForSelection(from, to, ref bSel, ref eSel,
            (pos, block) => block?.CollisionBoxes != null && block.CollisionBoxes.Length > 0,
            (e) => false);
        return bSel == null;
    }

    /// <summary>
    /// Light as perceived at pos: block/sun light, plus a linear-falloff estimate
    /// of the carrier's held light (a lit torch/lantern in either hand) when a
    /// carrier is given. Held light is read from the hands, NOT Entity.LightHsv —
    /// that property is only ever set for entities on fire, so a torch-carrying
    /// player reports LightHsv null and would read as standing in the dark.
    /// </summary>
    public static int LightAt(IWorldAccessor world, BlockPos pos, Entity? carrier = null)
    {
        int light = world.BlockAccessor.GetLightLevel(pos, EnumLightLevelType.MaxTimeOfDayLight);
        int held = HeldLightLevel(world, carrier);
        if (held > 0)
        {
            int dist = (int)carrier!.Pos.DistanceTo(new Vec3d(pos.X + 0.5, pos.Y + 0.5, pos.Z + 0.5));
            light = Math.Max(light, held - dist);
        }
        return light;
    }

    /// <summary>Brightness (HSV value) of a light-emitting item held in either hand, else 0.</summary>
    public static int HeldLightLevel(IWorldAccessor world, Entity? carrier)
    {
        if (carrier is not EntityAgent agent) return 0;
        return Math.Max(SlotLight(world, agent.RightHandItemSlot), SlotLight(world, agent.LeftHandItemSlot));
    }

    private static int SlotLight(IWorldAccessor world, ItemSlot? slot)
    {
        ItemStack? stack = slot?.Itemstack;
        if (stack?.Collectible == null) return 0;
        byte[]? hsv = stack.Collectible.GetLightHsv(world.BlockAccessor, null, stack);
        return hsv != null && hsv.Length >= 3 ? hsv[2] : 0;
    }

    /// <summary>
    /// Sample escape points biased away from the threat, strongly preferring
    /// darkness but accepting a lit spot over standing still in the light.
    /// </summary>
    public static Vec3d? PickRetreatSpot(IWorldAccessor world, Random rand, Entity self, Entity threat, int lightThreshold, int samples = 16)
    {
        Vec3d away = self.Pos.XYZ.SubCopy(threat.Pos.XYZ).Normalize();
        double awayAng = Math.Atan2(away.X, away.Z);
        Vec3d? best = null;
        double bestScore = double.MinValue;

        for (int i = 0; i < samples; i++)
        {
            double ang = awayAng + (rand.NextDouble() - 0.5) * GameMath.PI; // ±90° of dead away
            double rad = 6 + rand.NextDouble() * 8;
            double x = self.Pos.X + Math.Sin(ang) * rad;
            double z = self.Pos.Z + Math.Cos(ang) * rad;

            if (!TryFindGround(world, self.Pos.Dimension, x, (int)self.Pos.Y, z, out int groundY)) continue;

            int light = LightAt(world, new BlockPos((int)x, groundY, (int)z, self.Pos.Dimension), threat);
            double score = (lightThreshold - light) * 0.5;
            score += new Vec3d(x, groundY, z).DistanceTo(threat.Pos.XYZ) * 0.1;

            if (score > bestScore)
            {
                bestScore = score;
                best = new Vec3d(x, groundY, z);
            }
        }

        return best;
    }

    /// <summary>Scan a small column near refY for standable ground (solid top below, passable space at/above).</summary>
    public static bool TryFindGround(IWorldAccessor world, int dim, double x, int refY, double z, out int groundY)
    {
        var ba = world.BlockAccessor;
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

    public static Vec3d EyesOf(Entity e) => e.Pos.XYZ.AddCopy(e.LocalEyePos);

    public static Vec3d CenterOf(Entity e) => e.Pos.XYZ.AddCopy(0, e.SelectionBox.Y2 / 2f, 0);
}
