using System;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;
using Ehm93.VS.Crops.Common;

namespace Ehm93.VS.Crops.Generations;

public class BEBehaviorFarmlandGeneration : BlockEntityBehavior, IGenerationSource
{
    private int generation = 0;

    public int Generation
    {
        get => generation;
        set
        {
            if (generation == value) return;
            generation = value;
            FarmlandEntity.MarkDirty();
        }
    }

    public BlockEntityFarmland FarmlandEntity => (BlockEntityFarmland)Blockentity;

    public BEBehaviorFarmlandGeneration(BlockEntity blockEntity) : base(blockEntity)
    {
        if (blockEntity is not BlockEntityFarmland)
            throw new ArgumentException("FarmlandGeneration behavior may only be used on farmland.");
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetInt("generation", generation);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        generation = tree.GetInt("generation", 0);
    }
}
