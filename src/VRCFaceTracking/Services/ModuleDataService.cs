using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using VRCFaceTracking.Core.Contracts.Services;
using VRCFaceTracking.Core.Helpers;
using VRCFaceTracking.Core.Models;
using VRCFaceTracking.Models;

namespace VRCFaceTracking.Core.Services;

internal sealed partial class ModuleDataService : IModuleDataService
{
    private List<InstallableTrackingModule>? _remoteModules;
    private readonly Dictionary<Guid, int> _ratingCache = [];

    private readonly IIdentityService _identityService;
    private readonly ILogger<ModuleDataService> _logger;
    private readonly HttpClient _httpClient;

    private const string BaseUrl = "https://registry.vrcft.io/";

    public ModuleDataService(IIdentityService identityService, ILogger<ModuleDataService> logger)
    {
        _identityService = identityService;
        _logger = logger;
        _httpClient = HappyEyeballsHttp.CreateHttpClient();
        _httpClient.BaseAddress = new Uri(BaseUrl);
    }

    private async Task<IEnumerable<InstallableTrackingModule>> FetchAllModulesAsync(CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage response = await _httpClient.GetAsync(new Uri("modules", UriKind.Relative), cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return [];
            }

            string content = await response.Content.ReadAsStringAsync(cancellationToken);
            return await JsonText.ToObjectAsync<List<InstallableTrackingModule>>(content, cancellationToken) ?? [];
        }
        catch (Exception e) when (e is HttpRequestException or JsonException || (e is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            LogExceptionTryingGetModulesModule(_logger, e.Message);
            return [];
        }
    }

    public async Task<IEnumerable<InstallableTrackingModule>> GetRemoteModulesAsync(CancellationToken cancellationToken)
    {
        _remoteModules ??= [.. await FetchAllModulesAsync(cancellationToken)];

        return _remoteModules;
    }

    public async Task IncrementDownloadsAsync(TrackingModuleMetadata moduleMetadata, CancellationToken cancellationToken)
    {
        var rating = new RatingObject
        {
            UserId = _identityService.GetUniqueUserId(),
            ModuleId = moduleMetadata.ModuleId.ToString(),
        };
        using var content = new StringContent(JsonConvert.SerializeObject(rating), Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await _httpClient.PatchAsync(new Uri("downloads", UriKind.Relative), content, cancellationToken);

        if (response.StatusCode == HttpStatusCode.OK)
        {
            return;
        }

        LogFailedIncrementDownloadsStatusCode(_logger, moduleMetadata.ModuleId, response.StatusCode);
    }

    public IEnumerable<InstallableTrackingModule> GetLegacyModules()
    {
        if (!Directory.Exists(Utils.CustomLibsDirectory))
        {
            Directory.CreateDirectory(Utils.CustomLibsDirectory);
        }

        string[] moduleDlls = Directory.GetFiles(Utils.CustomLibsDirectory, "*.dll");

        return moduleDlls.Select(moduleDll => new InstallableTrackingModule
        {
            AssemblyLoadPath = moduleDll,
            DllFileName = Path.GetFileName(moduleDll),
            InstallationState = InstallState.Installed,
            ModuleId = Guid.Empty,
            ModuleName = Path.GetFileNameWithoutExtension(moduleDll),
            ModuleDescription = "Legacy module",
            AuthorName = "Local",
            ModulePageUrl = new Uri("file:///" + Path.GetDirectoryName(moduleDll)),
        });
    }

    public async Task<int?> GetMyRatingAsync(TrackingModuleMetadata moduleMetadata, CancellationToken cancellationToken)
    {
        try
        {
            if (_ratingCache.TryGetValue(moduleMetadata.ModuleId, out int cached))
            {
                LogRatingCached(_logger, moduleMetadata.ModuleId, cached);
                return cached;
            }

            var rating = new RatingObject
            {
                UserId = _identityService.GetUniqueUserId(),
                ModuleId = moduleMetadata.ModuleId.ToString(),
            };

            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("rating", UriKind.Relative))
            {
                Content = new StringContent(JsonConvert.SerializeObject(rating), Encoding.UTF8, "application/json"),
            };
            using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);

            if (response.StatusCode != HttpStatusCode.OK)
            {
                LogFailedGetUserRatingModule(_logger, moduleMetadata.ModuleId);
                return null;
            }

            RatingObject ratingResponse = await JsonText.ToObjectAsync<RatingObject>(await response.Content.ReadAsStringAsync(cancellationToken), cancellationToken);

            LogRatingCaching(_logger, moduleMetadata.ModuleId, ratingResponse.Rating);
            _ratingCache[moduleMetadata.ModuleId] = ratingResponse.Rating;
            return ratingResponse.Rating;
        }
        catch (Exception e) when (e is HttpRequestException or JsonException || (e is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            LogGetUserRatingFailed(_logger, moduleMetadata.ModuleId, e.Message);
            return null;
        }
    }

