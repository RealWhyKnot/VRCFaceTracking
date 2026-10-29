using VRCFaceTracking.Core.Contracts;

namespace VRCFaceTracking.Perf.Tests;

internal sealed record ParameterDefinition(string Address, string Name, Type Type) : IParameterDefinition;
