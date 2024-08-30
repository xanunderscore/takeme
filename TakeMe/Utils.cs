using Dalamud.Interface.Textures.Internal;
using ImGuiNET;
using System.Numerics;

namespace TakeMe;

internal class Utils
{
    public static void Icon(uint iconId, Vector2 size)
    {
        try
        {
            var icon = Service.TextureProvider.GetFromGameIcon(iconId)?.GetWrapOrEmpty();
            if (icon != null)
            {
                ImGui.Image(icon.ImGuiHandle, size);
                ImGui.SameLine();
            }
        }
        catch (IconNotFoundException)
        { }
    }

    internal static void DumpObject<T>(T obj)
    {
        foreach (var field in typeof(T).GetFields())
            ImGui.Text($"{field.Name}: {field.GetValue(obj)}");
    }
}
