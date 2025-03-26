using Dalamud.Interface.Utility;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace TakeMe;
internal class Camera
{
    public static Camera? Instance;

    public Vector3 Origin;
    public Matrix4x4 View;
    public Matrix4x4 Proj;
    public Matrix4x4 ViewProj;
    public Vector4 NearPlane;
    public float CameraAzimuth; // facing north = 0, facing west = pi/4, facing south = +-pi/2, facing east = -pi/4
    public float CameraAltitude; // facing horizontally = 0, facing down = pi/4, facing up = -pi/4
    public Vector2 ViewportSize;

    private readonly List<(Vector2 from, Vector2 to, uint col)> _worldDrawLines = [];
    private readonly List<(Vector2 a, Vector2 b, Vector2 c, Vector2 d, uint col)> _worldDrawQuads = [];

    public unsafe void Update()
    {
        var controlCamera = CameraManager.Instance()->GetActiveCamera();
        var renderCamera = controlCamera != null ? controlCamera->SceneCamera.RenderCamera : null;
        if (renderCamera == null)
            return;

        Origin = renderCamera->Origin;
        View = renderCamera->ViewMatrix;
        View.M44 = 1; // for whatever reason, game doesn't initialize it...
        Proj = renderCamera->ProjectionMatrix;
        ViewProj = View * Proj;

        // note that game uses reverse-z by default, so we can't just get full plane equation by reading column 3 of vp matrix
        // so just calculate it manually: column 3 of view matrix is plane equation for a plane equation going through origin
        // proof:
        // plane equation p is such that p.dot(Q, 1) = 0 if Q lines on the plane => pw = -Q.dot(n); for view matrix, V43 is -origin.dot(forward)
        // plane equation for near plane has Q.dot(n) = O.dot(n) - near => pw = V43 + near
        NearPlane = new(View.M13, View.M23, View.M33, View.M43 + renderCamera->NearPlane);

        CameraAzimuth = MathF.Atan2(View.M13, View.M33);
        CameraAltitude = MathF.Asin(View.M23);
        var device = FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.Device.Instance();
        ViewportSize = new(device->Width, device->Height);
    }

    public void DrawWorldPrimitives()
    {
        if (_worldDrawLines.Count == 0 && _worldDrawQuads.Count == 0)
            return;

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0, 0));
        ImGuiHelpers.ForceNextWindowMainViewport();
        ImGuiHelpers.SetNextWindowPosRelativeMainViewport(new Vector2(0, 0));
        ImGui.Begin("world_overlay", ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoBackground);
        ImGui.SetWindowSize(ImGui.GetIO().DisplaySize);

        var dl = ImGui.GetWindowDrawList();
        foreach (var l in _worldDrawLines)
            dl.AddLine(l.from, l.to, l.col, l.col == 0xff0000ff ? 1 : 3);
        _worldDrawLines.Clear();

        foreach (var l in _worldDrawQuads)
            dl.AddQuadFilled(l.a, l.b, l.c, l.d, l.col);
        _worldDrawQuads.Clear();

        ImGui.End();
        ImGui.PopStyleVar();
    }

    public void DrawWorldLine(Vector3 start, Vector3 end, uint color)
    {
        var p1 = start;
        var p2 = end;
        if (!ClipLineToNearPlane(ref p1, ref p2))
            return;

        if (!Service.GameGui.WorldToScreen(p1, out var s1))
            return;
        if (!Service.GameGui.WorldToScreen(p2, out var s2))
            return;

        _worldDrawLines.Add((s1, s2, color));
    }

    private unsafe bool ClipLineToNearPlane(ref Vector3 a, ref Vector3 b)
    {
        var an = Vector4.Dot(new(a, 1), NearPlane);
        var bn = Vector4.Dot(new(b, 1), NearPlane);
        if (an >= 0 && bn >= 0)
            return false; // line fully behind near plane

        if (an > 0 || bn > 0)
        {
            var ab = b - a;
            var abn = Vector3.Dot(ab, new Vector3(NearPlane.X, NearPlane.Y, NearPlane.Z));
            var t = -an / abn;
            var p = a + t * ab;
            if (an > 0)
                a = p;
            else
                b = p;
        }
        return true;

    }
}
