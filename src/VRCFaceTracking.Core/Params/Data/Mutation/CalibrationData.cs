using VRCFaceTracking.Core.Params.Expressions;

namespace VRCFaceTracking.Core.Params.Data.Mutation;

public sealed class CalibrationData
{
    public IReadOnlyList<CalibrationParameter> Shapes { get; set; } = CreateShapes();

    public void Clear() => Shapes = CreateShapes();

    private static CalibrationParameter[] CreateShapes()
    {
        var shapes = new CalibrationParameter[(int)UnifiedExpressions.Max];
        for (int i = 0; i < shapes.Length; i++)
        {
            shapes[i] = new CalibrationParameter { Name = ((UnifiedExpressions)i).ToString() };
        }

        return shapes;
    }
}
