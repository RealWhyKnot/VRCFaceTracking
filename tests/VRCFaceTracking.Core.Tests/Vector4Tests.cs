using VRCFaceTracking.Core.Osc;
using VRCFaceTracking.Core.Types;

namespace VRCFaceTracking.Core.Tests;

public sealed class Vector4Tests
{
    [Fact]
    public void EqualityComparesEveryComponent()
    {
        var a = new Vector4(1, 2, 3, 4);

        Assert.True(a.Equals(new Vector4(1, 2, 3, 4)));
        Assert.False(a.Equals(new Vector4(1, 2, 3, 5)));
        Assert.False(a.Equals(new Vector4(0, 2, 3, 4)));
        Assert.True(EqualityComparer<Vector4>.Default.Equals(a, new Vector4(1, 2, 3, 4)));
        Assert.Equal(a.GetHashCode(), new Vector4(1, 2, 3, 4).GetHashCode());
    }

    [Fact]
    public void EqualityKeepsFloatEqualsSemantics()
    {
        Assert.True(new Vector4(float.NaN, 0, 0, 0).Equals(new Vector4(float.NaN, 0, 0, 0)));
        Assert.True(new Vector4(0f, 0, 0, 0).Equals(new Vector4(-0f, 0, 0, 0)));
        Assert.True(new Vector4(1, 2, 3, 4).Equals((object)new Vector4(1, 2, 3, 4)));
        Assert.False(new Vector4(1, 2, 3, 4).Equals("not a vector"));
    }

    [Fact]
    public void OscMessageSetValueWritesFieldsInDeclarationOrder()
    {
        var typed = new OscMessage("/v", typeof(Vector4));
        var boxed = new OscMessage("/v", typeof(Vector4));

        typed.SetValue(new Vector4(1, 2, 3, 4));
        boxed.Value = new Vector4(1, 2, 3, 4);

        Assert.Equal(4, typed._values.Length);
        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(OscValueType.Float, typed._values[i].Type);
            Assert.Equal(i + 1f, typed._values[i].FloatValue);
            Assert.Equal(boxed._values[i].FloatValue, typed._values[i].FloatValue);
        }
    }

    [Fact]
    public void OscMessageSetValueDoesNotAllocate()
    {
        var message = new OscMessage("/v", typeof(Vector4));
        var value = new Vector4(1, 2, 3, 4);
        message.SetValue(value);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            value.x = i;
            message.SetValue(value);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal(999f, message._values[1].FloatValue);
    }
}
