using VRCFaceTracking.Core.Params.Data.Mutation;

namespace VRCFaceTracking.Helpers;

internal sealed class MutationTypeTextConverter : MutationTypeEqualityConverter
{
    protected override MutationPropertyType Expected => MutationPropertyType.TextBox;
}
