using System.Globalization;
using System.Text;

namespace VRCFaceTracking.Core.Library;

internal sealed class IpcLatencyRing
{
    internal const int Capacity = 16;

    private readonly (float Module, float Transit)[] _samples = new (float, float)[Capacity];
    private readonly Lock _gate = new();
    private int _next;
    private int _count;

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _count;
            }
        }
    }

    public void Add(double moduleMs, double transitMs)
    {
        lock (_gate)
        {
            _samples[_next] = ((float)moduleMs, (float)transitMs);
            _next = (_next + 1) % Capacity;
            _count = Math.Min(_count + 1, Capacity);
        }
    }

    public override string ToString() => Describe();

    public string Describe()
    {
        lock (_gate)
        {
            var text = new StringBuilder();
            for (int i = 0; i < _count; i++)
            {
                (float module, float transit) = _samples[(((_next - _count + i) % Capacity) + Capacity) % Capacity];
                if (i > 0)
                {
                    text.Append(' ');
                }

                text.Append(module.ToString("0", CultureInfo.InvariantCulture))
                    .Append('/')
                    .Append(transit.ToString("0", CultureInfo.InvariantCulture));
            }

            return text.ToString();
        }
    }
}
