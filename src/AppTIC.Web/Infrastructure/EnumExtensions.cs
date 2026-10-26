using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace AppTIC.Web.Infrastructure;

public static class EnumExtensions
{
    public static string DisplayName(this Enum value) =>
        value.GetType().GetField(value.ToString())?.GetCustomAttribute<DisplayAttribute>()?.Name ?? value.ToString();

    /// <summary>Lista {Value, Text} para DropDownList Syncfusion.</summary>
    public static List<LookupItem> ToLookup<TEnum>() where TEnum : struct, Enum =>
        Enum.GetValues<TEnum>().Select(e => new LookupItem(e.ToString(), e.DisplayName())).ToList();
}

public record LookupItem(string Value, string Text);
