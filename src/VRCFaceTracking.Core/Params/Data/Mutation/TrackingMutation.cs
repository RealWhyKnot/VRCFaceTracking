using System.Collections.ObjectModel;
using System.Globalization;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using VRCFaceTracking.Core.Contracts.Services;

namespace VRCFaceTracking.Core.Params.Data.Mutation;

public abstract class TrackingMutation
{
    public abstract string Name { get; }

    [JsonIgnore]
    public abstract string Description { get; }

    public abstract MutationPriority Priority { get; }

    [JsonIgnore]
    public virtual int Order => 0;

    [JsonIgnore]
    public virtual int PageOrder => int.MaxValue;

    [JsonIgnore]
    public ObservableCollection<IMutationComponent> Components { get; private set; } = [];

    public virtual bool IsSaved => false;

    public virtual bool IsActive { get; set; }

    [JsonIgnore]
    public bool ActiveSwitch
    {
        get => IsActive;
        set
        {
            if (IsActive == value)
            {
                return;
            }

            IsActive = value;
            _ = SaveAsync(CancellationToken.None);
        }
    }

    [JsonIgnore]
    public ILogger Logger { get; set; } = NullLogger.Instance;

    [JsonIgnore]
    public ILocalSettingsService? LocalSettingsService { get; set; }

    public static TrackingMutation[] GetImplementingMutations(bool ordered = true)
    {
        IEnumerable<TrackingMutation> mutations = Assembly.GetExecutingAssembly()
            .GetTypes()
            .Where(type => type.IsSubclassOf(typeof(TrackingMutation)))
            .Select(type => (TrackingMutation)Activator.CreateInstance(type)!);

        if (ordered)
        {
            mutations = mutations.OrderBy(m => m.Priority).ThenBy(m => m.Order).ThenBy(m => m.Name, StringComparer.Ordinal);
        }

        return [.. mutations];
    }

    public virtual void Initialize(UnifiedTrackingData data)
    {
    }

    public abstract void MutateData(ref UnifiedTrackingData data);

    public void CreateProperties() => Components = MutationComponentFactory.CreateComponents(this);

    public Task SaveAsync(CancellationToken cancellationToken) =>
        LocalSettingsService?.SaveSettingAsync(Name, this, forceLocal: true, cancellationToken) ?? Task.CompletedTask;

    public string DescribeSettings()
    {
        IEnumerable<string> settings = GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Where(property => property.GetCustomAttribute<MutationPropertyAttribute>() is not null)
            .Select(property => $"{property.Name}={FormatSetting(property.GetValue(this))}");
        return $"active={IsActive} {string.Join(' ', settings)}";
    }

    private static string FormatSetting(object? value) => value switch
    {
        null => "null",
        ValueTuple<float, float> range => string.Create(CultureInfo.InvariantCulture, $"{range.Item1}..{range.Item2}"),
        IFormattable formattable => formattable.ToString(format: null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };
}
