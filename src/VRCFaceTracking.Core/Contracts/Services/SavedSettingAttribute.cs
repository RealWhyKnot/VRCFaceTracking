namespace VRCFaceTracking.Core.Contracts.Services;

[AttributeUsage(AttributeTargets.Property)]
public sealed class SavedSettingAttribute : Attribute
{
    public SavedSettingAttribute(string settingName, object? defaultValue = default, bool forceLocal = false)
    {
        SettingName = settingName;
        DefaultValue = defaultValue;
        ForceLocal = forceLocal;
    }

    public string SettingName { get; }
    public object? DefaultValue { get; }
    public bool ForceLocal { get; }
}
