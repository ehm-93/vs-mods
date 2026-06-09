using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Ehm93.VS.Primitive.Compass;

// Client-only debug/tuning command. The compass needle alignment is view/transform dependent, so the
// exact yaw offset is far easier to dial in visually than to derive. Face a known direction, hold the
// compass, then adjust until the tip points the right way; bake the final values into BlockCompass.
internal static class CompassTuningCommand
{
    public static void Register(ICoreClientAPI capi)
    {
        var parsers = capi.ChatCommands.Parsers;
        capi.ChatCommands.Create("compass")
            .WithDescription("Tune the in-hand compass needle alignment")
            .RequiresPrivilege("chat")
            .BeginSubCommand("offset")
                .WithDescription("Set the needle yaw offset in degrees")
                .WithArgs(parsers.Float("degrees"))
                .HandleWith(args =>
                {
                    BlockCompass.YawOffsetDeg = (float)args[0];
                    return TextCommandResult.Success($"Compass yaw offset = {BlockCompass.YawOffsetDeg:0.#}°");
                })
            .EndSubCommand()
            .BeginSubCommand("sign")
                .WithDescription("Flip the needle rotation direction")
                .HandleWith(args =>
                {
                    BlockCompass.YawSign = -BlockCompass.YawSign;
                    return TextCommandResult.Success($"Compass yaw sign = {BlockCompass.YawSign:+0;-0}");
                })
            .EndSubCommand()
            .BeginSubCommand("lerp")
                .WithDescription("Set the needle easing speed (higher = settles faster, ~0 = barely moves)")
                .WithArgs(parsers.Float("rate"))
                .HandleWith(args =>
                {
                    BlockCompass.LerpRate = (float)args[0];
                    return TextCommandResult.Success($"Compass lerp rate = {BlockCompass.LerpRate:0.##}");
                })
            .EndSubCommand()
            .BeginSubCommand("guioffset")
                .WithDescription("Rotate the inventory-icon needle (degrees) so its tip points up when you face north")
                .WithArgs(parsers.Float("degrees"))
                .HandleWith(args =>
                {
                    BlockCompass.GuiYawOffsetDeg = (float)args[0];
                    return TextCommandResult.Success($"Compass gui offset = {BlockCompass.GuiYawOffsetDeg:0.#}°");
                })
            .EndSubCommand()
            .BeginSubCommand("info")
                .WithDescription("Show camera yaw, the direction you face, and current tuning")
                .HandleWith(args =>
                {
                    var player = capi.World.Player;
                    float yawDeg = player.CameraYaw * 180f / GameMath.PI;
                    BlockFacing facing = BlockFacing.HorizontalFromYaw(player.CameraYaw);
                    return TextCommandResult.Success(
                        $"camera yaw {yawDeg:0.#}°, facing {facing.Code}, offset {BlockCompass.YawOffsetDeg:0.#}°, sign {BlockCompass.YawSign:+0;-0}, lerp {BlockCompass.LerpRate:0.##}, guioffset {BlockCompass.GuiYawOffsetDeg:0.#}°");
                })
            .EndSubCommand()
            .Validate();
    }
}
