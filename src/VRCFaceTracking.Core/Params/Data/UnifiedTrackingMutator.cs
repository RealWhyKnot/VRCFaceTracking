using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using VRCFaceTracking.Core.Contracts.Services;
using VRCFaceTracking.Core.Params.Data.Mutation;

namespace VRCFaceTracking.Core.Params.Data;

public sealed partial class UnifiedTrackingMutator : ObservableObject
{
    [ObservableProperty]
    [property: SavedSetting("MutatorEnabled", true)]
    private bool _enabled;

    private readonly ILogger<UnifiedTrackingMutator> _logger;
    private readonly ILocalSettingsService _localSettingsService;
    private readonly IDispatcherService _dispatcherService;
    private readonly Lock _mutationsLock = new();
    private UnifiedTrackingData _inputBuffer;
    private readonly Lock _previewLock = new();
    public ObservableCollection<TrackingMutation> Mutations { get; } = [];

    public UnifiedTrackingMutator(ILogger<UnifiedTrackingMutator> logger, ILocalSettingsService localSettingsService, IDispatcherService dispatcherService, IExternalTrackingSource? externalSource = null)
    {
        ExternalSource = externalSource;
        TrackingOutput.Mutator = this;
        _logger = logger;
        _localSettingsService = localSettingsService;
        _dispatcherService = dispatcherService;

        Enabled = false;
        _inputBuffer = new UnifiedTrackingData();
    }

    public UnifiedTrackingData MutateData(UnifiedTrackingData input)
    {
        using (UnifiedTracking.LockData())
        {
            _inputBuffer.CopyPropertiesOf(input);
        }

        ExternalSource?.Merge(_inputBuffer);

        float rawLeft = _inputBuffer.Eye.Left.Openness;
        float rawRight = _inputBuffer.Eye.Right.Openness;
        var rawPreview = TrackingPreviewValues.From(_inputBuffer);
        bool applied = Enabled;

        if (applied)
        {
            lock (_mutationsLock)
            {
                for (int i = 0; i < Mutations.Count; i++)
                {
                    if (Mutations[i].IsActive)
                    {
                        Mutations[i].MutateData(ref _inputBuffer);
                    }
                }
            }
        }

        LastEyeOpenness = (rawLeft, rawRight, _inputBuffer.Eye.Left.Openness, _inputBuffer.Eye.Right.Openness);
        var snapshot = new TrackingPreviewSnapshot(rawPreview, TrackingPreviewValues.From(_inputBuffer), applied, Stopwatch.GetTimestamp());
        PreviewSnapshot = snapshot;

        return _inputBuffer;
    }

    public TrackingPreviewSnapshot PreviewSnapshot
    {
        get
        {
            lock (_previewLock)
            {
                return field;
            }
        }
        private set
        {
            lock (_previewLock)
            {
                field = value;
            }
        }
    }

    internal Func<AvatarParameterSnapshot?> CurrentAvatar { get; set; } = () => AvatarParameters.Current;

    public IExternalTrackingSource? ExternalSource { get; }

    public TrackingActivity Activity
    {
        get
        {
            long now = Stopwatch.GetTimestamp();
            long last = UnifiedTracking.LastDataUpdateTicks;
            if (ExternalSource?.SinceLastData is { } since)
            {
                long external = Math.Max(1, now - (long)(since.TotalSeconds * Stopwatch.Frequency));
                last = Math.Max(last, external);
            }

            return TrackingActivity.Classify(last, now, Stopwatch.Frequency, CurrentAvatar()?.UsesNativeFaceTracking == true) with
            {
                ExternalEyes = ExternalSource?.DrivesEyes == true,
                ExternalFace = ExternalSource?.DrivesFace == true,
            };
        }
    }

    public UnifiedTrackingData LastOutput => _inputBuffer;

    public (float RawLeft, float RawRight, float OutLeft, float OutRight) LastEyeOpenness
    {
        get; private set;
    }

    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        LogSavingMutationConfiguration(_logger);
        await _localSettingsService.SaveAsync(this, cancellationToken).ConfigureAwait(false);

        List<TrackingMutation> mutationsToSave;
        lock (_mutationsLock)
        {
            mutationsToSave = [.. Mutations];
        }

        foreach (TrackingMutation mutation in mutationsToSave)
        {
            LogSavingData(_logger, mutation.Name);
            await _localSettingsService.SaveSettingAsync(mutation.Name, mutation, forceLocal: true, cancellationToken).ConfigureAwait(false);
        }

