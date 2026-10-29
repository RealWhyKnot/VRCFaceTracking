using System.ComponentModel.DataAnnotations;
using System.Net;

namespace VRCFaceTracking.Core.Validation;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class ValidIpAddressAttribute : ValidationAttribute
{
    public override bool IsValid(object? value) => value is string ipString && !string.IsNullOrWhiteSpace(ipString) && IPAddress.TryParse(ipString, out _);
}
