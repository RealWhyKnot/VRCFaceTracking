using System.Globalization;
using System.Text;
using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Core.Params.Expressions;

namespace VRCFaceTracking.Core.Logging;

public sealed class ShapeStats
{
    public const int Bins = 40;
    public const float ActiveAbove = 0.05f;

    public static readonly int ShapeCount = (int)UnifiedExpressions.Max;
    public static readonly string[] ChannelNames = BuildChannelNames();

    private readonly int[] _count = new int[ChannelNames.Length];
    private readonly double[] _sum = new double[ChannelNames.Length];
    private readonly float[] _min = new float[ChannelNames.Length];
    private readonly float[] _max = new float[ChannelNames.Length];
    private readonly int[] _active = new int[ChannelNames.Length];
    private readonly int[] _histogram = new int[ChannelNames.Length * Bins];

    public ShapeStats()
    {
        Reset();
    }

    public int Samples
    {
        get; private set;
    }

    public void Add(UnifiedTrackingData data)
    {
        UnifiedExpressionShape[] shapes = data.Shapes;
        int shapeCount = Math.Min(ShapeCount, shapes.Length);
        for (int i = 0; i < shapeCount; i++)
        {
            Put(i, shapes[i].Weight);
        }

        int eye = ShapeCount;
        Put(eye, data.Eye.Left.Openness);
        Put(eye + 1, data.Eye.Right.Openness);
        Put(eye + 2, data.Eye.Left.Gaze.x);
        Put(eye + 3, data.Eye.Left.Gaze.y);
        Put(eye + 4, data.Eye.Right.Gaze.x);
        Put(eye + 5, data.Eye.Right.Gaze.y);
        Samples++;
    }

    public int Count(int channel) => _count[channel];

    public float Min(int channel) => _count[channel] == 0 ? 0 : _min[channel];

    public float Max(int channel) => _count[channel] == 0 ? 0 : _max[channel];

    public double Mean(int channel) => _count[channel] == 0 ? 0 : _sum[channel] / _count[channel];

    public double ActivePercent(int channel) => _count[channel] == 0 ? 0 : 100.0 * _active[channel] / _count[channel];

    public bool IsZero(int channel) => _count[channel] == 0 || (_min[channel] == 0 && _max[channel] == 0);

    public float Percentile(int channel, double fraction)
    {
        int count = _count[channel];
        if (count == 0)
        {
            return 0;
        }

        int target = Math.Max(1, (int)Math.Ceiling(fraction * count));
        int seen = 0;
        int offset = channel * Bins;
        for (int bin = 0; bin < Bins; bin++)
        {
            seen += _histogram[offset + bin];
            if (seen >= target)
            {
                float upper = -1f + ((bin + 1) * 2f / Bins);
                return Math.Clamp(upper, _min[channel], _max[channel]);
            }
        }

        return _max[channel];
    }

    public void AppendSummary(StringBuilder moving, StringBuilder zero)
    {
        for (int channel = 0; channel < ChannelNames.Length; channel++)
        {
            if (IsZero(channel))
            {
                if (zero.Length > 0)
                {
                    zero.Append(',');
                }
                zero.Append(ChannelNames[channel]);
                continue;
            }

            if (moving.Length > 0)
            {
                moving.Append(' ');
            }
            moving.Append(ChannelNames[channel]).Append('=')
                .Append(Min(channel).ToString("0.00", CultureInfo.InvariantCulture)).Append('/')
                .Append(Mean(channel).ToString("0.00", CultureInfo.InvariantCulture)).Append('/')
                .Append(Percentile(channel, 0.95).ToString("0.00", CultureInfo.InvariantCulture)).Append('/')
                .Append(Max(channel).ToString("0.00", CultureInfo.InvariantCulture)).Append('/')
                .Append(ActivePercent(channel).ToString("0", CultureInfo.InvariantCulture)).Append('%');
            if (_count[channel] != Samples)
            {
                moving.Append("/nan=").Append(Samples - _count[channel]);
            }
        }
    }

    public void Reset()
    {
        Array.Clear(_count);
        Array.Clear(_sum);
        Array.Fill(_min, float.MaxValue);
        Array.Fill(_max, float.MinValue);
        Array.Clear(_active);
        Array.Clear(_histogram);
        Samples = 0;
    }

    private void Put(int channel, float value)
    {
        if (!float.IsFinite(value))
        {
            return;
        }

        _count[channel]++;
        _sum[channel] += value;
        if (value < _min[channel])
        {
            _min[channel] = value;
        }
        if (value > _max[channel])
        {
            _max[channel] = value;
        }
        if (Math.Abs(value) > ActiveAbove)
        {
            _active[channel]++;
        }

        int bin = Math.Clamp((int)((value + 1f) * 0.5f * Bins), 0, Bins - 1);
        _histogram[(channel * Bins) + bin]++;
    }

    private static string[] BuildChannelNames()
    {
        int shapeCount = (int)UnifiedExpressions.Max;
        string[] names = new string[shapeCount + 6];
        for (int i = 0; i < shapeCount; i++)
        {
            names[i] = Enum.GetName((UnifiedExpressions)i) ?? i.ToString(CultureInfo.InvariantCulture);
        }

        names[shapeCount] = "EyeOpennessLeft";
        names[shapeCount + 1] = "EyeOpennessRight";
        names[shapeCount + 2] = "EyeGazeLeftX";
        names[shapeCount + 3] = "EyeGazeLeftY";
        names[shapeCount + 4] = "EyeGazeRightX";
        names[shapeCount + 5] = "EyeGazeRightY";
        return names;
    }
}
