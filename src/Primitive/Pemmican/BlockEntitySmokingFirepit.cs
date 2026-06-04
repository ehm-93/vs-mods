using System;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Ehm93.VS.Primitive.Pemmican;

// A firepit with a smoking rack baked into the same block. It keeps a stripped-down copy of the vanilla
// firepit's fuel/ignite/burn loop (no cooking pot, no input/output smelting, no GUI) and, while the fire
// is lit, smokes the rack contents into their dried output as a single batch. The shared rack inventory,
// hang/take, drying lookup and item rendering live in BlockEntityRack. Slot 0 is fuel; slots 1..8 rack.
public class BlockEntitySmokingFirepit : BlockEntityRack, IHeatSource
{
    public const double SmokeHoursRequired = 24.0;
    // In-game hours one fuel item burns for, per second of its vanilla burnDuration. Firewood
    // (burnDuration 24) -> ~8 in-game hours, so a full 24h batch needs ~3 logs (a low, slow, smoky fire).
    public const float FuelHourFactor = 0.334f;

    // Smoking wants smoky, organic fuel — not coal/charcoal/coke. Matched by first code part, so this
    // is a quick list to fine-tune later.
    static readonly string[] AcceptedFuels = { "firewood", "stick", "peatbrick" };

    // --- firepit burn state (public so attach/detach can copy it to/from a vanilla firepit) ---
    public float furnaceTemperature = 20f;
    public int maxTemperature;
    public float fuelBurnTime;
    public float maxFuelBurnTime;
    public bool canIgniteFuel;
    public double extinguishedTotalHours;

    public bool IsBurning => fuelBurnTime > 0f;
    public bool IsSmoldering => canIgniteFuel;

    // --- rack smoking state ---
    protected double smokeHours;

    public ItemSlot FuelSlot => inventory[0];
    public ItemStack? FuelStack
    {
        get => inventory[0].Itemstack;
        set { inventory[0].Itemstack = value; inventory[0].MarkDirty(); }
    }

    public int FuelCount => FuelStack?.StackSize ?? 0;

    MeshData? rackMesh;
    const float RackYOffset = 0.0f;

    // The standalone rack shape carries extra elements used only for connecting to adjacent racks (seam
    // stubs, rail/rung bridges, the centred seam rail/rung, the moved lashings, and the upward post
    // extensions). A firepit-mounted rack is always a single, unconnected rack, so we tesselate ONLY this
    // base "lone rack" element set — otherwise those connect-only pieces render as bars poking out in every
    // direction and the posts shoot up out of the top of the firepit.
    static readonly string[] LoneRackElements =
    {
        "bar-front", "bar-mid", "bar-back",
        "rail-left", "rail-right",
        "lash-fl", "lash-ml", "lash-bl", "lash-fr", "lash-mr", "lash-br",
        "post-nw", "post-ne", "post-sw", "post-se",
    };

