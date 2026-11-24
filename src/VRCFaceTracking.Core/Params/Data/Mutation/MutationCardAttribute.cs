namespace VRCFaceTracking.Core.Params.Data.Mutation;

[AttributeUsage(AttributeTargets.Class)]
public sealed class MutationCardAttribute : Attribute
{
    public MutationCardAttribute(TrackingCard card)
    {
        Card = card;
    }

    public TrackingCard Card { get; }

    public int Order { get; set; }
}