    public async Task SetMyRatingAsync(TrackingModuleMetadata moduleMetadata, int rating, CancellationToken cancellationToken)
    {
        try
        {
            var ratingObject = new RatingObject
            {
                UserId = _identityService.GetUniqueUserId(),
                ModuleId = moduleMetadata.ModuleId.ToString(),
                Rating = rating,
            };

            using var content = new StringContent(JsonConvert.SerializeObject(ratingObject), Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await _httpClient.PutAsync(new Uri("rating", UriKind.Relative), content, cancellationToken);

            if (response.StatusCode != HttpStatusCode.OK)
            {
                LogFailedSetRatingStatusCode(_logger, moduleMetadata.ModuleId, rating, response.StatusCode);
                return;
            }

            LogRatingSetCaching(_logger, moduleMetadata.ModuleId, rating);
            _ratingCache[moduleMetadata.ModuleId] = rating;
        }
        catch (Exception e) when (e is HttpRequestException || (e is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            LogSetRatingFailed(_logger, moduleMetadata.ModuleId, e.Message);
        }
    }

    public IEnumerable<InstallableTrackingModule> GetInstalledModules()
    {
        if (!Directory.Exists(Utils.CustomLibsDirectory))
        {
            Directory.CreateDirectory(Utils.CustomLibsDirectory);
        }

        var installedModules = new List<InstallableTrackingModule>();
        string[] moduleFolders = Directory.GetDirectories(Utils.CustomLibsDirectory);
        foreach (string moduleFolder in moduleFolders)
        {
            string moduleJsonPath = Path.Combine(moduleFolder, "module.json");
            if (!File.Exists(moduleJsonPath))
            {
                continue;
            }

            try
            {
                string moduleJson = File.ReadAllText(moduleJsonPath);
                InstallableTrackingModule? module = JsonConvert.DeserializeObject<InstallableTrackingModule>(moduleJson);
                if (module?.DllFileName is not { Length: > 0 } dllFileName)
                {
                    LogModuleJsonNamesNoDll(_logger, moduleFolder);
                    continue;
                }

                module.AssemblyLoadPath = Path.Combine(moduleFolder, dllFileName);
                installedModules.Add(module);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
            {
                LogFailedDeserializeModuleJson(_logger, e, moduleFolder);
            }
        }

        return installedModules;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipping {Folder}: its module.json names no DLL")]
    private static partial void LogModuleJsonNamesNoDll(ILogger logger, string folder);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Exception trying to get modules from module registry: {E}")]
    private static partial void LogExceptionTryingGetModulesModule(ILogger logger, string e);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to increment downloads for {ModuleId}. Status code: {StatusCode}")]
    private static partial void LogFailedIncrementDownloadsStatusCode(ILogger logger, Guid moduleId, HttpStatusCode statusCode);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Rating for {ModuleId} was cached as {Rating}")]
    private static partial void LogRatingCached(ILogger logger, Guid moduleId, int rating);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Failed to get user rating for module {ModuleId}")]
    private static partial void LogFailedGetUserRatingModule(ILogger logger, Guid moduleId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Rating for {ModuleId} was {Rating}. Caching...")]
    private static partial void LogRatingCaching(ILogger logger, Guid moduleId, int rating);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to get user rating for module {ModuleId}. Exception: {ErrorMessage}")]
    private static partial void LogGetUserRatingFailed(ILogger logger, Guid moduleId, string errorMessage);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to set rating for {ModuleId} to {Rating}. Status code: {StatusCode}")]
    private static partial void LogFailedSetRatingStatusCode(ILogger logger, Guid moduleId, int rating, HttpStatusCode statusCode);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Rating for {ModuleId} was set to {Rating}. Caching...")]
    private static partial void LogRatingSetCaching(ILogger logger, Guid moduleId, int rating);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to set rating for module {ModuleId}. Exception: {ErrorMessage}")]
    private static partial void LogSetRatingFailed(ILogger logger, Guid moduleId, string errorMessage);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to deserialize module.json for {ModuleFolder}")]
    private static partial void LogFailedDeserializeModuleJson(ILogger logger, Exception exception, string moduleFolder);
}
