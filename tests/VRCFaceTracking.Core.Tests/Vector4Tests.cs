using VRCFaceTracking.Core.OSC;
using VRCFaceTracking.Core.Types;

namespace VRCFaceTracking.Core.Tests;

public class Vector4Tests
{
    [Fact]
    public void Equality_ComparesEveryComponent()
    {
        var a = new Vector4(1, 2, 3, 4);

        Assert.True(a.Equals(new Vector4(1, 2, 3, 4)));
        Assert.False(a.Equals(new Vector4(1, 2, 3, 5)));
        Assert.False(a.Equals(new Vector4(0, 2, 3, 4)));
        Assert.True(EqualityComparer<Vector4>.Default.Equals(a, new Vector4(1, 2, 3, 4)));
        Assert.Equal(a.GetHashCode(), new Vector4(1, 2, 3, 4).GetHashCode());
    }

    [Fact]
    public void Equality_KeepsFloatEqualsSemantics()
    {
        Assert.True(new Vector4(float.NaN, 0, 0, 0).Equals(new Vector4(float.NaN, 0, 0, 0)));
        Assert.True(new Vector4(0f, 0, 0, 0).Equals(new Vector4(-0f, 0, 0, 0)));
        Assert.True(new Vector4(1, 2, 3, 4).Equals((object)new Vector4(1, 2, 3, 4)));
        Assert.False(new Vector4(1, 2, 3, 4).Equals((object)"not a vector"));
    }

    [Fact]
    public void OscMessage_SetValueWritesFieldsInDeclarationOrder()
    {
        var typed = new OscMessage("/v", typeof(Vector4));
        var boxed = new OscMessage("/v", typeof(Vector4));

        typed.SetValue(new Vector4(1, 2, 3, 4));
        boxed.Value = new Vector4(1, 2, 3, 4);

        Assert.Equal(4, typed.Values.Length);
        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(OscValueType.Float, typed.Values[i].Type);
            Assert.Equal(i + 1f, typed.Values[i].FloatValue);
            Assert.Equal(boxed.Values[i].FloatValue, typed.Values[i].FloatValue);
        }
    }

    [Fact]
    public void OscMessage_SetValueDoesNotAllocate()
    {
        var message = new OscMessage("/v", typeof(Vector4));
        var value = new Vector4(1, 2, 3, 4);
        message.SetValue(value);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            value.x = i;
            message.SetValue(value);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal(999f, message.Values[1].FloatValue);
    }
}
