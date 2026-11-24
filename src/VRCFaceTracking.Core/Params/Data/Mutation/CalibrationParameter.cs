using Microsoft.Extensions.Logging;

namespace VRCFaceTracking.Core.Params.Data.Mutation;

public sealed partial class CalibrationParameter
{
    private float[] _dataPoints = new float[Calibration.DefaultWindowSize];
    private float _progress;
    private int _rollingIndex;
    private int _fixedIndex;
    private bool _finished;
    private bool _restored;
    private float _currentStep;

    public string Name { get; set; } = string.Empty;

    public float Progress
    {
        get => _progress;
        set
        {
            _progress = float.IsFinite(value) ? Math.Clamp(value, 0f, 1f) : 0f;
            if (_fixedIndex == 0 && _progress > 0f)
            {
                _fixedIndex = Math.Clamp((int)MathF.Round(_progress * _dataPoints.Length), 0, _dataPoints.Length);
                _restored = true;
            }
        }
    }

    public float Max { get; set; }

    public void Resize(int windowSize)
    {
        if (windowSize == _dataPoints.Length)
        {
            return;
        }

        if (_restored)
        {
            _dataPoints = new float[windowSize];
            _fixedIndex = Math.Clamp((int)MathF.Round(_progress * windowSize), 0, windowSize);
            _rollingIndex = _fixedIndex % windowSize;
            _finished = false;
            return;
        }

        int old = _dataPoints.Length;
        int kept = Math.Min(Math.Min(_fixedIndex, old), windowSize);
        float[] resized = new float[windowSize];
        for (int i = 0; i < kept; i++)
        {
            resized[kept - 1 - i] = _dataPoints[(((_rollingIndex - 1 - i) % old) + old) % old];
        }

        _dataPoints = resized;
        _fixedIndex = kept;
        _rollingIndex = kept % windowSize;
        _finished = false;
        _progress = kept / (float)windowSize;
    }

    public void UpdateCalibration(float currentValue, bool continuous, ILogger logger, float deltaTime, float stepDelta, int windowSize)
    {
        Resize(windowSize);
        float step = stepDelta * deltaTime;
        if (float.IsNaN(_currentStep) || Math.Abs(currentValue - _currentStep) >= step)
        {
            _restored = false;
            if (_fixedIndex < _dataPoints.Length)
            {
                _fixedIndex++;
                _progress = _fixedIndex / (float)_dataPoints.Length;
            }
            else if (!_finished)
            {
                LogWindowSaturated(logger, Name);
                _finished = true;
            }

            _dataPoints[_rollingIndex] = currentValue;
            if (!_finished || continuous)
            {
                _rollingIndex = (_rollingIndex + 1) % _dataPoints.Length;
                UpdateMax();
            }
        }

        _currentStep = MathF.Floor(currentValue / step) * step;
    }

    public float CalculateParameter(float currentValue, float blend)
    {
        if (float.IsNaN(currentValue) || Max == 0f)
        {
            return currentValue;
        }

        float confidence = blend * Progress;
        float adjustedValue = (confidence * (currentValue / Max)) + ((1 - confidence) * currentValue);

        return float.IsFinite(adjustedValue) ? adjustedValue : currentValue;
    }

    private void UpdateMax()
    {
        if (_fixedIndex >= 0.1f * _dataPoints.Length)
        {
            float windowMax = _dataPoints.Max();
            if (windowMax > Max)
            {
                Max = windowMax;
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Data saturated window: {Name}.")]
    private static partial void LogWindowSaturated(ILogger logger, string name);
}