        LogMutationDataSaved(_logger);
    }

    public void Initialize()
    {
        LogInitializingMutations(_logger);
        lock (_mutationsLock)
        {
            foreach (TrackingMutation mutation in Mutations)
            {
                LogInitializing(_logger, mutation.Name);
                mutation.Initialize(UnifiedTracking.Data);
            }
        }

        LogMutationsInitializedSuccessfully(_logger);
    }

    private static readonly MethodInfo ReadSavedMutationMethod =
        typeof(UnifiedTrackingMutator).GetMethod(nameof(ReadSavedMutationAsync), BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new MissingMethodException(nameof(UnifiedTrackingMutator), nameof(ReadSavedMutationAsync));

    private async Task<TrackingMutation?> ReadSavedMutationAsync<T>(T mutation, CancellationToken cancellationToken)
        where T : TrackingMutation
    {
        T? saved = await _localSettingsService.ReadSettingAsync(mutation.Name, mutation, forceLocal: true, cancellationToken).ConfigureAwait(false);
        return saved;
    }

    private async Task CreateMutationAsync(TrackingMutation mutation, CancellationToken cancellationToken)
    {
        try
        {
            LogLoading(_logger, mutation.Name);

            if (ReadSavedMutationMethod.MakeGenericMethod(mutation.GetType()).Invoke(this, [mutation, cancellationToken]) is not Task<TrackingMutation?> read)
            {
                throw new InvalidOperationException($"{nameof(ReadSavedMutationAsync)} did not return a task");
            }

            if (await read.ConfigureAwait(false) is { } loaded)
            {
                mutation = loaded;
            }

            mutation.Logger = _logger;
            mutation.LocalSettingsService = _localSettingsService;
            if (mutation is EyeAssists eyeAssists)
            {
                await eyeAssists.MoveEyelidBlendFromCorrectorsAsync(_localSettingsService, cancellationToken).ConfigureAwait(false);
            }

            if (mutation is Correctors correctors)
            {
                await correctors.MoveCompensationFromAssistsAsync(_localSettingsService, cancellationToken).ConfigureAwait(false);
            }

            if (mutation is ParameterAdjustment adjustment)
            {
                await adjustment.MigrateHeadRangesAsync(cancellationToken).ConfigureAwait(false);
            }

            mutation.CreateProperties();
            if (_logger.IsEnabled(LogLevel.Information))
            {
                string settings = mutation.DescribeSettings();
                LogSettings(_logger, mutation.Name, settings);
            }
            _dispatcherService.Run(() =>
            {
                lock (_mutationsLock)
                {
                    Mutations.Add(mutation);
                }
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogCreatingNewMutationData(_logger, ex.Message);
            mutation.Logger = _logger;
            mutation.LocalSettingsService = _localSettingsService;
            mutation.CreateProperties();
            _dispatcherService.Run(() =>
            {
                lock (_mutationsLock)
                {
                    Mutations.Add(mutation);
                }
            });
        }
    }

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        LogLoadingMutationData(_logger);
        _dispatcherService.Run(() =>
        {
            lock (_mutationsLock)
            {
                Mutations.Clear();
            }
        });
        TrackingMutation[] mutations = TrackingMutation.GetImplementingMutations(ordered: true);

        if (mutations.Length == 0)
        {
            LogNoMutationsWereDiscoveredCalibration(_logger);
        }
        await _localSettingsService.LoadAsync(this, cancellationToken).ConfigureAwait(false);

        foreach (TrackingMutation mutation in mutations)
        {
            await CreateMutationAsync(mutation, cancellationToken).ConfigureAwait(false);
        }

        LogMutationDataLoaded(_logger);
        Initialize();
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Saving mutation configuration...")]
    private static partial void LogSavingMutationConfiguration(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Saving {MutationName} data.")]
    private static partial void LogSavingData(ILogger logger, string mutationName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Mutation data saved.")]
    private static partial void LogMutationDataSaved(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Initializing mutations...")]
    private static partial void LogInitializingMutations(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Initializing {MutationName}")]
    private static partial void LogInitializing(ILogger logger, string mutationName);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Mutations initialized successfully.")]
    private static partial void LogMutationsInitializedSuccessfully(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Loading {MutationName}")]
    private static partial void LogLoading(ILogger logger, string mutationName);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Name} settings {Settings}")]
    private static partial void LogSettings(ILogger logger, string name, string settings);

    [LoggerMessage(Level = LogLevel.Error, Message = "Creating new mutation data. {ErrorMessage}")]
    private static partial void LogCreatingNewMutationData(ILogger logger, string errorMessage);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Loading mutation data...")]
    private static partial void LogLoadingMutationData(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No mutations were discovered. Calibration and filtering are unavailable for this session.")]
    private static partial void LogNoMutationsWereDiscoveredCalibration(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Mutation data loaded.")]
    private static partial void LogMutationDataLoaded(ILogger logger);
}
