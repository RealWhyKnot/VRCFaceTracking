using System.Text.Json;
using Microsoft.Extensions.Logging;
using VRCFaceTracking.Core.Contracts;
using VRCFaceTracking.Core.Models.ParameterDefinition.FileBased;
using VRCFaceTracking.Core.Params;
using VRCFaceTracking.Core.Services;

namespace VRCFaceTracking.Core;

public sealed partial class AvatarConfigParser
{
    private readonly ILogger<AvatarConfigParser> _logger;

    public AvatarConfigParser(ILogger<AvatarConfigParser> parserLogger)
    {
        _logger = parserLogger;
    }

    public async Task<(IAvatarInfo avatarInfo, List<Parameter> relevantParameters)?> ParseAvatarAsync(string? newId, bool skipParams, bool fullFaceTracking, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(newId))
        {
            return null;
        }

        var paramList = new List<Parameter>();

        if (string.IsNullOrEmpty(VRChat.VRCOSCDirectory) || !Directory.Exists(VRChat.VRCOSCDirectory))
        {
            LogVRChatOscDirectoryNotFound(_logger, VRChat.VRCOSCDirectory);
            return null;
        }

        AvatarConfigFile? avatarConfig = await FindAvatarConfigAsync(newId, cancellationToken).ConfigureAwait(false);
        if (avatarConfig == null)
        {
            LogAvatarConfigFileNotFound(_logger, newId);
            return null;
        }

        avatarConfig.FullFaceTracking = fullFaceTracking;

        if (!skipParams)
        {
            LogParsingConfigFileAvatar(_logger, avatarConfig.Name);
            ParameterSenderService.Clear();
            IReadOnlyList<IParameterDefinition> parameters = [.. avatarConfig.ConfigParameters.Where(param => param.Input != null)];
            var leftToVrchat = new List<string>();
            paramList.AddRange(AvatarParameters.Reset(parameters, fullFaceTracking, leftToVrchat));
            if (leftToVrchat.Count > 0 && _logger.IsEnabled(LogLevel.Information))
            {
                string driven = string.Join(", ", leftToVrchat);
                LogAvatarUsesVRChatFaceTracking(_logger, avatarConfig.Id, leftToVrchat.Count, driven);
            }
        }

        return (avatarConfig, paramList);
    }

    private async Task<AvatarConfigFile?> FindAvatarConfigAsync(string avatarId, CancellationToken cancellationToken)
    {
        foreach (string? userFolder in Directory.GetDirectories(VRChat.VRCOSCDirectory)
                     .Where(folder => Directory.Exists(Path.Combine(folder, "Avatars"))))
        {
            foreach (string avatarFile in Directory.GetFiles(Path.Combine(userFolder, "Avatars"), "*.json"))
            {
                try
                {
                    string configText = await File.ReadAllTextAsync(avatarFile, cancellationToken).ConfigureAwait(false);
                    AvatarConfigFile? config = JsonSerializer.Deserialize<AvatarConfigFile>(configText);
                    if (config != null && string.Equals(config.Id, avatarId, StringComparison.Ordinal))
                    {
                        return config;
                    }
                }
                catch (JsonException ex)
                {
                    string backupFileName = Path.ChangeExtension(avatarFile, ".bak");
                    LogMalformedJsonFileDetectedRenaming(_logger, avatarFile, backupFileName, ex.Message);

                    try
                    {
                        File.Move(avatarFile, backupFileName, overwrite: true);
                    }
                    catch (Exception moveEx) when (moveEx is IOException or UnauthorizedAccessException)
                    {
                        LogFailedRenameMalformedJsonFile(_logger, avatarFile, moveEx.Message);
                    }
                }
            }
        }

        return null;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "VRChat OSC directory not found at {Path}")]
    private static partial void LogVRChatOscDirectoryNotFound(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Malformed JSON file detected: {FileName}. Renaming to {BackupFileName}. Error: {Error}")]
    private static partial void LogMalformedJsonFileDetectedRenaming(ILogger logger, string fileName, string backupFileName, string error);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to rename malformed JSON file {FileName}: {Error}")]
    private static partial void LogFailedRenameMalformedJsonFile(ILogger logger, string fileName, string error);

    [LoggerMessage(Level = LogLevel.Error, Message = "Avatar config file for {AvatarId} not found")]
    private static partial void LogAvatarConfigFileNotFound(ILogger logger, string avatarId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Parsing config file for avatar: {AvatarName}")]
    private static partial void LogParsingConfigFileAvatar(ILogger logger, string avatarName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Avatar {AvatarId} uses VRChat face tracking. Not sending {Count} parameters VRChat drives: {Parameters}")]
    private static partial void LogAvatarUsesVRChatFaceTracking(ILogger logger, string avatarId, int count, string parameters);
}
