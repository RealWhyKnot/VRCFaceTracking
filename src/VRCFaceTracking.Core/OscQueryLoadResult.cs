using VRCFaceTracking.Core.Contracts;
using VRCFaceTracking.Core.Params;

namespace VRCFaceTracking.Core;

internal readonly record struct OscQueryLoadResult((IAvatarInfo avatarInfo, List<Parameter> relevantParameters)? Avatar, bool NotFound);
