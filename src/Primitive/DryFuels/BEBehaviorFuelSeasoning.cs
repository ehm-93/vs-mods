using System;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace Ehm93.VS.Primitive.DryFuels;

// Seasons stacked fuel in place. Rides on ground-storage block entities — firewood and peat piles are these —
// accumulating "seasoning" by the pile's local climate (warm, dry, sheltered, indoors season faster) and
// swapping fresh fuel for its longer-burning seasoned form once cured. Fuel opts in via the collectible
// attribute "dryfuelsSeasonsTo" (the seasoned variant's block/item code), added to firewood/peat by our patches.
//
// The behavior sits on ALL ground storage, but only piles that actually hold seasonable fuel register a tick:
// registration is driven by the inventory's SlotModified event (plus a one-shot check on load), so the vast
// majority of ground-storage piles never tick or write save data.
public class BEBehaviorFuelSeasoning : BlockEntityBehavior
{
    public const string SeasonsToAttr = "dryfuelsSeasonsTo";

    protected double seasonedHours;
    protected double lastCheckTotalHours;
    protected double seasonHoursRequired = 240; // base at a temperate, sheltered spot (rate ~1); tunable via behavior props

    private long tickListenerId;        // 0 = no tick registered
    private InventoryBase? watchedInv;  // the inventory we're subscribed to (for clean unsubscribe)
    private bool ticking;               // true while OnServerTick runs, so a conversion's SlotModified defers
    private bool resyncPending;         // a SlotModified arrived mid-tick; re-evaluate registration afterwards

    public BEBehaviorFuelSeasoning(BlockEntity blockentity) : base(blockentity) { }

    protected InventoryBase? Inventory => (Blockentity as BlockEntityContainer)?.Inventory;

    public double Progress => seasonHoursRequired <= 0 ? 0 : Math.Clamp(seasonedHours / seasonHoursRequired, 0, 1);

    public bool HasSeasonable
    {
        get
        {
            InventoryBase? inv = Inventory;
            if (inv == null) return false;
            for (int i = 0; i < inv.Count; i++)
                if (SeasonedCode(inv[i].Itemstack) != null) return true;
            return false;
        }
    }

    public override void Initialize(ICoreAPI api, JsonObject properties)
    {
        base.Initialize(api, properties);
        seasonHoursRequired = properties["seasonHours"].AsDouble(seasonHoursRequired);

        // Defer the first check so the inventory is populated (FromTreeAttributes has run); thereafter
        // SlotModified keeps the tick registration in sync. Server-only — seasoning is a server-side sim.
        if (api is ICoreServerAPI)
            Blockentity.RegisterDelayedCallback(_ => StartWatching(), 200);
    }

    private void StartWatching()
    {
        InventoryBase? inv = Inventory;
        if (inv == null) return;
        if (watchedInv == null)
        {
            inv.SlotModified += OnSlotModified;
            watchedInv = inv;
        }
        SyncTickState();
    }

    private void OnSlotModified(int slotId)
    {
        if (ticking) { resyncPending = true; return; } // defer: don't touch the listener from inside its own tick
        SyncTickState();
    }

    // Register the seasoning tick only while seasonable fuel is present; drop it (and reset progress) otherwise.
    private void SyncTickState()
    {
        if (Api is not ICoreServerAPI) return;

        bool hasFuel = HasSeasonable;
        if (hasFuel && tickListenerId == 0)
        {
            tickListenerId = Blockentity.RegisterGameTickListener(OnServerTick, 10000 + Api.World.Rand.Next(2000));
        }
        else if (!hasFuel && tickListenerId != 0)
        {
            Blockentity.UnregisterGameTickListener(tickListenerId);
            tickListenerId = 0;
            if (seasonedHours != 0 || lastCheckTotalHours != 0)
            {
                seasonedHours = 0;
                lastCheckTotalHours = 0;
                Blockentity.MarkDirty(true);
            }
        }
    }

    public override void OnBlockUnloaded()
    {
        base.OnBlockUnloaded();
        Unsubscribe();
    }

    public override void OnBlockRemoved()
    {
        base.OnBlockRemoved();
        Unsubscribe();
    }

