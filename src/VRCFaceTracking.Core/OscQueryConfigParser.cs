using System.Net;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using VRCFaceTracking.Core.Contracts;
using VRCFaceTracking.Core.Mdns;
using VRCFaceTracking.Core.Osc.Query;
using VRCFaceTracking.Core.Params;

namespace VRCFaceTracking.Core;

public sealed partial class OscQueryConfigParser : IDisposable
{
    private readonly ILogger<OscQueryConfigParser> _parserLogger;
    private readonly AvatarConfigParser _configParser;
    private readonly MulticastDnsService _multicastDnsService;
    private readonly HttpClient _httpClient = new();

    public OscQueryConfigParser(
        ILogger<OscQueryConfigParser> parserLogger,
        AvatarConfigParser configParser,
        MulticastDnsService multicastDnsService)
    {
        _parserLogger = parserLogger;
        _configParser = configParser;
        _multicastDnsService = multicastDnsService;
    }

    public void Dispose() => _httpClient.Dispose();

    public async Task<(IAvatarInfo avatarInfo, List<Parameter> relevantParameters)?> ParseAvatarAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (_multicastDnsService.VrchatClientEndpoint == null)
            {
                return null;
            }

            var httpEndpoint = new Uri("http://" + _multicastDnsService.VrchatClientEndpoint + "/avatar");
            OscQueryNode avatarConfig = await OscQueryAvatarReader.ReadAsync(_httpClient, httpEndpoint, cancellationToken).ConfigureAwait(false);
            LogAvatarConfig(_parserLogger, avatarConfig);
            var avatarInfo = new OscQueryAvatarInfo(avatarConfig);

            var leftToVrchat = new List<string>();
            List<Parameter> paramList = AvatarParameters.Reset(avatarInfo.Parameters, avatarInfo.FullFaceTracking, leftToVrchat);
            if (leftToVrchat.Count > 0 && _parserLogger.IsEnabled(LogLevel.Information))
            {
                string driven = string.Join(", ", leftToVrchat);
                LogAvatarUsesVRChatFaceTracking(_parserLogger, avatarInfo.Id, leftToVrchat.Count, driven);
            }

            if (string.Equals(avatarInfo.Name, OscQueryAvatarInfo.UnknownName, StringComparison.Ordinal))
            {
                await ResolveNameFromConfigFileAsync(avatarInfo, cancellationToken).ConfigureAwait(false);
            }

            return (avatarInfo, paramList);
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or IOException or UnauthorizedAccessException
                                  || (e is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            LogFailedParseOSCQueryAvatarConfig(_parserLogger, e, _multicastDnsService.VrchatClientEndpoint);
            return null;
        }
    }

    public async Task<string?> GetAvatarIdAsync(IPEndPoint endpoint, CancellationToken cancellationToken)
    {
        string json = await _httpClient.GetStringAsync(new Uri("http://" + endpoint + "/avatar/change"), cancellationToken).ConfigureAwait(false);
        return JsonConvert.DeserializeObject<OscQueryNode>(json)?.Value is [string id, ..] && !string.IsNullOrWhiteSpace(id) ? id : null;
    }

    private async Task ResolveNameFromConfigFileAsync(OscQueryAvatarInfo avatarInfo, CancellationToken cancellationToken)
    {
        LogAttemptingResolveAvatarConfigFile(_parserLogger, avatarInfo.Id);
        (IAvatarInfo avatarInfo, List<Parameter> relevantParameters)? configFileInfo = await _configParser.ParseAvatarAsync(avatarInfo.Id, skipParams: true, fullFaceTracking: false, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(configFileInfo?.avatarInfo.Name))
        {
            avatarInfo.Name = configFileInfo.Value.avatarInfo.Name;
            LogSuccessfullyFoundConfigContainingAvatar(_parserLogger, avatarInfo.Name);
        }
        else
        {
            LogOddAttemptFindLegacyOsc(_parserLogger);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Avatar {AvatarId} uses VRChat face tracking. Not sending {Count} parameters VRChat drives: {Parameters}")]
    private static partial void LogAvatarUsesVRChatFaceTracking(ILogger logger, string avatarId, int count, string parameters);

    [LoggerMessage(Level = LogLevel.Information, Message = "Attempting to resolve avatar config file for {AvatarInfoId}")]
    private static partial void LogAttemptingResolveAvatarConfigFile(ILogger logger, string avatarInfoId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Successfully found config containing avatar name {AvatarInfoName}")]
    private static partial void LogSuccessfullyFoundConfigContainingAvatar(ILogger logger, string avatarInfoName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Odd. Our attempt to find the legacy osc config json for this avatar failed.")]
    private static partial void LogOddAttemptFindLegacyOsc(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to parse OSCQuery avatar config from {Endpoint}")]
    private static partial void LogFailedParseOSCQueryAvatarConfig(ILogger logger, Exception exception, IPEndPoint? endpoint);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{Config}")]
    private static partial void LogAvatarConfig(ILogger logger, OscQueryNode config);
}
