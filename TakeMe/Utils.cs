using Dalamud.Interface.Textures.Internal;
using ImGuiNET;
using System.Numerics;

namespace TakeMe;

internal static unsafe class Utils
{
    private static readonly unsafe delegate* unmanaged<void*, byte> _automoveOff;
    private static readonly void* _instPMC;

    static Utils()
    {
        _automoveOff = (delegate* unmanaged<void*, byte>)Service.SigScanner.ScanText("80 B9 ?? ?? ?? ?? 01 76");
        _instPMC = (void*)Service.SigScanner.GetStaticAddressFromSig("48 8D 0D ?? ?? ?? ?? F3 0F 10 45");
    }

    public static void AutomoveOff()
    {
        _automoveOff(_instPMC);
    }

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