    private void Unsubscribe()
    {
        if (watchedInv != null)
        {
            watchedInv.SlotModified -= OnSlotModified;
            watchedInv = null;
        }
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        // Idle / non-fuel piles (the vast majority of ground storage) keep nothing extra in the save.
        if (seasonedHours != 0 || lastCheckTotalHours != 0)
        {
            tree.SetDouble("seasonedHours", seasonedHours);
            tree.SetDouble("lastCheckTotalHours", lastCheckTotalHours);
        }
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        seasonedHours = tree.TryGetDouble("seasonedHours") ?? 0;
        lastCheckTotalHours = tree.TryGetDouble("lastCheckTotalHours") ?? 0;
    }

    protected virtual void OnServerTick(float dt)
    {
        InventoryBase? inv = Inventory;
        if (inv == null) return;

        ticking = true;
        try
        {
            double now = Api.World.Calendar.TotalHours;
            if (lastCheckTotalHours == 0) { lastCheckTotalHours = now; return; } // freshly started: anchor only
            double elapsed = now - lastCheckTotalHours;
            lastCheckTotalHours = now;
            if (elapsed <= 0) return;

            if (!HasSeasonable) return; // safety: fuel removed between a deferred SlotModified and this tick

            seasonedHours += elapsed * SeasoningRateMul();
            if (seasonedHours >= seasonHoursRequired)
            {
                ConvertSeasonable(inv);
                seasonedHours = 0;
            }
            Blockentity.MarkDirty(true);
        }
        finally
        {
            ticking = false;
            if (resyncPending) { resyncPending = false; SyncTickState(); }
        }
    }

    // Swap each fresh fuel stack for its seasoned variant, preserving the stack size.
    protected virtual void ConvertSeasonable(InventoryBase inv)
    {
        for (int i = 0; i < inv.Count; i++)
        {
            ItemSlot slot = inv[i];
            string? code = SeasonedCode(slot.Itemstack);
            if (code == null) continue;
            ItemStack? seasoned = ResolveStack(code, slot.Itemstack!.StackSize);
            if (seasoned == null) continue;
            slot.Itemstack = seasoned;
            slot.MarkDirty();
        }
    }

    protected string? SeasonedCode(ItemStack? stack)
    {
        JsonObject? attr = stack?.Collectible?.Attributes;
        if (attr == null) return null;
        string? code = attr[SeasonsToAttr].AsString(null);
        return string.IsNullOrEmpty(code) ? null : code;
    }

    protected ItemStack? ResolveStack(string code, int quantity)
    {
        var loc = new AssetLocation(code);
        Block? block = Api.World.GetBlock(loc);
        if (block != null) return new ItemStack(block, quantity);
        Item? item = Api.World.GetItem(loc);
        return item != null ? new ItemStack(item, quantity) : null;
    }

    // Stored in a fully enclosed room (a woodshed / indoor store) seasons faster, on top of climate. Tunable.
    protected const double RoomDryingBonus = 2.0;

    // Tunable seasoning-rate multiplier from the pile's local conditions. ~1.0 at a temperate, sheltered spot;
    // > 1 when warm/dry/indoors, < 1 when cold/wet/exposed.
    protected virtual double SeasoningRateMul()
    {
        ClimateCondition? cc = Api.World.BlockAccessor.GetClimateAt(Pos, EnumGetClimateMode.NowValues);
        if (cc == null) return 1.0;

        double warmth = Math.Clamp((cc.Temperature + 4.0) / 22.0, 0.3, 1.6);   // ~0.3x near freezing .. 1.6x hot, ~1.0 at 18C
        double dry01 = 1.0 - Math.Clamp((double)cc.WorldgenRainfall, 0.0, 1.0);
        double dryFactor = 0.6 + 0.8 * dry01;                                  // 0.6x in a soaking climate .. 1.4x arid
        bool openToSky = Pos.Y >= Api.World.BlockAccessor.GetRainMapHeightAt(Pos.X, Pos.Z);
        double shelter = openToSky ? (0.6 + 0.4 * dry01) : 1.0;                // exposed wood seasons slower in wet climates
        double room = InEnclosedRoom() ? RoomDryingBonus : 1.0;                // a woodshed / indoor store seasons faster

        return warmth * dryFactor * shelter * room;
    }

    // True when the pile sits in a fully enclosed room (no openings to the outside).
    protected virtual bool InEnclosedRoom()
    {
        Room? room = Api.ModLoader.GetModSystem<RoomRegistry>()?.GetRoomForPosition(Pos);
        return room != null && room.ExitCount == 0;
    }
}
