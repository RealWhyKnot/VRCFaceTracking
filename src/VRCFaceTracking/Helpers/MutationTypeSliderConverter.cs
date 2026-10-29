using VRCFaceTracking.Core.Params.Data.Mutation;

namespace VRCFaceTracking.Helpers;

internal sealed class MutationTypeSliderConverter : MutationTypeEqualityConverter
{
    protected override MutationPropertyType Expected => MutationPropertyType.Slider;
}
