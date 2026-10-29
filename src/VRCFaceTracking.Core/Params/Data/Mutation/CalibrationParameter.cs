using Microsoft.Extensions.Logging;

namespace VRCFaceTracking.Core.Params.Data.Mutation;

public sealed partial class CalibrationParameter
{
    private readonly float[] _dataPoints = new float[Calibration.WindowSize];
    private int _rollingIndex;
    private int _fixedIndex;
    private bool _finished;
    private float _currentStep;

    public string Name { get; set; } = string.Empty;

    public float Progress { get; set; }

    public float Max { get; set; }

    public void UpdateCalibration(float currentValue, bool continuous, ILogger logger, float deltaTime)
    {
        float step = Calibration.StepDelta * deltaTime;
        if (float.IsNaN(_currentStep) || Math.Abs(currentValue - _currentStep) >= step)
        {
            if (_fixedIndex < _dataPoints.Length)
            {
                _fixedIndex++;
                Progress = _fixedIndex / (float)_dataPoints.Length;
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
