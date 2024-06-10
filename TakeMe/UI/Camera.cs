using Dalamud.Interface.Utility;
using Dalamud.Utility;
using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;

namespace TakeMe;
internal class Camera
{
    public static Camera? Instance;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr GetMatrixSingletonDelegate();

    private GetMatrixSingletonDelegate _getMatrixSingleton { get; init; }

    public SharpDX.Matrix ViewProj { get; private set; }
    public SharpDX.Matrix Proj { get; private set; }
    public SharpDX.Matrix View { get; private set; }
    public SharpDX.Matrix CameraWorld { get; private set; }
    public float CameraAzimuth { get; private set; } // facing north = 0, facing west = pi/4, facing south = +-pi/2, facing east = -pi/4
    public float CameraAltitude { get; private set; } // facing horizontally = 0, facing down = pi/4, facing up = -pi/4
    public SharpDX.Vector2 ViewportSize { get; private set; }

    private readonly List<(Vector2 from, Vector2 to, uint col)> _worldDrawLines = [];
    private readonly List<(Vector2 a, Vector2 b, Vector2 c, Vector2 d, uint col)> _worldDrawQuads = [];

    public Camera()
    {
        var funcAddress = Service
            .SigScanner
            .ScanText("E8 ?? ?? ?? ?? 48 8D 4C 24 ?? 48 89 4c 24 ?? 4C 8D 4D ?? 4C 8D 44 24 ??");
        _getMatrixSingleton = Marshal.GetDelegateForFunctionPointer<GetMatrixSingletonDelegate>(funcAddress);
    }

    public void Update()
    {
        var matrixSingleton = _getMatrixSingleton();
        ViewProj = ReadMatrix(matrixSingleton + 0x1b4);
        Proj = ReadMatrix(matrixSingleton + 0x174);
        View = ViewProj * SharpDX.Matrix.Invert(Proj);
        CameraWorld = SharpDX.Matrix.Invert(View);
        CameraAzimuth = MathF.Atan2(View.Column3.X, View.Column3.Z);
        CameraAltitude = MathF.Asin(View.Column3.Y);
        ViewportSize = ReadVec2(matrixSingleton + 0x1f4);
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

    private unsafe SharpDX.Matrix ReadMatrix(IntPtr address)
    {
        var p = (float*)address;
        SharpDX.Matrix mtx = new();
        for (var i = 0; i < 16; i++)
            mtx[i] = *p++;
        return mtx;
    }

    private unsafe SharpDX.Vector2 ReadVec2(IntPtr address)
    {
        var p = (float*)address;
        return new(p[0], p[1]);
    }

    public void DrawWorldLine(Vector3 start, Vector3 end, uint color)
    {
        var p1 = start.ToSharpDX();
        var p2 = end.ToSharpDX();
        if (!ClipLineToNearPlane(ref p1, ref p2))
            return;

        _worldDrawLines.Add((WorldToScreen(p1), WorldToScreen(p2), color));
    }

    public Vector2 WorldToScreen(Vector3 pos) => WorldToScreen(new SharpDX.Vector3(pos.X, pos.Y, pos.Z));

    public Vector2 WorldToScreen(SharpDX.Vector3 pos)
    {
        var p1 = SharpDX.Vector3.TransformCoordinate(pos, ViewProj);
        return
            new Vector2(0.5f * ViewportSize.X * (1 + p1.X), 0.5f * ViewportSize.Y * (1 - p1.Y))
            + ImGuiHelpers.MainViewport.Pos;
    }

    private bool ClipLineToNearPlane(ref SharpDX.Vector3 a, ref SharpDX.Vector3 b)
    {
        var n = ViewProj.Column3; // near plane
        var an = SharpDX.Vector4.Dot(new(a, 1), n);
        var bn = SharpDX.Vector4.Dot(new(b, 1), n);
        if (an <= 0 && bn <= 0)
            return false;

        if (an < 0 || bn < 0)
        {
            var ab = b - a;
            var abn = SharpDX.Vector3.Dot(ab, new(n.X, n.Y, n.Z));
            var t = -an / abn;
            if (an < 0)
                a += t * ab;
            else
                b = a + (t * ab);
        }
        return true;
    }
}
