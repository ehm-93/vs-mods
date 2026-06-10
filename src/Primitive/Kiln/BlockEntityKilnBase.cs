using System;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace Ehm93.VS.Primitive.Kiln;

// The updraft kiln: a permanent clay-firing structure built on one (1x1) or four (2x2) kiln-base blocks.
// Bloomery-inspired flow: place base(s) -> right-click with fireclay bricks to build the shell (a ghost
// outline shows the finished volume) -> load raw clayware through the open door -> seal the door with more
// bricks -> right-click with fuel -> hold a torch to light -> cook, then cool -> open the door, take the
// fired ware.
//
// One base is the CONTROLLER: it owns the inventory, fuel pool, state machine and shell mesh. For a 2x2
// kiln the other three bases are members that just forward everything here. Extends BlockEntityDisplay so
// the loaded ware renders at its shelf positions (the controller renders all of it; members display none).
public class BlockEntityKilnBase : BlockEntityDisplay
{
    private const int MaxSlots = 32;       // fixed display-array size; actual capacity is per footprint

    public enum KilnState { Construction = 0, Open = 1, Sealed = 2, Lit = 3, Cooling = 4, Cooled = 5 }

    private readonly InventoryGeneric inv;

    // Member role: position of the controller this base belongs to (null = standalone or controller itself).
    private BlockPos? controllerPos;

    // Controller state.
    private bool formed;                   // a kiln (small or large) has been started here
    private bool isLarge;
    private int doorFacingIndex = BlockFacing.NORTH.Index;
    private KilnState state = KilnState.Construction;
    private int tiersBuilt;                // construction tiers completed; each tier is one click of bricks
    private int doorBricks;
    private float fuelSeconds;
    private readonly Dictionary<string, int> fuelItems = new();
    private double cookFinishHours;
    private double coolFinishHours;

    private long tickListenerId;
    private long clientTickId;
    private MeshData? shellMesh;

