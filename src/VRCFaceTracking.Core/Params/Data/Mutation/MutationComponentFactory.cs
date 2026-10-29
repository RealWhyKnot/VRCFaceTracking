using System.Collections.ObjectModel;
using System.Globalization;
using System.Reflection;

namespace VRCFaceTracking.Core.Params.Data.Mutation;

public static class MutationComponentFactory
{
    public static ObservableCollection<IMutationComponent> CreateComponents(TrackingMutation mutation)
    {
        ObservableCollection<IMutationComponent> components = [];
        Type type = mutation.GetType();

        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
        {
            MutationPropertyAttribute? attribute = property.GetCustomAttribute<MutationPropertyAttribute>();
            if (attribute is null)
            {
                continue;
            }

            IMutationComponent component = CreateComponent(mutation, property, attribute);
            component.Group = attribute.Group;
            component.StartsGroup = attribute.Group is not null
                && (components.Count == 0 || !string.Equals(components[^1].Group, attribute.Group, StringComparison.Ordinal));
            components.Add(component);
        }

        foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
        {
            MutationButtonAttribute? attribute = method.GetCustomAttribute<MutationButtonAttribute>();
            if (attribute is not null)
            {
                components.Add(new MutationAction(attribute.Name, () => method.Invoke(mutation, parameters: null))
                {
                    IsDeveloper = attribute.Developer,
                });
            }
        }

        return components;
    }

    private static IMutationComponent CreateComponent(TrackingMutation mutation, PropertyInfo property, MutationPropertyAttribute attribute)
    {
        object value = property.GetValue(mutation)!;
        return value is ValueTuple<float, float> range
            ? CreateRange(mutation, property, attribute, range)
            : CreateProperty(mutation, property, attribute, value);
    }

    private static MutationRangeProperty CreateRange(TrackingMutation mutation, PropertyInfo property, MutationPropertyAttribute attribute, (float, float) range) =>
        new(
            attribute.Name,
            range.Item1,
            range.Item2,
            newValue =>
            {
                property.SetValue(mutation, newValue);
                SaveIfImmediate(mutation, attribute);
            },
            attribute.Min,
            attribute.Max);

    private static MutationProperty CreateProperty(TrackingMutation mutation, PropertyInfo property, MutationPropertyAttribute attribute, object value) =>
        new(
            attribute.Name,
            value,
            DeterminePropertyType(value),
            newValue =>
            {
                property.SetValue(mutation, Convert.ChangeType(newValue, property.PropertyType, CultureInfo.InvariantCulture));
                SaveIfImmediate(mutation, attribute);
            },
            attribute.Min,
            attribute.Max)
        {
            IsDeveloper = attribute.Developer,
            Indented = attribute.Indented,
        };

    private static void SaveIfImmediate(TrackingMutation mutation, MutationPropertyAttribute attribute)
    {
        if (attribute.SavedImmediate)
        {
            _ = mutation.SaveAsync(CancellationToken.None);
        }
    }

    private static MutationPropertyType DeterminePropertyType(object value) => value switch
    {
        bool => MutationPropertyType.CheckBox,
        float => MutationPropertyType.Slider,
        _ => MutationPropertyType.TextBox,
    };
}
