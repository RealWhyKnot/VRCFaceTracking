using System.Security.Cryptography;
using System.Text;
using VRCFaceTracking.Core.Contracts.Services;

namespace VRCFaceTracking.Services;

internal sealed class IdentityService : IIdentityService
{
    private string _uniqueUserId = string.Empty;

    public string GetUniqueUserId()
    {
        if (!string.IsNullOrEmpty(_uniqueUserId))
        {
            return _uniqueUserId;
        }

        string systemId = Environment.MachineName + Environment.UserName + Environment.OSVersion.Platform;
        byte[] machineIdBytes = Encoding.UTF8.GetBytes(systemId);
        byte[] hashed = SHA256.HashData(machineIdBytes);
        _uniqueUserId = Convert.ToHexString(hashed);

        return _uniqueUserId;
    }
}
