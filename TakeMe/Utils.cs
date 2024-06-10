using ImGuiNET;
using System.Numerics;

namespace TakeMe;

internal class Utils
{
    public static void Icon(uint iconId, Vector2 size)
    {
        var icon = Service.TextureProvider.GetIcon(iconId);
        if (icon != null)
        {
            ImGui.Image(icon.ImGuiHandle, size);
            ImGui.SameLine();
        }
    }
}
