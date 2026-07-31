using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;
using System.Collections.Generic;
using System.Numerics;

namespace TakeMe;

public class IPC
{
    private readonly ICallGateSubscriber<Vector3, bool, float, bool> _pathfindAndMoveTo;
    private readonly ICallGateSubscriber<object> _pathStop;
    private readonly ICallGateSubscriber<object> _pathfindCancel;
    private readonly ICallGateSubscriber<Vector3, bool, float, Vector3?> _pointOnFloor;
    private readonly ICallGateSubscriber<float> _pathTolerance;
    private readonly ICallGateSubscriber<List<Vector3>> _pathWaypoints;
    private readonly ICallGateSubscriber<bool> _pathfindInProgress;
    private readonly ICallGateSubscriber<int> _pathfindNumQueued;
    private readonly ICallGateSubscriber<bool> _pathIsRunning;

    public IPC()
    {
        _pathfindAndMoveTo = Service.PluginInterface.GetIpcSubscriber<Vector3, bool, float, bool>("vnavmesh.SimpleMove.PathfindAndMoveCloseTo");
        _pathStop = Service.PluginInterface.GetIpcSubscriber<object>("vnavmesh.Path.Stop");
        _pathfindCancel = Service.PluginInterface.GetIpcSubscriber<object>("vnavmesh.Nav.PathfindCancelAll");
        _pointOnFloor = Service.PluginInterface.GetIpcSubscriber<Vector3, bool, float, Vector3?>("vnavmesh.Query.Mesh.PointOnFloor");
        _pathTolerance = Service.PluginInterface.GetIpcSubscriber<float>("vnavmesh.Path.GetTolerance");
        _pathWaypoints = Service.PluginInterface.GetIpcSubscriber<List<Vector3>>("vnavmesh.Path.ListWaypoints");
        _pathfindInProgress = Service.PluginInterface.GetIpcSubscriber<bool>("vnavmesh.Nav.PathfindInProgress");
        _pathfindNumQueued = Service.PluginInterface.GetIpcSubscriber<int>("vnavmesh.Nav.PathfindNumQueued");
        _pathIsRunning = Service.PluginInterface.GetIpcSubscriber<bool>("vnavmesh.Path.IsRunning");
    }

    public void PathfindCancel() => _pathfindCancel.InvokeAction();
    public bool PathfindAndMoveTo(Vector3 pos, bool fly, float tolerance = 0)
    {
        _pathStop.InvokeAction();
        return _pathfindAndMoveTo.InvokeFunc(pos, fly, tolerance);
    }
    public float PathTolerance => _pathTolerance.InvokeFunc();
    public List<Vector3> PathWaypoints => _pathWaypoints.InvokeFunc();
    public Vector3? PointOnFloor(Vector3 center, bool allowUnlandable, float radius) => _pointOnFloor.InvokeFunc(center, allowUnlandable, radius);
    public bool PathActive => _pathfindInProgress.InvokeFunc() || _pathfindNumQueued.InvokeFunc() > 0 || _pathIsRunning.InvokeFunc();

    public bool IPCReady()
    {
        try
        {
            _pathfindInProgress.InvokeFunc();
            return true;
        }
        catch (IpcNotReadyError)
        {
            return false;
        }
    }
}