    // Flames lick out of the stoke arch while firing; smoke rises from the chimney while firing and cooling.
    private static readonly SimpleParticleProperties FlameParticles = new(
        1, 2, ColorUtil.ToRgba(255, 255, 215, 130),
        new Vec3d(), new Vec3d(),
        new Vec3f(-0.05f, 0.15f, -0.05f), new Vec3f(0.05f, 0.4f, 0.05f),
        0.6f, 0f, 0.25f, 0.5f, EnumParticleModel.Quad)
    {
        VertexFlags = 128, // glow
        OpacityEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, -255f),
        SizeEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, -0.3f),
        AddPos = new Vec3d(0.2, 0.2, 0.2),
    };

    private static readonly SimpleParticleProperties KilnSmoke = new(
        1, 1, ColorUtil.ToRgba(150, 85, 85, 85),
        new Vec3d(), new Vec3d(),
        new Vec3f(-0.05f, 0.25f, -0.05f), new Vec3f(0.05f, 0.6f, 0.05f),
        3f, -0.02f, 0.5f, 1f, EnumParticleModel.Quad)
    {
        SelfPropelled = true,
        AddPos = new Vec3d(0.3, 0, 0.3),
        OpacityEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, -160f),
        SizeEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, 1.2f),
    };

    public BlockEntityKilnBase()
    {
        inv = new InventoryGeneric(MaxSlots, null, null);
    }

    public override InventoryBase Inventory => inv;
    public override string InventoryClassName => "primitivekiln";

    private KilnConfig Config => Api.ModLoader.GetModSystem<KilnModSystem>().Config;

    private bool IsController => formed && controllerPos == null;
    private bool IsMember => controllerPos != null;

    private int TierCount => isLarge ? 5 : 4;                 // matches the TierN element groups in the shapes
    private int TierCost => Math.Max(1, (isLarge ? Config.KilnBricksLarge : Config.KilnBricksSmall) / TierCount);
    private int DoorBricksNeeded => isLarge ? Config.KilnDoorBricksLarge : Config.KilnDoorBricksSmall;
    private float FuelSecondsNeeded => isLarge ? Config.KilnFuelSecondsLarge : Config.KilnFuelSecondsSmall;
    private int Capacity => isLarge ? 8 : 1;   // one full-size piece per cell per layer (stacking ware piles up within it)
    private BlockFacing DoorFacing => BlockFacing.ALLFACES[doorFacingIndex];

    // The whole kiln's state, member bases included — used by BlockKilnBase.GetLightHsv so every base block
    // of a burning kiln emits light. Falls back to the local state when the controller isn't reachable.
    public KilnState StateForLight
    {
        get
        {
            if (controllerPos == null || Api == null) return state;
            return (Api.World.BlockAccessor.GetBlockEntity(controllerPos) as BlockEntityKilnBase)?.state ?? state;
        }
    }

    // Members display nothing; the controller renders every loaded piece. Always the full slot count (not
    // Capacity): base.OnTesselation indexes tfMatrices[i] for i < DisplayedItems, so this and the matrix
    // array length must never disagree mid-sync or tesselation dies with an index error partway through
    // (symptom: only the first N pieces render). Capacity is enforced at insertion instead.
    public override int DisplayedItems => IsController ? MaxSlots : 0;

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        if (IsController && (state == KilnState.Lit || state == KilnState.Cooling) && api.Side == EnumAppSide.Server)
            RegisterTick();
        // Self-heal: kilns built before fillers existed (or with cells cleared by world edits) regain their
        // collision volume on load. Idempotent — occupied cells, including existing fillers, are skipped.
        if (IsController && state != KilnState.Construction && api.Side == EnumAppSide.Server)
            PlaceFillers();
        if (api.Side == EnumAppSide.Client) UpdateClientTick();
    }

    private BlockEntityKilnBase? Controller()
    {
        if (controllerPos == null) return this;
        return Api.World.BlockAccessor.GetBlockEntity(controllerPos) as BlockEntityKilnBase;
    }

    // ---------------- interaction (called from BlockKilnBase) ----------------

    // Returns true when the click was consumed.
    public bool OnInteract(IPlayer player)
    {
        BlockEntityKilnBase? ctrl = Controller();
        if (ctrl == null || ctrl == this) return HandleInteract(player);
        return ctrl.HandleInteract(player);
    }

    private bool HandleInteract(IPlayer player)
    {
        ItemSlot hand = player.InventoryManager.ActiveHotbarSlot;

        if (hand != null && !hand.Empty)
        {
            ItemStack stack = hand.Itemstack!;
            string code = stack.Collectible?.Code?.Path ?? "";

            if (code == "burnedbrick-fire") return HandleBrick(hand, player);
            // Let torches/firestarters fall through to the held CanIgnite flow (sneak+hold ignites).
            if (code.Contains("torch") || code.Contains("firestarter")) return false;
            if (BEBehaviorBrickClamp.IsFuel(stack)) return HandleFuel(hand, player);
            if (IsFireable(stack)) return HandleWare(hand, player);
            return false;
        }

        return HandleEmptyHand(player);
    }

    private bool HandleBrick(ItemSlot hand, IPlayer player)
    {
        if (Api.Side != EnumAppSide.Server) return true;

        if (!formed && !IsMember)
        {
            FormKiln();
            // Forming may have elected a different base (the 2x2 min corner) as controller and made this
            // one a member — the brick belongs on the controller.
            BlockEntityKilnBase? ctrl = Controller();
            if (ctrl != null && ctrl != this) return ctrl.HandleBrick(hand, player);
        }

        // One click builds one whole tier (or seals the whole door), consuming exactly the bricks it needs.
        if (state == KilnState.Construction)
        {
            if (hand.StackSize < TierCost && player.WorldData.CurrentGameMode != EnumGameMode.Creative)
            {
                Tell(player, "primitivekiln:kiln-err-needbricks", TierCost);
                return true;
            }
            tiersBuilt++;
            ConsumeFromHand(hand, player, TierCost);
            if (tiersBuilt >= TierCount)
            {
                state = KilnState.Open;
                PlaceFillers(); // the finished shell gets collision + selection via vanilla multiblock fillers
                Tell(player, "primitivekiln:kiln-built");
            }
            PlayBrickSound();
            SyncAndRedraw();
            return true;
        }

        if (state == KilnState.Open)
        {
            if (hand.StackSize < DoorBricksNeeded && player.WorldData.CurrentGameMode != EnumGameMode.Creative)
            {
                Tell(player, "primitivekiln:kiln-err-needbricks", DoorBricksNeeded);
                return true;
            }
            doorBricks = DoorBricksNeeded;
            ConsumeFromHand(hand, player, DoorBricksNeeded);
            state = KilnState.Sealed;
            Tell(player, "primitivekiln:kiln-sealed-msg");
            PlayBrickSound();
            SyncAndRedraw();
            return true;
        }

        Tell(player, "primitivekiln:kiln-err-sealed");
        return true;
    }

    private bool HandleFuel(ItemSlot hand, IPlayer player)
    {
        if (Api.Side != EnumAppSide.Server) return true;

        if (state != KilnState.Sealed)
        {
            Tell(player, state == KilnState.Construction || state == KilnState.Open
                ? "primitivekiln:kiln-err-sealfirst" : "primitivekiln:kiln-err-hot");
            return true;
        }

        float deficit = FuelSecondsNeeded - fuelSeconds;
        if (deficit <= 0)
        {
            Tell(player, "primitivekiln:kiln-fueled", (int)fuelSeconds, (int)FuelSecondsNeeded);
            return true;
        }

        float per = BEBehaviorBrickClamp.FuelSecondsOf(hand.Itemstack);
        int batch = player.Entity.Controls.CtrlKey ? 4 : 1;
        int needed = (int)Math.Ceiling(deficit / per);
        int take = Math.Min(batch, Math.Min(needed, hand.StackSize));
        if (take <= 0) return true;

        fuelSeconds += take * per;
        string itemCode = hand.Itemstack!.Collectible.Code.ToShortString();
        fuelItems.TryGetValue(itemCode, out int already);
        fuelItems[itemCode] = already + take;

        AssetLocation sound = BEBehaviorBrickClamp.FuelPlaceSound(hand.Itemstack);
        ConsumeFromHand(hand, player, take);
        Api.World.PlaySoundAt(sound, Pos, 0);
        Tell(player, "primitivekiln:kiln-fuelloaded", (int)fuelSeconds, (int)FuelSecondsNeeded);
        SyncAndRedraw();
        return true;
    }

    private bool HandleWare(ItemSlot hand, IPlayer player)
    {
        if (Api.Side != EnumAppSide.Server) return true;

        if (state == KilnState.Construction) { Tell(player, "primitivekiln:kiln-err-buildfirst"); return true; }
        if (state != KilnState.Open) { Tell(player, "primitivekiln:kiln-err-sealed"); return true; }

        if (IsTooLarge(hand.Itemstack!))
        {
            Tell(player, "primitivekiln:kiln-err-toobig");
            return true;
        }

        int batch = player.Entity.Controls.CtrlKey ? 4 : 1;
        int perSlot = StackPerSlot(hand.Itemstack!);
        bool added = false;
        for (int n = 0; n < batch; n++)
        {
            if (hand.Empty) break;
            int slot = SlotFor(hand.Itemstack!, perSlot);
            if (slot < 0) { if (!added) Tell(player, "primitivekiln:kiln-err-full"); break; }
            if (hand.TryPutInto(Api.World, inv[slot], 1) <= 0) break;
            added = true;
        }

        if (added)
        {
            hand.MarkDirty();
            Api.World.PlaySoundAt(new AssetLocation("game", "sounds/block/ceramicplace"), Pos, 0);
            SyncAndRedraw();
        }
        return true;
    }

    private bool HandleEmptyHand(IPlayer player)
    {
        if (Api.Side != EnumAppSide.Server) return state != KilnState.Construction;

        switch (state)
        {
            case KilnState.Open:
                int slot = LastUsedSlot();
                if (slot < 0) return false;
                ItemStack stack = inv[slot].TakeOutWhole();
                if (!player.InventoryManager.TryGiveItemstack(stack))
                    Api.World.SpawnItemEntity(stack, Pos.ToVec3d().Add(0.5, 0.7, 0.5));
                inv[slot].MarkDirty();
                SyncAndRedraw();
                return true;

            case KilnState.Sealed:
                // Tearing the door open before lighting: door bricks and fuel come back.
                RefundDoorBricks();
                RefundFuel();
                state = KilnState.Open;
                PlayBrickSound();
                SyncAndRedraw();
                return true;

            case KilnState.Cooled:
                RefundDoorBricks();
                state = KilnState.Open;
                PlayBrickSound();
                Tell(player, "primitivekiln:kiln-opened");
                SyncAndRedraw();
                return true;

            case KilnState.Lit:
            case KilnState.Cooling:
                Tell(player, "primitivekiln:kiln-err-hot");
                return true;

            default:
                return false;
        }
    }

    // ---------------- forming (1x1 vs 2x2 detection) ----------------

    // Called on the first brick. Looks for a 2x2 square of free kiln bases containing this one; if found the
    // square's min corner becomes the controller, otherwise this base alone forms a small kiln. The door faces
    // the way the clicked base does (HorizontalOrientable pointed it at the player on placement).
    private void FormKiln()
    {
        // HorizontalOrientable points the variant the way the PLAYER faces (away from them) — the door
        // should face TOWARD the builder, so flip it.
        BlockFacing facing = (BlockFacing.FromCode(Block.Variant["side"]) ?? BlockFacing.NORTH).Opposite;

        foreach ((int mx, int mz) in new[] { (0, 0), (-1, 0), (0, -1), (-1, -1) })
        {
            BlockPos mc = Pos.AddCopy(mx, 0, mz);
            BlockPos[] cells = { mc, mc.AddCopy(1, 0, 0), mc.AddCopy(0, 0, 1), mc.AddCopy(1, 0, 1) };

            bool allFree = true;
            var bes = new BlockEntityKilnBase?[4];
            for (int i = 0; i < 4; i++)
            {
                bes[i] = Api.World.BlockAccessor.GetBlockEntity(cells[i]) as BlockEntityKilnBase;
                // A base that was clicked once on its own (formed small, zero progress) is still free —
                // otherwise one stray click would permanently block 2x2 detection.
                bool free = bes[i] != null && !bes[i]!.IsMember
                    && (!bes[i]!.formed || (bes[i]!.tiersBuilt == 0 && bes[i]!.state == KilnState.Construction));
                if (!free) { allFree = false; break; }
            }
            if (!allFree) continue;

            BlockEntityKilnBase ctrl = bes[0]!;
            ctrl.formed = true;
            ctrl.isLarge = true;
            ctrl.doorFacingIndex = facing.Index;
            for (int i = 1; i < 4; i++)
            {
                bes[i]!.controllerPos = mc.Copy();
                bes[i]!.MarkDirty(true);
            }
            ctrl.MarkDirty(true);
            return;
        }

        formed = true;
        isLarge = false;
        doorFacingIndex = facing.Index;
        MarkDirty(true);
    }

    // ---------------- firing ----------------

    public bool CanIgnite() =>
        IsController ? state == KilnState.Sealed && fuelSeconds >= FuelSecondsNeeded
                     : Controller()?.CanIgnite() == true;

    public bool TryIgnite()
    {
        BlockEntityKilnBase? ctrl = Controller();
        if (ctrl != null && ctrl != this) return ctrl.TryIgnite();
        if (!CanIgnite()) return false;

        state = KilnState.Lit;
        cookFinishHours = Api.World.Calendar.TotalHours + Config.KilnCookHours;
        RegisterTick();
        SyncAndRedraw();
        return true;
    }

    private void OnServerTick(float dt)
    {
        double now = Api.World.Calendar.TotalHours;

        if (state == KilnState.Lit && now >= cookFinishHours)
        {
            ConvertWare();
            state = KilnState.Cooling;
            coolFinishHours = now + Config.KilnCoolHours;
            SyncAndRedraw();
        }
        else if (state == KilnState.Cooling && now >= coolFinishHours)
        {
            state = KilnState.Cooled;
            fuelSeconds = 0;
            fuelItems.Clear(); // spent
            UnregisterTick();
            SyncAndRedraw();
        }
    }

    // Pit-kiln style conversion: each slot's stack becomes its smelted result, no loss.
    private void ConvertWare()
    {
        for (int i = 0; i < Capacity; i++)
        {
            ItemStack? stack = inv[i].Itemstack;
            if (stack == null) continue;

            CombustibleProperties? cp = stack.Collectible.GetCombustibleProperties(Api.World, stack, null);
            ItemStack? smelted = cp?.SmeltedStack?.ResolvedItemstack;
            if (smelted == null) continue;

            ItemStack outStack = smelted.Clone();
            outStack.StackSize = Math.Max(1, stack.StackSize / Math.Max(1, cp!.SmeltedRatio));
            outStack.Collectible.SetTemperature(Api.World, outStack, 600f);
            inv[i].Itemstack = outStack;
            inv[i].MarkDirty();
        }
    }

    private static bool IsFireable(ItemStack stack)
    {
        CombustibleProperties? cp = stack.Collectible?.CombustibleProps;
        return cp != null && cp.SmeltingType == EnumSmeltType.Fire && cp.SmeltedStack != null;
    }

    // Full-block-sized clayware doesn't fit on the kiln's ~14-voxel tiers. Code-substring deny-list — extend
    // as oversized ware turns up in testing.
    private static readonly string[] TooLargeCodes = { "storagevessel", "clayplanter" };

    private static bool IsTooLarge(ItemStack stack)
    {
        string code = stack.Collectible?.Code?.Path ?? "";
        foreach (string c in TooLargeCodes) if (code.Contains(c)) return true;
        return false;
    }

    // ---------------- refunds / deconstruction ----------------

    private void RefundDoorBricks()
    {
        SpawnBricks(doorBricks);
        doorBricks = 0;
    }

    private void RefundFuel()
    {
        foreach (KeyValuePair<string, int> kv in fuelItems)
        {
            var loc = new AssetLocation(kv.Key);
            CollectibleObject? col = (CollectibleObject?)Api.World.GetItem(loc) ?? Api.World.GetBlock(loc);
            if (col == null) continue;
            int remaining = kv.Value;
            while (remaining > 0)
            {
                int n = Math.Min(remaining, col.MaxStackSize);
                remaining -= n;
                Api.World.SpawnItemEntity(new ItemStack(col, n), Pos.ToVec3d().Add(0.5, 0.7, 0.5));
            }
        }
        fuelItems.Clear();
        fuelSeconds = 0;
    }

    private void SpawnBricks(int count)
    {
        Item? brick = Api.World.GetItem(new AssetLocation("game", "burnedbrick-fire"));
        if (brick == null || count <= 0) return;
        while (count > 0)
        {
            int n = Math.Min(count, brick.MaxStackSize);
            count -= n;
            Api.World.SpawnItemEntity(new ItemStack(brick, n), Pos.ToVec3d().Add(0.5, 0.7, 0.5));
        }
    }

    // Called when this base (controller or member) is broken. The whole kiln deconstructs: construction +
    // door bricks and unspent fuel are refunded at the controller, contents drop, and surviving bases revert
    // to standalone unbuilt bases.
    public void OnKilnBroken()
    {
        if (Api.Side != EnumAppSide.Server) return;

        BlockEntityKilnBase? ctrl = Controller();
        if (ctrl == null) return;
        if (ctrl != this) { ctrl.Deconstruct(); return; }
        Deconstruct();
    }

    // The kiln's body above the bases is filled with vanilla multiblock filler blocks once the shell is
    // complete — they provide full-cube collision and selection and forward interactions/breaking to the
    // controller. Coverage = the body (dy 1..2: walls + shoulder); the chimney neck above stays open air.
    private IEnumerable<BlockPos> FillerPositions(int maxDy = 2)
    {
        int span = isLarge ? 2 : 1;
        for (int dx = 0; dx < span; dx++)
            for (int dz = 0; dz < span; dz++)
                for (int dy = 1; dy <= maxDy; dy++)
                    yield return Pos.AddCopy(dx, dy, dz);
    }

    private void PlaceFillers()
    {
        if (Api.Side != EnumAppSide.Server) return;
        foreach (BlockPos p in FillerPositions())
        {
            Block existing = Api.World.BlockAccessor.GetBlock(p);
            if (existing.Id != 0 && existing.Replaceable < 6000) continue; // something's in the way: skip quietly

            int dx = p.X - Pos.X, dy = p.Y - Pos.Y, dz = p.Z - Pos.Z;
            string code = "multiblock-monolithic-" + OffsetPart(dx) + "-" + OffsetPart(dy) + "-" + OffsetPart(dz);
            Block? filler = Api.World.GetBlock(new AssetLocation("game", code));
            if (filler != null) Api.World.BlockAccessor.SetBlock(filler.Id, p);
        }
    }

    private void RemoveFillers()
    {
        // Scan one layer higher than we place, to clean up fillers from earlier versions.
        foreach (BlockPos p in FillerPositions(3))
        {
            Block b = Api.World.BlockAccessor.GetBlock(p);
            if (b.Code != null && b.Code.Path.StartsWith("multiblock-monolithic"))
                Api.World.BlockAccessor.SetBlock(0, p);
        }
    }

    private static string OffsetPart(int v) => v == 0 ? "0" : (v < 0 ? "n" + (-v) : "p" + v);

    private void Deconstruct()
    {
        if (!formed) return;

        RemoveFillers();
        SpawnBricks(tiersBuilt * TierCost + doorBricks);
        tiersBuilt = 0;
        doorBricks = 0;
        if (state != KilnState.Lit && state != KilnState.Cooling) RefundFuel();
        else { fuelItems.Clear(); fuelSeconds = 0; }

        inv.DropAll(Pos.ToVec3d().Add(0.5, 0.7, 0.5));

        if (isLarge)
        {
            foreach (BlockPos p in MemberPositions())
            {
                if (Api.World.BlockAccessor.GetBlockEntity(p) is BlockEntityKilnBase member && member != this)
                {
                    member.controllerPos = null;
                    member.formed = false;
                    member.MarkDirty(true);
                }
            }
        }

        formed = false;
        isLarge = false;
        state = KilnState.Construction;
        UnregisterTick();
        MarkDirty(true);
    }

    private IEnumerable<BlockPos> MemberPositions()
    {
        yield return Pos.AddCopy(1, 0, 0);
        yield return Pos.AddCopy(0, 0, 1);
        yield return Pos.AddCopy(1, 0, 1);
    }

    // ---------------- inventory helpers ----------------

    private int FirstFreeSlot()
    {
        for (int i = 0; i < Capacity; i++) if (inv[i].Empty) return i;
        return -1;
    }

    // Pit-kiln parity per placement spot: shaped pieces (bowls, pots, molds, …) group up to 4 identical
    // pieces (the pit kiln's quadrant layout); stacking-layout ware (rawbricks, shingles, tiles) holds up to
    // its maxFireable — the exact gate the pit kiln applies (bricks 12, shingles 48, tiles 32).
    private static int StackPerSlot(ItemStack stack)
    {
        GroundStorageProperties? props = StackingPropsOf(stack);
        if (props == null) return 4;
        return Math.Max(1, Math.Min(props.MaxFireable, props.StackingCapacity));
    }

    private static GroundStorageProperties? StackingPropsOf(ItemStack? stack)
    {
        GroundStorageProperties? p =
            stack?.Collectible?.GetCollectibleBehavior<CollectibleBehaviorGroundStorable>(true)?.StorageProps;
        return p != null && p.Layout == EnumGroundStorageLayout.Stacking ? p : null;
    }

    // A partially-filled spot of the same ware first, else a free spot.
    private int SlotFor(ItemStack stack, int perSlot)
    {
        if (perSlot > 1)
        {
            for (int i = 0; i < Capacity; i++)
            {
                ItemStack? cur = inv[i].Itemstack;
                if (cur != null && cur.StackSize < perSlot
                    && cur.Equals(Api.World, stack, GlobalConstants.IgnoredStackAttributes)) return i;
            }
        }
        return FirstFreeSlot();
    }

    private int LastUsedSlot()
    {
        for (int i = Capacity - 1; i >= 0; i--) if (!inv[i].Empty) return i;
        return -1;
    }

    private void ConsumeFromHand(ItemSlot hand, IPlayer player, int count)
    {
        if (player.WorldData.CurrentGameMode == EnumGameMode.Creative) return;
        hand.TakeOut(count);
        hand.MarkDirty();
    }

    private void PlayBrickSound() =>
        Api.World.PlaySoundAt(new AssetLocation("game", "sounds/block/ceramicplace"), Pos, 0);

    private static void Tell(IPlayer player, string langKey, params object[] args) =>
        (player as IServerPlayer)?.SendIngameError("primitivekiln", Lang.Get(langKey, args));

    private void SyncAndRedraw()
    {
        shellMesh = null;
        MarkDirty(true);
    }

    // ---------------- rendering ----------------

    // Shelf positions for the loaded ware, controller-relative, before door-facing rotation.
    protected override float[][] genTransformationMatrices()
    {
        var result = new float[DisplayedItems][];
        if (DisplayedItems == 0) return result;

        float yaw = DoorYawRad();
        float cx = isLarge ? 1f : 0.5f;   // footprint centre
        float cz = isLarge ? 1f : 0.5f;

        int n = 0;
        if (!isLarge)
        {
            // One full-size piece centred on the firing floor (top at y=16 voxels).
            result[n++] = PlacementMatrix(0.5f, 1f, 0.5f, cx, cz, yaw);
        }
        else
        {
            // One piece per cell: layer 1 on the firing floor (y=1.0), layer 2 on the shelf (34/16 = 2.125).
            foreach (float layerY in new[] { 1f, 2.125f })
                foreach (int cellX in new[] { 0, 1 })
                    foreach (int cellZ in new[] { 0, 1 })
                        result[n++] = PlacementMatrix(cellX + 0.5f, layerY, cellZ + 0.5f, cx, cz, yaw);
        }

        // Pad to the full array (only the first Capacity positions are ever filled; the rest must be valid).
        while (n < result.Length) result[n++] = PlacementMatrix(0.5f, 1f, 0.5f, cx, cz, yaw);
        return result;
    }

    private static float[] PlacementMatrix(float x, float y, float z, float cx, float cz, float yaw)
    {
        // The trailing -0.5 centres the mesh on (x,z): content meshes (raw clayware blocks, pile shapes)
        // span 0..1, so placing their ORIGIN at the spot renders them half a block off — outside the walls.
        return new Matrixf()
            .Translate(cx, 0f, cz)
            .RotateY(yaw)
            .Translate(x - cx - 0.5f, y, z - cz - 0.5f)
            .Values;
    }

    private float DoorYawRad()
    {
        // Shape's door faces north; same rotation mapping the bloomery uses for its variants.
        return DoorFacing.Index switch
        {
            0 => 0f,                        // north
            1 => GameMath.PI * 1.5f,        // east  (270°)
            2 => GameMath.PI,               // south (180°)
            3 => GameMath.PIHALF,           // west  (90°)
            _ => 0f,
        };
    }

    // ---------------- content meshes ----------------

    private const float PileScale = 1f;     // piles render full size — the walls sit outside the footprint
    private const float GroupScale = 1f;    // grouped pieces full size at ground-storage quadrant spacing

    // Set while tesselating a stacking pile so the texture indexer resolves the ware's StackingTextures
    // (raw vs fired brick textures live there, not in the shape).
    private GroundStorageProperties? nowPileProps;

    public override TextureAtlasPosition this[string textureCode]
    {
        get
        {
            if (nowPileProps?.StackingTextures != null
                && nowPileProps.StackingTextures.TryGetValue(textureCode, out AssetLocation? loc))
                return getOrCreateTexPos(loc);
            return base[textureCode];
        }
    }

    // Pile and group meshes change with the stack size, so the count is part of the cache key.
    protected override string getMeshCacheKey(ItemSlot slot)
    {
        GroundStorageProperties? props = StackingPropsOf(slot.Itemstack);
        if (props != null && slot.Itemstack != null)
        {
            int models = (int)Math.Ceiling((float)slot.Itemstack.StackSize / props.ItemsPerModel);
            return "kilnpile-" + models + "x" + base.getMeshCacheKey(slot);
        }
        int count = Math.Min(4, slot.Itemstack?.StackSize ?? 1);
        return "kilnware-" + count + "x" + base.getMeshCacheKey(slot);
    }

    // Stacking ware renders as its ground-storage pile model with as many elements as there are items —
    // 12 rawbricks look like a 12-brick pile (mirrors BlockEntityGroundStorage's stacking branch).
    protected override MeshData? getOrCreateMesh(ItemSlot slot, int index)
    {
        ItemStack? stack = slot.Itemstack;
        if (Api is not ICoreClientAPI capi || stack == null) return base.getOrCreateMesh(slot, index);

        string key = getMeshCacheKey(slot);
        if (MeshCache.TryGetValue(key, out MeshData? cached)) return cached;

        GroundStorageProperties? props = StackingPropsOf(stack);
        MeshData? mesh;
        if (props?.StackingModel != null)
        {
            AssetLocation shapePath = props.StackingModel.Clone().WithPathPrefixOnce("shapes/").WithPathAppendixOnce(".json");
            Shape? shape = Shape.TryGet(capi, shapePath);
            if (shape == null) return base.getOrCreateMesh(slot, index);

            nowPileProps = props;
            nowTesselatingShape = shape;
            nowTesselatingObj = stack.Collectible;
            int models = (int)Math.Ceiling((float)stack.StackSize / props.ItemsPerModel);
            capi.Tesselator.TesselateShape("kilnPile", shape, out mesh, this, null, 0, 0, 0, props.CuboidsPerModel * models);
            nowPileProps = null;
            mesh.Scale(new Vec3f(0.5f, 0f, 0.5f), PileScale, PileScale, PileScale);
        }
        else
        {
            // base caches under our key (it calls the virtual getMeshCacheKey), so reshaping in place is safe.
            mesh = base.getOrCreateMesh(slot, index);
            int count = Math.Min(4, stack.StackSize);
            if (mesh != null && count > 1)
            {
                // Group identical pieces in a pit-kiln-style quadrant arrangement within the spot.
                MeshData composite = mesh.Clone().Clear();
                (float x, float z)[] offs = { (-0.25f, -0.25f), (0.25f, -0.25f), (-0.25f, 0.25f), (0.25f, 0.25f) };
                for (int i = 0; i < count; i++)
                {
                    MeshData copy = mesh.Clone();
                    copy.Scale(new Vec3f(0.5f, 0f, 0.5f), GroupScale, GroupScale, GroupScale);
                    copy.Translate(offs[i].x, 0f, offs[i].z);
                    composite.AddMeshData(copy);
                }
                mesh = composite;
            }
        }

        if (mesh != null) MeshCache[key] = mesh;
        return mesh;
    }

    public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tesselator)
    {
        if (IsController && tiersBuilt > 0 && Api is ICoreClientAPI capi)
        {
            if (shellMesh == null) shellMesh = GenShellMesh(capi, tesselator);
            if (shellMesh != null) mesher.AddMeshData(shellMesh);
            AddFuelMesh(capi, mesher, tesselator);
        }
        return base.OnTesselation(mesher, tesselator);
    }

    // The fuel heap in the combustion chamber, visible through the open stoke arch — same complete-block-mesh
    // technique as the clamp (hand-built cubes emit nothing in the terrain pool). Ember texture while firing.
    private void AddFuelMesh(ICoreClientAPI capi, ITerrainMeshPool mesher, ITesselatorAPI tesselator)
    {
        if (fuelSeconds <= 0 || state == KilnState.Cooled) return;

        bool burning = state == KilnState.Lit || state == KilnState.Cooling;
        MeshData cube;
        if (burning)
        {
            // The vanilla ember block (game:ember, glowLevel 160) IS the look — tesselating it bakes its
            // texture and glow into the mesh. Dim the glow while cooling (low 8 bits of each vertex's flags).
            Block? ember = capi.World.GetBlock(new AssetLocation("game", "ember"));
            if (ember == null) return;
            tesselator.TesselateBlock(ember, out cube);
            if (state == KilnState.Cooling)
                for (int i = 0; i < cube.Flags.Length; i++) cube.Flags[i] = (cube.Flags[i] & ~0xFF) | 60;
        }
        else
        {
            AssetLocation loc = new("game", BEBehaviorBrickClamp.FuelTexturePath(FirstFuelCode()));
            TextureAtlasPosition? texPos = capi.BlockTextureAtlas[loc];
            if (texPos == null)
            {
                capi.BlockTextureAtlas.GetOrInsertTexture(loc, out int _, out TextureAtlasPosition inserted);
                texPos = inserted;
            }
            if (texPos == null) return;

            Block? baseBlock = capi.World.GetBlock(new AssetLocation("game", "rock-granite"));
            if (baseBlock == null) return;
            tesselator.TesselateBlock(baseBlock, out cube);
            cube.SetTexPos(texPos);
        }

        // Chamber spans y 4..14 voxels; the heap grows with the pooled fuel.
        float fill = GameMath.Clamp(fuelSeconds / FuelSecondsNeeded, 0.15f, 1f);
        float width = isLarge ? 1.6f : 0.65f;
        float height = fill * 0.55f;
        cube.Scale(new Vec3f(0.5f, 0f, 0.5f), width, height, width);
        cube.Translate(isLarge ? 0.5f : 0f, 0.26f, isLarge ? 0.5f : 0f);
        mesher.AddMeshData(cube);
    }

    private string FirstFuelCode()
    {
        foreach (string key in fuelItems.Keys) return key;
        return "";
    }

    private MeshData? GenShellMesh(ICoreClientAPI capi, ITesselatorAPI tesselator)
    {
        string shapePath = isLarge ? "primitivekiln:shapes/block/kiln/large.json" : "primitivekiln:shapes/block/kiln/small.json";
        Shape? shape = Shape.TryGet(capi, shapePath);
        if (shape == null) return null;

        int revealed = state != KilnState.Construction ? TierCount : Math.Min(tiersBuilt, TierCount);

        // Exact element names from the shape (selective tesselation matches names, not wildcards).
        var elements = new List<string>();
        foreach (ShapeElement el in shape.Elements)
        {
            string name = el.Name ?? "";
            if (name == "DoorSeal")
            {
                if (doorBricks >= DoorBricksNeeded) elements.Add(name);
                continue;
            }
            for (int s = 1; s <= revealed; s++)
            {
                if (name.StartsWith("Tier" + s + "-") || name == "Tier" + s) { elements.Add(name); break; }
            }
        }

        var texSource = new ShapeTextureSource(capi, shape, shapePath);
        tesselator.TesselateShape("primitivekiln-shell", shape, out MeshData mesh, texSource, null, 0, 0, 0, null, elements.ToArray());

        float cx = isLarge ? 1f : 0.5f;
        float cz = isLarge ? 1f : 0.5f;
        mesh.Rotate(new Vec3f(cx, 0f, cz), 0f, DoorYawRad(), 0f);
        return mesh;
    }

    // ---------------- client particles ----------------

    // 100ms client tick alive only while the controller is firing or cooling (driven off synced state).
    private void UpdateClientTick()
    {
        if (Api?.Side != EnumAppSide.Client) return;
        bool wanted = IsController && (state == KilnState.Lit || state == KilnState.Cooling);
        if (wanted && clientTickId == 0) clientTickId = RegisterGameTickListener(OnClientTick, 100);
        else if (!wanted && clientTickId != 0)
        {
            UnregisterGameTickListener(clientTickId);
            clientTickId = 0;
        }
    }

    private void OnClientTick(float dt)
    {
        Random rand = Api.World.Rand;
        float cx = isLarge ? 1f : 0.5f;
        float cz = isLarge ? 1f : 0.5f;
        double chimneyTop = isLarge ? 70 / 16.0 : 58 / 16.0;

        // Smoke from the chimney: thick while firing, thin while cooling.
        double smokeChance = state == KilnState.Lit ? 0.5 : 0.15;
        if (rand.NextDouble() < smokeChance)
        {
            KilnSmoke.MinPos.Set(Pos.X + cx - 0.15, Pos.Y + chimneyTop, Pos.Z + cz - 0.15);
            Api.World.SpawnParticles(KilnSmoke);
        }

        // Flames rise inside the chimney and just peek out of its mouth while firing.
        if (state == KilnState.Lit && rand.NextDouble() < 0.6)
        {
            FlameParticles.MinPos.Set(Pos.X + cx - 0.1, Pos.Y + chimneyTop - 0.15, Pos.Z + cz - 0.1);
            Api.World.SpawnParticles(FlameParticles);
        }
    }

    // ---------------- lifecycle / persistence ----------------

    private void RegisterTick()
    {
        if (tickListenerId == 0 && Api.Side == EnumAppSide.Server)
            tickListenerId = RegisterGameTickListener(OnServerTick, 1000);
    }

    private void UnregisterTick()
    {
        if (tickListenerId != 0)
        {
            UnregisterGameTickListener(tickListenerId);
            tickListenerId = 0;
        }
        if (clientTickId != 0)
        {
            UnregisterGameTickListener(clientTickId);
            clientTickId = 0;
        }
    }

    public override void OnBlockRemoved()
    {
        base.OnBlockRemoved();
        UnregisterTick();
    }

    public override void OnBlockUnloaded()
    {
        base.OnBlockUnloaded();
        UnregisterTick();
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetBool("kilnFormed", formed);
        tree.SetBool("kilnLarge", isLarge);
        tree.SetInt("kilnDoorFacing", doorFacingIndex);
        tree.SetInt("kilnState", (int)state);
        tree.SetInt("kilnTiers", tiersBuilt);
        tree.SetInt("kilnDoorBricks", doorBricks);
        tree.SetFloat("kilnFuel", fuelSeconds);
        tree.SetDouble("kilnCookFinish", cookFinishHours);
        tree.SetDouble("kilnCoolFinish", coolFinishHours);

        if (controllerPos != null)
        {
            tree.SetInt("kilnCtrlX", controllerPos.X);
            tree.SetInt("kilnCtrlY", controllerPos.Y);
            tree.SetInt("kilnCtrlZ", controllerPos.Z);
        }

        var parts = new List<string>(fuelItems.Count);
        foreach (KeyValuePair<string, int> kv in fuelItems) parts.Add(kv.Key + "=" + kv.Value);
        tree.SetString("kilnFuelItems", string.Join(",", parts));
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);
        formed = tree.GetBool("kilnFormed", false);
        isLarge = tree.GetBool("kilnLarge", false);
        doorFacingIndex = tree.GetInt("kilnDoorFacing", BlockFacing.NORTH.Index);
        state = (KilnState)tree.GetInt("kilnState", 0);
        tiersBuilt = tree.GetInt("kilnTiers", 0);
        doorBricks = tree.GetInt("kilnDoorBricks", 0);
        fuelSeconds = tree.GetFloat("kilnFuel", 0f);
        cookFinishHours = tree.GetDouble("kilnCookFinish", 0.0);
        coolFinishHours = tree.GetDouble("kilnCoolFinish", 0.0);

        controllerPos = tree.HasAttribute("kilnCtrlX")
            ? new BlockPos(tree.GetInt("kilnCtrlX"), tree.GetInt("kilnCtrlY"), tree.GetInt("kilnCtrlZ"))
            : null;

        fuelItems.Clear();
        string joined = tree.GetString("kilnFuelItems", "") ?? "";
        foreach (string part in joined.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = part.LastIndexOf('=');
            if (eq > 0 && int.TryParse(part[(eq + 1)..], out int n)) fuelItems[part[..eq]] = n;
        }

        shellMesh = null;
        if (Api?.Side == EnumAppSide.Client)
        {
            // Regenerate display matrices AFTER our fields are loaded — base.FromTreeAttributes runs before
            // them, so its redraw can build tfMatrices against stale role/size state.
            MarkMeshesDirty();
            UpdateClientTick();
            MarkDirty(true);
        }
    }

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        BlockEntityKilnBase? ctrl = Controller();
        if (ctrl == null || (!ctrl.formed)) { dsc.AppendLine(Lang.Get("primitivekiln:kiln-place-hint")); return; }
        if (ctrl != this) { ctrl.AppendStateInfo(forPlayer, dsc); return; }
        AppendStateInfo(forPlayer, dsc);
    }

    private void AppendStateInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        double now = Api.World.Calendar.TotalHours;
        switch (state)
        {
            case KilnState.Construction:
                dsc.AppendLine(Lang.Get("primitivekiln:kiln-construct", tiersBuilt, TierCount, TierCost));
                break;
            case KilnState.Open:
                dsc.AppendLine(Lang.Get("primitivekiln:kiln-open", UsedSlots(), Capacity, DoorBricksNeeded));
                break;
            case KilnState.Sealed:
                dsc.AppendLine(Lang.Get("primitivekiln:kiln-sealed", (int)fuelSeconds, (int)FuelSecondsNeeded));
                break;
            case KilnState.Lit:
                dsc.AppendLine(Lang.Get("primitivekiln:kiln-lit", FormatHours(Math.Max(0, cookFinishHours - now))));
                break;
            case KilnState.Cooling:
                dsc.AppendLine(Lang.Get("primitivekiln:kiln-cooling", FormatHours(Math.Max(0, coolFinishHours - now))));
                break;
            case KilnState.Cooled:
                dsc.AppendLine(Lang.Get("primitivekiln:kiln-cooled"));
                break;
        }
    }

    private int UsedSlots()
    {
        int n = 0;
        for (int i = 0; i < Capacity; i++) if (!inv[i].Empty) n++;
        return n;
    }

    private static string FormatHours(double hours)
    {
        if (hours >= 24) return $"{hours / 24:0.0} days";
        if (hours >= 1) return $"{hours:0.0}h";
        return $"{(int)Math.Ceiling(hours * 60)}m";
    }
}
