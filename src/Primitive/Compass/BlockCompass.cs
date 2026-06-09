using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Ehm93.VS.Primitive.Compass;

// The held compass renders like a vanilla bowl of water with a magnetised needle floating in it — the
// bowl stays still and only the needle turns to point north.
//
// This is a BLOCK (not an item) on purpose: the vanilla bowl is a block, and only the block inventory-
// mesh path renders a multi-texture shape (bowl clay + blue water + iron needle) correctly in the slot
// icon. The cached ITEM inventory mesh only rendered the primary texture, so an item compass showed an
// empty brown bowl in the hotbar. As a block it inherits the block-default isometric gui rotation too.
//
// We can't rotate one part of a model via OnBeforeRender's single Transform, so we build the mesh
// ourselves: the bowl is the actual vanilla bowl shape (game:block/clay/bowl-empty-ground) plus a static
// water surface; the needle is a separate mesh. Each frame we combine bowl+water with the needle rotated
// to the current heading and hand it to the renderer via renderinfo.ModelRef. The block's JSON transforms
// are copied from the vanilla bowl so it sits in the hand identically.
//
// North is -Z in VS; the player faces north at cameraYaw ≈ PI (see BlockFacing). The needle is rigidly
// attached to the bowl, which is attached to the player's view, so to keep it aimed at a FIXED world
// direction we counter-rotate by the camera yaw. The exact sign/offset depend on the in-hand transform,
// so they're live-tunable via the /compass client command.
public class BlockCompass : Block
{
    /// Flip if the needle turns WITH you instead of staying fixed in the world.
    public static float YawSign = -1f;

    /// Rotate the needle (degrees) until its tip points north. Tune with /compass offset.
    public static float YawOffsetDeg = 0f;

    /// How fast the needle eases toward north (per second; higher = snappier). Tune with /compass lerp.
    public static float LerpRate = 0.5f;

    /// Extra rotation (degrees) for the inventory-icon needle, on top of the world-north heading. Tune with /compass guioffset.
    public static float GuiYawOffsetDeg = 0f;

    private const string BowlShapePath = "game:shapes/block/clay/bowl-empty-ground.json";
    private const string WaterShapePath = "primitivecompass:shapes/item/compass-water.json";
    private const string NeedleShapePath = "primitivecompass:shapes/item/compass-needle.json";

    // TesselateShape yields mesh coords in 0..1 (16px = 1.0); the bowl and needle are both centred on
    // x=8,z=8, so the needle spins about (0.5, _, 0.5). Y is irrelevant for a yaw (Y-axis) rotation.
    private static readonly Vec3f NeedlePivot = new Vec3f(0.5f, 0f, 0.5f);

    private MeshData? bowlMesh;    // vanilla bowl + static water (never rotated)
    private MeshData? needleMesh;  // the needle, rotated per frame
    private MultiTextureMeshRef? guiRef;       // bowl + needle for gui / ground / handbook (tracks facing)
    private MultiTextureMeshRef? dynamicRef;   // bowl + needle at the live heading, for the hands
    private float currentHeadingDeg = float.NaN;    // displayed hand heading, eased toward the target each frame
    private float lastBuiltHeadingDeg = float.NaN;  // heading the current dynamicRef was built at
    private float currentGuiHeadingDeg = float.NaN; // displayed gui heading, eased toward the target each frame
    private float lastGuiHeadingDeg = float.NaN;    // heading the current guiRef was built at
    private long lastHandEaseMs;                    // ElapsedMilliseconds at the last hand-ease update
    private long lastGuiEaseMs;                     // ElapsedMilliseconds at the last gui-ease update

