using Vintagestory.API.Common;

namespace Ehm93.VS.Cooking.Beer;

public class BeerModSystem : ModSystem
{
    public const string ModId = "beer";

    public override void Start(ICoreAPI api)
    {
        base.Start(api);

        // The hop PLANT is the whole cultivation system: plant the rhizome on farmland and a 3 m pole renders
        // automatically (no collision) for the bine to climb over the seasons (drawing farmland nutrients).
        // Growth tick is server-gated; the bine mesh is client-gated.
        api.RegisterItemClass("HopRhizome", typeof(ItemHopRhizome));
        api.RegisterBlockClass("HopPlant", typeof(BlockHopPlant));
        api.RegisterBlockEntityClass("HopPlant", typeof(BlockEntityHopPlant));
    }
}
