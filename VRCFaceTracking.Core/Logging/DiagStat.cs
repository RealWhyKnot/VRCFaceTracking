namespace VRCFaceTracking.Core.Logging;

public sealed class DiagStat
{
    private readonly double[] _samples;
    private int _count;
    private int _next;
    private double _max;
    private double _sum;

    public DiagStat(int capacity = 4096) => _samples = new double[capacity];

    public int Count
    {
        get; private set;
    }

    public double Max => _max;

    public double Mean => Count == 0 ? 0 : _sum / Count;

    public void Add(double value)
    {
        Count++;
        _sum += value;
        if (value > _max)
        {
            _max = value;
        }

        _samples[_next] = value;
        _next = (_next + 1) % _samples.Length;
        if (_count < _samples.Length)
        {
            _count++;
        }
    }

    public (double p50, double p99) Percentiles()
    {
        if (_count == 0)
        {
            return (0, 0);
        }

        var copy = new double[_count];
        Array.Copy(_samples, copy, _count);
        Array.Sort(copy);
        return (copy[_count / 2], copy[Math.Min(_count - 1, (int)(_count * 0.99))]);
    }

    public void Reset()
    {
        _count = 0;
        _next = 0;
        _max = 0;
        _sum = 0;
        Count = 0;
    }
}
