using VRCFaceTracking.Core.Params.Data.Mutation;

namespace VRCFaceTracking.Helpers;

internal sealed class MutationTypeCheckboxConverter : MutationTypeEqualityConverter
{
    protected override MutationPropertyType Expected => MutationPropertyType.CheckBox;
}
