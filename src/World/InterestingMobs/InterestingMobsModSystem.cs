using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace Ehm93.VS.World.InterestingMobs;

public class InterestingMobsModSystem : ModSystem
{
    public const string ModId = "worldinterestingmobs";

    public override void Start(ICoreAPI api)
    {
        base.Start(api);

        AiTaskRegistry.Register<AiTaskBloatStalk>("bloatstalk");
        AiTaskRegistry.Register<AiTaskBloatFuse>("bloatfuse");

        AiTaskRegistry.Register<AiTaskGloamHunt>("gloamhunt");

        api.RegisterEntityBehaviorClass("gloamheadtracking", typeof(EntityBehaviorGloamHeadTracking));
    }
}