    public override void OnBeforeRender(ICoreClientAPI capi, ItemStack itemstack, EnumItemRenderTarget target, ref ItemRenderInfo renderinfo)
    {
        base.OnBeforeRender(capi, itemstack, target, ref renderinfo);
        if (!EnsureBaseMeshes(capi)) return;

        if (target == EnumItemRenderTarget.HandTp || target == EnumItemRenderTarget.HandTpOff)
        {
            if (capi.World?.Player is not IClientPlayer player) return;

            float targetHeadingDeg = YawSign * (player.CameraYaw * 180f / GameMath.PI) + YawOffsetDeg;

            // Ease toward the target instead of snapping, like a real needle settling. Frame-rate
            // independent exponential smoothing, always taking the shortest way around the circle.
            if (float.IsNaN(currentHeadingDeg))
            {
                currentHeadingDeg = targetHeadingDeg; // snap on the first frame so it doesn't wind up from 0
            }
            else
            {
                float dt = EaseDt(capi.ElapsedMilliseconds, ref lastHandEaseMs);
                currentHeadingDeg += SignedDelta(targetHeadingDeg, currentHeadingDeg) * (1f - (float)Math.Exp(-LerpRate * dt));
            }

            // Rebuild the GPU mesh only while the needle is actually moving; once settled this stops.
            if (dynamicRef == null || dynamicRef.Disposed || float.IsNaN(lastBuiltHeadingDeg) || Math.Abs(SignedDelta(currentHeadingDeg, lastBuiltHeadingDeg)) > 0.1f)
            {
                dynamicRef?.Dispose();
                dynamicRef = BuildCombined(capi, currentHeadingDeg);
                lastBuiltHeadingDeg = currentHeadingDeg;
            }
            if (dynamicRef != null) renderinfo.ModelRef = dynamicRef;
        }
        else
        {
            // GUI icon / dropped item / handbook: a live mini-compass that tracks the player's facing and
            // EASES toward it exactly like the in-hand needle (same LerpRate), plus a gui-specific offset.
            float guiTargetDeg = 0f;
            if (capi.World?.Player is IClientPlayer guiPlayer)
            {
                // NOTE the NEGATED sign vs the hand: the block GUI render adds +180° to the model's X
                // rotation (RenderItemstackToGui), which mirrors the icon and reverses the apparent spin
                // direction. Negating the camera-yaw term un-mirrors it so the icon turns like the hand.
                guiTargetDeg = -YawSign * (guiPlayer.CameraYaw * 180f / GameMath.PI) + GuiYawOffsetDeg;
            }

            if (float.IsNaN(currentGuiHeadingDeg))
            {
                currentGuiHeadingDeg = guiTargetDeg;
            }
            else
            {
                float dt = EaseDt(capi.ElapsedMilliseconds, ref lastGuiEaseMs);
                currentGuiHeadingDeg += SignedDelta(guiTargetDeg, currentGuiHeadingDeg) * (1f - (float)Math.Exp(-LerpRate * dt));
            }

            if (guiRef == null || guiRef.Disposed || float.IsNaN(lastGuiHeadingDeg) || Math.Abs(SignedDelta(currentGuiHeadingDeg, lastGuiHeadingDeg)) > 0.1f)
            {
                guiRef?.Dispose();
                guiRef = BuildCombined(capi, currentGuiHeadingDeg);
                lastGuiHeadingDeg = currentGuiHeadingDeg;
            }
            if (guiRef != null) renderinfo.ModelRef = guiRef;
        }
    }

    private bool EnsureBaseMeshes(ICoreClientAPI capi)
    {
        if (bowlMesh != null && needleMesh != null) return true;

        Shape? bowlShape = capi.Assets.TryGet(new AssetLocation(BowlShapePath))?.ToObject<Shape>();
        Shape? waterShape = capi.Assets.TryGet(new AssetLocation(WaterShapePath))?.ToObject<Shape>();
        Shape? needleShape = capi.Assets.TryGet(new AssetLocation(NeedleShapePath))?.ToObject<Shape>();
        if (bowlShape == null || waterShape == null || needleShape == null) return false;

        // Tesselated with `this` (the block) as the texture source, so the bowl shape's #mat resolves to
        // our hardened clay colour and #water / #metal to our textures.
        capi.Tesselator.TesselateShape(this, bowlShape, out bowlMesh);
        capi.Tesselator.TesselateShape(this, waterShape, out MeshData? waterMesh);
        if (bowlMesh != null && waterMesh != null) bowlMesh.AddMeshData(waterMesh);

        capi.Tesselator.TesselateShape(this, needleShape, out needleMesh);

        return bowlMesh != null && needleMesh != null;
    }

    private MultiTextureMeshRef BuildCombined(ICoreClientAPI capi, float headingDeg)
    {
        MeshData combined = bowlMesh!.Clone();
        MeshData needle = needleMesh!.Clone();
        needle.Rotate(NeedlePivot, 0f, headingDeg * GameMath.PI / 180f, 0f);
        combined.AddMeshData(needle);
        return capi.Render.UploadMultiTextureMesh(combined);
    }

    // Frame delta (seconds) from a shared clock (capi.ElapsedMilliseconds), so the hand and gui eases
    // advance at the SAME speed regardless of what dt each render path puts in renderinfo.dt (the gui
    // path passes 0). Each ease tracks its own last-update time, so each gets ~one real frame's delta.
    private static float EaseDt(long nowMs, ref long lastMs)
    {
        float dt = lastMs == 0L ? 1f / 60f : (nowMs - lastMs) / 1000f;
        lastMs = nowMs;
        return Math.Clamp(dt, 0f, 0.1f);
    }

    // Shortest signed angular difference a-b (degrees), wrapped to [-180, 180].
    private static float SignedDelta(float a, float b)
    {
        float d = a - b;
        d -= 360f * (float)Math.Round(d / 360f);
        return d;
    }

    public override void OnUnloaded(ICoreAPI api)
    {
        guiRef?.Dispose();
        guiRef = null;
        dynamicRef?.Dispose();
        dynamicRef = null;
        base.OnUnloaded(api);
    }
}
