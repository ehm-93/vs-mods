using System;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace Ehm93.VS.Primitive.Pemmican;

// A firepit with a smoking rack baked into the same block. It keeps a stripped-down copy of the vanilla
// firepit's fuel/ignite/burn loop (no cooking pot, no input/output smelting, no GUI) plus an 8-slot rack
// that smokes meat into jerky while the fire is lit. Slot 0 is fuel; slots 1..8 are the rack.
public class BlockEntitySmokingFirepit : BlockEntity, IHeatSource, ITexPositionSource
{
    public const int RackSlots = 8;
    public const double SmokeHoursRequired = 24.0;
    // In-game hours one fuel item burns for, per second of its vanilla burnDuration. Firewood
    // (burnDuration 24) -> ~4 in-game hours, so a full 24h batch needs ~6 logs (a low, slow, smoky fire).
    public const float FuelHourFactor = 0.167f;

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
    protected double lastCalendarHours = -1;

    protected InventoryGeneric inventory;
    public InventoryBase Inventory => inventory;

    public ItemSlot FuelSlot => inventory[0];
    public ItemStack? FuelStack
    {
        get => inventory[0].Itemstack;
        set { inventory[0].Itemstack = value; inventory[0].MarkDirty(); }
    }

    public int FuelCount => FuelStack?.StackSize ?? 0;

    int saveTickCounter;
    MeshData? rackMesh;
    ICoreClientAPI? capi;
    CollectibleObject? nowTesselatingObj;
    Shape? nowTesselatingShape;

    // On-rack display layout (block-local 0..1). The rack frame is raised so it sits above the fire.
    const float RackYOffset = 0.0f;
    const float ItemScale = 0.7f;
    const float ItemY = 0.98f;
    // 8 meat positions in a 3 / 2 / 3 arrangement (back row, middle row, front row).
    static readonly float[] PosX = { 0.2f, 0.5f, 0.8f, 0.35f, 0.65f, 0.2f, 0.5f, 0.8f };
    static readonly float[] PosZ = { 0.2f, 0.2f, 0.2f, 0.5f, 0.5f, 0.8f, 0.8f, 0.8f };

    public BlockEntitySmokingFirepit()
    {
        inventory = new InventoryGeneric(RackSlots + 1, null, null, null);
    }

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        inventory.LateInitialize($"smokingfirepit-{Pos.X}/{Pos.Y}/{Pos.Z}", api);
        capi = api as ICoreClientAPI;

        if (lastCalendarHours < 0) lastCalendarHours = api.World.Calendar.TotalHours;

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
        else if (++saveTickCounter >= 150) { saveTickCounter = 0; MarkDirty(); }
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

    // ---------------- rack / smoking ----------------

    // What a hung item turns into, and how long it takes. The recipes are data-driven
    // (assets/<domain>/recipes/drying/*.json); we just delegate to the loaded set. Returns null if the
    // item can't go on the rack, so we never hang something that would never convert.
    private PemmicanModSystem? rackRecipes;
    public DryingResult? Match(ItemStack? stack)
    {
        if (Api == null) return null;
        rackRecipes ??= Api.ModLoader.GetModSystem<PemmicanModSystem>();
        return rackRecipes?.Match(Api.World, stack);
    }

    public bool IsRackable(ItemStack? stack) => Match(stack) != null;

    // The batch finishes when smoking reaches the slowest hung piece's required hours.
    double RequiredHours()
    {
        double hours = 0;
        for (int i = 1; i <= RackSlots; i++)
            if (Match(inventory[i].Itemstack) is DryingResult m && m.Hours > hours) hours = m.Hours;
        return hours > 0 ? hours : SmokeHoursRequired;
    }

    public bool IsFuelItem(ItemStack? stack)
    {
        if (stack?.Collectible?.Code == null) return false;
        if (Array.IndexOf(AcceptedFuels, stack.Collectible.Code.FirstCodePart()) < 0) return false;
        CombustibleProperties? props = stack.Collectible.GetCombustibleProperties(Api.World, stack, null);
        return props != null && props.BurnTemperature > 0;
    }

    public int CountMeat()
    {
        int n = 0;
        for (int i = 1; i <= RackSlots; i++) if (!inventory[i].Empty) n++;
        return n;
    }

