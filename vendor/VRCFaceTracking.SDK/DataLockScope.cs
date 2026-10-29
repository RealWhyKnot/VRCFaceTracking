namespace VRCFaceTracking;

public readonly struct DataLockScope : IDisposable
{
    private readonly object _lockObject;

    internal DataLockScope(object lockObject) => _lockObject = lockObject;

    public void Dispose()
    {
        if (_lockObject is not null)
        {
            Monitor.Exit(_lockObject);
        }
    }
}
