using Dalamud.Plugin.Ipc;
using System.Collections.Generic;
using System.Numerics;

namespace TakeMe;

public class IPC
{
    private ICallGateSubscriber<Vector3, bool, bool> _pathfindAndMoveTo;
    private ICallGateSubscriber<object> _pathStop;
    private ICallGateSubscriber<object> _pathfindCancel;
    private ICallGateSubscriber<Vector3, bool, float, Vector3?> _pointOnFloor;
    private ICallGateSubscriber<float> _pathTolerance;
    private ICallGateSubscriber<List<Vector3>> _pathWaypoints;

    public IPC()
    {
        _pathfindAndMoveTo = Service.PluginInterface.GetIpcSubscriber<Vector3, bool, bool>("vnavmesh.SimpleMove.PathfindAndMoveTo");
        _pathStop = Service.PluginInterface.GetIpcSubscriber<object>("vnavmesh.Path.Stop");
        _pathfindCancel = Service.PluginInterface.GetIpcSubscriber<object>("vnavmesh.Nav.PathfindCancelAll");
        _pointOnFloor = Service.PluginInterface.GetIpcSubscriber<Vector3, bool, float, Vector3?>("vnavmesh.Query.Mesh.PointOnFloor");
        _pathTolerance = Service.PluginInterface.GetIpcSubscriber<float>("vnavmesh.Path.GetTolerance");
        _pathWaypoints = Service.PluginInterface.GetIpcSubscriber<List<Vector3>>("vnavmesh.Path.ListWaypoints");
    }

    public void PathfindCancel() => _pathfindCancel.InvokeAction();
    public bool PathfindAndMoveTo(Vector3 pos, bool fly)
    {
        _pathStop.InvokeAction();
        return _pathfindAndMoveTo.InvokeFunc(pos, fly);
    }
    public float PathTolerance => _pathTolerance.InvokeFunc();
    public List<Vector3> PathWaypoints => _pathWaypoints.InvokeFunc();
    public Vector3? PointOnFloor(Vector3 center, bool allowUnlandable, float radius) => _pointOnFloor.InvokeFunc(center, allowUnlandable, radius);
}