    bool HasRackable()
    {
        for (int i = 1; i <= RackSlots; i++) if (IsRackable(inventory[i].Itemstack)) return true;
        return false;
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

    // Hang meat from the held stack onto the rack: one piece, or — when `all` — as many as fit.
    public bool TryAddMeat(ItemSlot handSlot, bool all = false)
    {
        if (handSlot.Empty || !IsRackable(handSlot.Itemstack)) return false;

        bool added = false;
        for (int i = 1; i <= RackSlots; i++)
        {
            if (!inventory[i].Empty) continue;
            if (handSlot.Empty || !IsRackable(handSlot.Itemstack)) break;
            if (handSlot.TryPutInto(Api.World, inventory[i], 1) > 0) added = true;
            if (!all) break;
        }

        if (added)
        {
            handSlot.MarkDirty();
            MarkDirty(true);
        }
        return added;
    }

    // Takes the top-most piece of meat/jerky off the rack, or — when `all` — every piece.
    // Returns false if the rack is empty.
    public bool TryTakeMeat(IPlayer byPlayer, bool all = false)
    {
        bool took = false;
        for (int i = RackSlots; i >= 1; i--)
        {
            if (inventory[i].Empty) continue;

            ItemStack stack = inventory[i].TakeOutWhole();
            if (!byPlayer.InventoryManager.TryGiveItemstack(stack))
            {
                Api.World.SpawnItemEntity(stack, Pos.ToVec3d().Add(0.5, 0.7, 0.5));
            }
            inventory[i].MarkDirty();
            took = true;
            if (!all) break;
        }

        if (took) MarkDirty(true);
        return took;
    }

    public bool RackIsEmpty()
    {
        for (int i = 1; i <= RackSlots; i++) if (!inventory[i].Empty) return false;
        return true;
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

    // ---------------- attach / detach state transfer ----------------

    // Pull the fuel stack out without dropping it (used during block swaps).
    // TakeOutWhole() throws on an empty slot, so guard against no fuel.
    public ItemStack? TakeOutFuel()
    {
        return FuelSlot.Empty ? null : FuelSlot.TakeOutWhole();
    }

    public void DropContents()
    {
        inventory.DropAll(Pos.ToVec3d().Add(0.5, 0.5, 0.5));
    }

    // ---------------- rendering ----------------

    // --- ITexPositionSource: lets us tesselate item meshes into the BLOCK texture atlas so meat/jerky
    //     render correctly when added to the chunk mesh (items normally live in a different atlas). ---
    public Size2i AtlasSize => capi!.BlockTextureAtlas.Size;

    public TextureAtlasPosition this[string textureCode]
    {
        get
        {
            IDictionary<string, CompositeTexture>? textures =
                nowTesselatingObj is Item it ? it.Textures : (nowTesselatingObj as Block)?.Textures;

            AssetLocation? path = null;
            if (textures != null && textures.TryGetValue(textureCode, out CompositeTexture? ct)) path = ct.Baked.BakedName;
            if (path == null && textures != null && textures.TryGetValue("all", out ct)) path = ct.Baked.BakedName;
            if (path == null) nowTesselatingShape?.Textures.TryGetValue(textureCode, out path);
            path ??= new AssetLocation(textureCode);

            return GetOrCreateTexPos(path);
        }
    }

    TextureAtlasPosition GetOrCreateTexPos(AssetLocation texturePath)
    {
        TextureAtlasPosition? pos = capi!.BlockTextureAtlas[texturePath];
        if (pos == null)
        {
            capi.BlockTextureAtlas.GetOrInsertTexture(texturePath, out int _, out TextureAtlasPosition inserted);
            pos = inserted;
        }
        return pos ?? capi.BlockTextureAtlas.UnknownTexturePosition;
    }

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
            if (rackBlock != null)
            {
                capi.Tesselator.TesselateBlock(rackBlock, out rackMesh);
                rackMesh.Translate(0f, RackYOffset, 0f);
            }
        }
        if (rackMesh != null) mesher.AddMeshData(rackMesh);

        Vec3f center = new Vec3f(0.5f, 0.5f, 0.5f);
        for (int i = 1; i <= RackSlots; i++)
        {
            ItemStack? stack = inventory[i].Itemstack;
            if (stack?.Item == null) continue;

            nowTesselatingObj = stack.Item;
            nowTesselatingShape = stack.Item.Shape?.Base != null ? capi.TesselatorManager.GetCachedShape(stack.Item.Shape.Base) : null;
            capi.Tesselator.TesselateItem(stack.Item, out MeshData mesh, this);
            mesh.RenderPassesAndExtraBits.Fill((short)EnumChunkRenderPass.Opaque);

            mesh.Scale(center, ItemScale, ItemScale, ItemScale);

            int idx = i - 1;
            mesh.Translate(PosX[idx] - 0.5f, ItemY - 0.5f, PosZ[idx] - 0.5f);
            mesher.AddMeshData(mesh);
        }

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

        int meat = CountMeat();
        sb.AppendLine(meat == 0
            ? Lang.Get("pemmican:smokerack-empty")
            : Lang.Get("pemmican:smokerack-contents", meat, RackSlots));

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

        ITreeAttribute invTree = new TreeAttribute();
        inventory.ToTreeAttributes(invTree);
        tree["inventory"] = invTree;

        tree.SetFloat("furnaceTemperature", furnaceTemperature);
        tree.SetInt("maxTemperature", maxTemperature);
        tree.SetFloat("fuelBurnTime", fuelBurnTime);
        tree.SetFloat("maxFuelBurnTime", maxFuelBurnTime);
        tree.SetBool("canIgniteFuel", canIgniteFuel);
        tree.SetDouble("extinguishedTotalHours", extinguishedTotalHours);
        tree.SetDouble("smokeHours", smokeHours);
        tree.SetDouble("lastCalendarHours", lastCalendarHours);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);

        ITreeAttribute? invTree = tree.GetTreeAttribute("inventory");
        if (invTree != null) inventory.FromTreeAttributes(invTree);

        furnaceTemperature = tree.GetFloat("furnaceTemperature", 20f);
        maxTemperature = tree.GetInt("maxTemperature");
        fuelBurnTime = tree.GetFloat("fuelBurnTime");
        maxFuelBurnTime = tree.GetFloat("maxFuelBurnTime");
        canIgniteFuel = tree.GetBool("canIgniteFuel");
        extinguishedTotalHours = tree.GetDouble("extinguishedTotalHours");
        smokeHours = tree.GetDouble("smokeHours");
        lastCalendarHours = tree.GetDouble("lastCalendarHours", -1);

        if (Api?.Side == EnumAppSide.Client) MarkDirty(true);
    }
}
