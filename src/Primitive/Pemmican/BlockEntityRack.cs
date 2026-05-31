using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace Ehm93.VS.Primitive.Pemmican;

// Shared base for the two racks: the smoking firepit (rack over a fire) and the standalone drying rack.
// It owns the 8-piece rack inventory, the hang/take interaction, the data-driven drying lookup (Match),
// and the in-world rendering of hung items (via ITexPositionSource, so item meshes go into the block
// atlas). Slot 0 is reserved (fuel on the firepit, unused on the drying rack); rack pieces are slots
// 1..RackSlots. Subclasses add their own processing (burning + batch smoke, or climate-driven drying).
public abstract class BlockEntityRack : BlockEntity, ITexPositionSource
{
    public const int RackSlots = 8;

    protected InventoryGeneric inventory;
    public InventoryBase Inventory => inventory;

    // Calendar-clock bookmark so /time set and chunk reloads fast-forward processing (shared model).
    protected double lastCalendarHours = -1;
    protected int saveTickCounter;

    protected ICoreClientAPI? capi;
    CollectibleObject? nowTesselatingObj;
    Shape? nowTesselatingShape;
    PemmicanModSystem? rackRecipes;

    // On-rack item layout (block-local 0..1): a 3 / 2 / 3 arrangement (back, middle, front rows). Public
    // so the block can build matching per-slot selection boxes. Index i here maps to rack slot i+1.
    protected const float ItemScale = 0.7f;
    protected const float ItemY = 0.98f;
    public static readonly float[] PosX = { 0.2f, 0.5f, 0.8f, 0.35f, 0.65f, 0.2f, 0.5f, 0.8f };
    public static readonly float[] PosZ = { 0.2f, 0.2f, 0.2f, 0.5f, 0.5f, 0.8f, 0.8f, 0.8f };

    // Distinguishes inventories per block type (used as the inventory network/save id).
    protected abstract string InventoryId { get; }

    protected BlockEntityRack()
    {
        inventory = new InventoryGeneric(RackSlots + 1, null, null, null);
    }

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        inventory.LateInitialize($"{InventoryId}-{Pos.X}/{Pos.Y}/{Pos.Z}", api);
        inventory.Pos = Pos;
        capi = api as ICoreClientAPI;
        if (lastCalendarHours < 0) lastCalendarHours = api.World.Calendar.TotalHours;
    }

    // ---------------- rack lookup ----------------

    // What a hung item turns into, and how long it takes — data-driven (config/drying/*.json). Null if
    // the item can't go on the rack, so we never hang something that would never convert.
    public DryingResult? Match(ItemStack? stack)
    {
        if (Api == null) return null;
        rackRecipes ??= Api.ModLoader.GetModSystem<PemmicanModSystem>();
        return rackRecipes?.Match(Api.World, stack);
    }

    public bool IsRackable(ItemStack? stack) => Match(stack) != null;

    protected bool HasRackable()
    {
        for (int i = 1; i <= RackSlots; i++) if (IsRackable(inventory[i].Itemstack)) return true;
        return false;
    }

    public int CountRacked()
    {
        int n = 0;
        for (int i = 1; i <= RackSlots; i++) if (!inventory[i].Empty) n++;
        return n;
    }

    public bool RackIsEmpty()
    {
        for (int i = 1; i <= RackSlots; i++) if (!inventory[i].Empty) return false;
        return true;
    }

    // ---------------- hang / take ----------------

    // Hang from the held stack onto the rack: one piece, or — when `all` — as many as fit.
    public bool TryHang(ItemSlot handSlot, bool all = false)
    {
        if (handSlot.Empty || !IsRackable(handSlot.Itemstack)) return false;

        bool added = false;
        for (int i = 1; i <= RackSlots; i++)
        {
            if (!inventory[i].Empty) continue;
            if (handSlot.Empty || !IsRackable(handSlot.Itemstack)) break;
            if (handSlot.TryPutInto(Api.World, inventory[i], 1) > 0) { added = true; OnRackSlotChanged(i); }
            if (!all) break;
        }

        if (added)
        {
            handSlot.MarkDirty();
            MarkDirty(true);
        }
        return added;
    }

    // Takes the top-most piece off the rack, or — when `all` — every piece. False if the rack is empty.
    public bool TryTake(IPlayer byPlayer, bool all = false)
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
            OnRackSlotChanged(i);
            took = true;
            if (!all) break;
        }

        if (took) MarkDirty(true);
        return took;
    }

    // Hang one piece into a specific slot (1..RackSlots) — used when the player targets that slot directly.
    public bool TryHangSlot(int slot, ItemSlot handSlot)
    {
        if (slot < 1 || slot > RackSlots || handSlot.Empty || !inventory[slot].Empty || !IsRackable(handSlot.Itemstack)) return false;
        if (handSlot.TryPutInto(Api.World, inventory[slot], 1) <= 0) return false;
        OnRackSlotChanged(slot);
        handSlot.MarkDirty();
        MarkDirty(true);
        return true;
    }

    // Take the piece from a specific slot (1..RackSlots). False if that slot is empty.
    public bool TryTakeSlot(int slot, IPlayer byPlayer)
    {
        if (slot < 1 || slot > RackSlots || inventory[slot].Empty) return false;
        ItemStack stack = inventory[slot].TakeOutWhole();
        if (!byPlayer.InventoryManager.TryGiveItemstack(stack))
            Api.World.SpawnItemEntity(stack, Pos.ToVec3d().Add(0.5, 0.7, 0.5));
        inventory[slot].MarkDirty();
        OnRackSlotChanged(slot);
        MarkDirty(true);
        return true;
    }

    public void DropContents()
    {
        inventory.DropAll(Pos.ToVec3d().Add(0.5, 0.5, 0.5));
    }

    // Called when rack slot `i` is filled (hang) or emptied (take). Subclasses that track per-slot state
    // (e.g. the drying rack's progress) reset it here so progress never leaks between different pieces.
    protected virtual void OnRackSlotChanged(int slot) { }

    // ---------------- rendering ----------------

    // ITexPositionSource: tesselate item meshes into the BLOCK texture atlas so hung pieces render
    // correctly in the chunk mesh (items normally live in a different atlas).
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

    // Adds the hung-item meshes to the chunk mesh. Subclasses call this from OnTesselation after drawing
    // whatever frame they use (the firepit draws its own; the drying rack uses its block shape).
    protected void RenderRackItems(ITerrainMeshPool mesher)
    {
        if (capi == null) return;

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
    }

    // ---------------- persistence ----------------

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);

        ITreeAttribute invTree = new TreeAttribute();
        inventory.ToTreeAttributes(invTree);
        tree["inventory"] = invTree;
        tree.SetDouble("lastCalendarHours", lastCalendarHours);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
    {
        base.FromTreeAttributes(tree, worldForResolving);

        ITreeAttribute? invTree = tree.GetTreeAttribute("inventory");
        if (invTree != null) inventory.FromTreeAttributes(invTree);
        lastCalendarHours = tree.GetDouble("lastCalendarHours", -1);

        if (Api?.Side == EnumAppSide.Client) MarkDirty(true);
    }
}
