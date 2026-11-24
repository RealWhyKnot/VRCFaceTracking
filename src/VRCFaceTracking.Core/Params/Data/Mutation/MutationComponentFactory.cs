using System.Collections.ObjectModel;
using System.Globalization;
using System.Reflection;

namespace VRCFaceTracking.Core.Params.Data.Mutation;

public static class MutationComponentFactory
{
    public static ObservableCollection<IMutationComponent> CreateComponents(TrackingMutation mutation)
    {
        ObservableCollection<IMutationComponent> components = [];
        List<(IMutationComponent Component, PropertyInfo Property)> bound = [];
        Type type = mutation.GetType();
        TrackingMutation? defaults = CreateDefaults(type);

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
            component.Metadata = new MutationMetadata(
                mutation,
                property.Name,
                ResolveCard(type, attribute.Card),
                attribute.Order,
                components.Count,
                attribute.Advanced,
                attribute.Developer,
                attribute.DependsOn,
                attribute.Step,
                Confirm: false,
                defaults is null ? null : property.GetValue(defaults));
            components.Add(component);
            bound.Add((component, property));
        }

        foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
        {
            MutationButtonAttribute? attribute = method.GetCustomAttribute<MutationButtonAttribute>();
            if (attribute is not null)
            {
                components.Add(CreateAction(mutation, method, attribute, components.Count, bound));
            }
        }

        return components;
    }

    private static MutationAction CreateAction(
        TrackingMutation mutation,
        MethodInfo method,
        MutationButtonAttribute attribute,
        int index,
        List<(IMutationComponent Component, PropertyInfo Property)> bound) =>
        new(attribute.Name, () =>
        {
            method.Invoke(mutation, parameters: null);
            Refresh(mutation, bound);
        })
        {
            IsDeveloper = attribute.Developer,
            Group = attribute.Group,
            Metadata = new MutationMetadata(
                mutation,
                method.Name,
                ResolveCard(mutation.GetType(), attribute.Card),
                attribute.Order,
                index,
                attribute.Advanced,
                attribute.Developer,
                DependsOn: null,
                Step: 0f,
                attribute.Confirm,
                DefaultValue: null),
        };

    public static TrackingCard ResolveCard(Type mutationType, TrackingCard declared) =>
        declared != TrackingCard.None
            ? declared
            : mutationType.GetCustomAttribute<MutationCardAttribute>()?.Card ?? TrackingCard.Other;

    private static TrackingMutation? CreateDefaults(Type type) =>
        type.GetConstructor(Type.EmptyTypes) is null ? null : Activator.CreateInstance(type) as TrackingMutation;

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

    private static void Refresh(TrackingMutation mutation, List<(IMutationComponent Component, PropertyInfo Property)> bound)
    {
        foreach ((IMutationComponent component, PropertyInfo property) in bound)
        {
            object value = property.GetValue(mutation)!;
            if (component is MutationRangeProperty range)
            {
                range.Refresh(((float, float))value);
            }
            else if (component is MutationProperty setting)
            {
                setting.Refresh(value);
            }
        }
    }

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