    protected override string InventoryId => "smokingfirepit";

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        if (api.Side == EnumAppSide.Server)
        {
            RegisterGameTickListener(OnBurnTick, 100);
        }
    }

    string BurnState => Block?.Variant["burnstate"] ?? "cold";

    // ---------------- fuel / burning ----------------

    bool HasFuel()
    {
        ItemStack? stack = FuelStack;
        if (stack == null) return false;
        CombustibleProperties? props = stack.Collectible.GetCombustibleProperties(Api.World, stack, null);
        return props != null && props.BurnTemperature > 0;
    }

    // Driven on the in-game calendar clock (not real-time dt) so /time set and returning to an unloaded
    // chunk fast-forward the fire and smoking.
    void OnBurnTick(float dt)
    {
        double now = Api.World.Calendar.TotalHours;
        double elapsed = now - lastCalendarHours;
        lastCalendarHours = now;

        if (elapsed <= 0) { UpdateBurnState(now); return; }

        SimulateBurning(elapsed, now);
    }

    // Advances the fire and smoking over `elapsed` in-game hours. Large jumps are stepped through so a
    // single log can't fast-forward a whole batch: smoking only progresses while fuel is actually burning,
    // and the fire relights from the next piece of fuel only until the stack runs out.
    void SimulateBurning(double elapsed, double now)
    {
        const double step = 0.5; // in-game-hour granularity
        int safety = 200_000;
        bool dirty = false;

        while (elapsed > 1e-6 && safety-- > 0)
        {
            if (fuelBurnTime <= 0f)
            {
                if (canIgniteFuel && HasFuel()) { ConsumeFuelPiece(); dirty = true; }
                else break; // fire is out and can't relight; the rest of the elapsed time just passes
            }

            double burn = Math.Min(Math.Min(step, elapsed), fuelBurnTime);
            fuelBurnTime -= (float)burn;
            elapsed -= burn;

            if (HasRackable())
            {
                smokeHours += burn;
                if (smokeHours >= RequiredHours())
                {
                    ConvertContents();
                    smokeHours = 0;
                    dirty = true;
                }
            }

            if (fuelBurnTime <= 0f)
            {
                fuelBurnTime = 0f;
                extinguishedTotalHours = now - elapsed;
            }
        }

        if (!HasRackable() && smokeHours != 0) { smokeHours = 0; dirty = true; }

        UpdateBurnState(now);

        if (dirty) MarkDirty(true);
        // Resync a few times a minute while smoking so the progress tooltip stays live (~3s at 100ms ticks).
        else if (++saveTickCounter >= 30) { saveTickCounter = 0; MarkDirty(); }
    }

    void UpdateBurnState(double now)
    {
        // Goes fully cold (needs re-lighting) once it's been out for a couple of hours.
        if (!IsBurning && canIgniteFuel && now - extinguishedTotalHours > 2.0) canIgniteFuel = false;

        string desired = IsBurning ? "lit" : (canIgniteFuel ? "extinct" : "cold");
        if (BurnState != desired) SetBlockState(desired);
    }

    // Consumes one piece of fuel and sets the burn timer (in in-game hours) for it.
    void ConsumeFuelPiece()
    {
        ItemStack? stack = FuelStack;
        if (stack == null) return;

        CombustibleProperties props = stack.Collectible.GetCombustibleProperties(Api.World, stack, null);
        fuelBurnTime = maxFuelBurnTime = props.BurnDuration * FuelHourFactor;
        maxTemperature = props.BurnTemperature;

        stack.StackSize--;
        if (stack.StackSize <= 0) FuelStack = null;
        else FuelSlot.MarkDirty();
    }

    public EnumIgniteState GetIgnitableState(float secondsIgniting)
    {
        if (!HasFuel()) return EnumIgniteState.NotIgnitablePreventDefault;
        if (IsBurning) return EnumIgniteState.NotIgnitablePreventDefault;
        return secondsIgniting > 3f ? EnumIgniteState.IgniteNow : EnumIgniteState.Ignitable;
    }

    void SetBlockState(string state)
    {
        if (Block == null) return;
        AssetLocation loc = Block.CodeWithVariant("burnstate", state);
        Block? next = Api.World.GetBlock(loc);
        if (next == null) return;
        Api.World.BlockAccessor.ExchangeBlock(next.Id, Pos);
        Block = next;
        MarkDirty(true);
    }

    public float GetHeatStrength(IWorldAccessor world, BlockPos heatSourcePos, BlockPos heatReceiverPos)
    {
        return IsBurning ? 10f : (IsSmoldering ? 0.25f : 0f);
    }

    // ---------------- smoking ----------------

    // The batch finishes when smoking reaches the slowest hung piece's required hours.
    double RequiredHours()
    {
        double hours = 0;
        for (int i = 1; i <= RackSlots; i++)
            if (Match(inventory[i].Itemstack) is DryingResult m && m.Hours > hours) hours = m.Hours;
        return hours > 0 ? hours : SmokeHoursRequired;
    }

    void ConvertContents()
    {
        for (int i = 1; i <= RackSlots; i++)
        {
            ItemSlot slot = inventory[i];
            if (Match(slot.Itemstack) is not DryingResult m) continue;

            slot.Itemstack = new ItemStack(m.Output, m.Quantity);
            slot.MarkDirty();
        }
    }

    public bool IsFuelItem(ItemStack? stack)
    {
        if (stack?.Collectible?.Code == null) return false;
        if (Array.IndexOf(AcceptedFuels, stack.Collectible.Code.FirstCodePart()) < 0) return false;
        CombustibleProperties? props = stack.Collectible.GetCombustibleProperties(Api.World, stack, null);
        return props != null && props.BurnTemperature > 0;
    }

    public bool TryAddFuel(ItemSlot handSlot)
    {
        if (!IsFuelItem(handSlot.Itemstack)) return false;
        int moved = handSlot.TryPutInto(Api.World, FuelSlot, 1);
        if (moved > 0)
        {
            handSlot.MarkDirty();
            MarkDirty(true);
            return true;
        }
        return false;
    }

    // ---------------- attach / detach state transfer ----------------

    // Pull the fuel stack out without dropping it (used during block swaps).
    // TakeOutWhole() throws on an empty slot, so guard against no fuel.
    public ItemStack? TakeOutFuel()
    {
        return FuelSlot.Empty ? null : FuelSlot.TakeOutWhole();
    }

    // ---------------- rendering ----------------

    public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
    {
        if (capi == null) return false;

        // Render the firepit base ourselves so we can drop the firewood logs when there's no fuel.
        string basePath = HasFuel() || IsBurning
            ? "game:shapes/block/wood/firepit/" + BurnState + "-normal.json"
            : "pemmican:shapes/block/firepit-empty.json";
        Shape? baseShape = Shape.TryGet(Api, basePath);
        bool renderedBase = false;
        if (baseShape != null)
        {
            capi.Tesselator.TesselateShape(Block, baseShape, out MeshData baseMesh);
            mesher.AddMeshData(baseMesh);
            renderedBase = true;
        }

        if (rackMesh == null)
        {
            Block rackBlock = Api.World.GetBlock(new AssetLocation(PemmicanModSystem.ModId, "smokerack"));
            Shape? rackShape = rackBlock?.Shape?.Base == null ? null : capi.TesselatorManager.GetCachedShape(rackBlock.Shape.Base);
            if (rackBlock != null && rackShape != null)
            {
                capi.Tesselator.TesselateShape(rackBlock, rackShape, out rackMesh, null, null, LoneRackElements);
                rackMesh.Translate(0f, RackYOffset, 0f);
            }
        }
        if (rackMesh != null) mesher.AddMeshData(rackMesh);

        RenderRackItems(mesher);

        // If we drew our own firepit base, skip the block's default (with-logs) shape to avoid doubling up.
        return renderedBase;
    }

    // ---------------- info / persistence ----------------

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder sb)
    {
        base.GetBlockInfo(forPlayer, sb);

        if (IsBurning) sb.AppendLine(Lang.Get("pemmican:smokingfirepit-burning"));
        else if (IsSmoldering) sb.AppendLine(Lang.Get("pemmican:smokingfirepit-smoldering"));
        else if (HasFuel()) sb.AppendLine(Lang.Get("pemmican:smokingfirepit-unlit"));
        else sb.AppendLine(Lang.Get("pemmican:smokingfirepit-nofuel"));

        if (FuelCount > 0) sb.AppendLine(Lang.Get("pemmican:smokingfirepit-fuelcount", FuelCount));

        int racked = CountRacked();
        sb.AppendLine(racked == 0
            ? Lang.Get("pemmican:smokerack-empty")
            : Lang.Get("pemmican:smokerack-contents", racked, RackSlots));

        if (HasRackable())
        {
            int pct = (int)GameMath.Clamp(smokeHours / RequiredHours() * 100.0, 0, 100);
            sb.AppendLine(Lang.Get("pemmican:smokerack-progress", pct));
            if (!IsBurning) sb.AppendLine(Lang.Get("pemmican:smokerack-needfire"));
        }
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);

        tree.SetFloat("furnaceTemperature", furnaceTemperature);
        tree.SetInt("maxTemperature", maxTemperature);
        tree.SetFloat("fuelBurnTime", fuelBurnTime);
        tree.SetFloat("maxFuelBurnTime", maxFuelBurnTime);
        tree.SetBool("canIgniteFuel", canIgniteFuel);
        tree.SetDouble("extinguishedTotalHours", extinguishedTotalHours);
        tree.SetDouble("smokeHours", smokeHours);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);

        furnaceTemperature = tree.GetFloat("furnaceTemperature", 20f);
        maxTemperature = tree.GetInt("maxTemperature");
        fuelBurnTime = tree.GetFloat("fuelBurnTime");
        maxFuelBurnTime = tree.GetFloat("maxFuelBurnTime");
        canIgniteFuel = tree.GetBool("canIgniteFuel");
        extinguishedTotalHours = tree.GetDouble("extinguishedTotalHours");
        smokeHours = tree.GetDouble("smokeHours");
    }
}
