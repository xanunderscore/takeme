using System.Numerics;
using Dalamud.Plugin.Ipc;

namespace TakeMe;

public class IPC {
    private ICallGateSubscriber<Vector3, bool, bool> _pathfindAndMoveTo;
    private ICallGateSubscriber<object> _pathStop;
    private ICallGateSubscriber<object> _pathfindCancel;
    private ICallGateSubscriber<Vector3, bool, float, Vector3?> _pointOnFloor;

    public IPC() {
        _pathfindAndMoveTo = Service.PluginInterface.GetIpcSubscriber<Vector3, bool, bool>("vnavmesh.SimpleMove.PathfindAndMoveTo");
        _pathStop = Service.PluginInterface.GetIpcSubscriber<object>("vnavmesh.Path.Stop");
        _pathfindCancel = Service.PluginInterface.GetIpcSubscriber<object>("vnavmesh.Nav.PathfindCancelAll");
        _pointOnFloor = Service.PluginInterface.GetIpcSubscriber<Vector3, bool, float, Vector3?>("vnavmesh.Query.Mesh.PointOnFloor");
    }

    public void PathfindCancel() => _pathfindCancel.InvokeAction();
    public bool PathfindAndMoveTo(Vector3 pos, bool fly) {
        _pathStop.InvokeAction();
        return _pathfindAndMoveTo.InvokeFunc(pos, fly);
    }
    public Vector3? PointOnFloor(Vector3 center, bool allowUnlandable, float radius) => _pointOnFloor.InvokeFunc(center, allowUnlandable, radius);
}
