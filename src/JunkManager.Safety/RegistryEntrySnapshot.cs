using System.Text.Json;
using Microsoft.Win32;

namespace JunkManager.Safety;

public sealed record RegistrySavedValue(
    string RelativeKey, string Name, RegistryValueKind Kind, ReadOnlyMemory<byte> Data)
{
    public object Decode() => Kind switch
    {
        RegistryValueKind.String or RegistryValueKind.ExpandString => JsonSerializer.Deserialize<string>(Data.Span)!,
        RegistryValueKind.MultiString => JsonSerializer.Deserialize<string[]>(Data.Span)!,
        RegistryValueKind.DWord => JsonSerializer.Deserialize<int>(Data.Span),
        RegistryValueKind.QWord => JsonSerializer.Deserialize<long>(Data.Span),
        RegistryValueKind.Binary or RegistryValueKind.None => JsonSerializer.Deserialize<byte[]>(Data.Span)!,
        _ => throw new InvalidDataException("неподдерживаемый тип значения реестра"),
    };
}

/// <summary>Typed, unexpanded data captured when the finding is made.</summary>
public sealed record RegistryEntrySnapshot(
    RegistryHive Hive, string SubKey, string ValueName, RegistryView View, bool WholeKey,
    IReadOnlyList<string> Keys, IReadOnlyList<RegistrySavedValue> Values)
{
    public static RegistryEntrySnapshot? Capture(
        RegistryHive hive, string subKey, string valueName, RegistryView view, bool wholeKey)
    {
        using var root = RegistryKey.OpenBaseKey(hive, view);
        using var key = root.OpenSubKey(subKey, writable: false);
        if (key is null || (!wholeKey && !key.GetValueNames().Contains(valueName, StringComparer.OrdinalIgnoreCase)))
        {
            return null;
        }

        var keys = new List<string>();
        var values = new List<RegistrySavedValue>();
        if (wholeKey)
        {
            ReadTree(key, string.Empty, keys, values, 0);
        }
        else
        {
            values.Add(ReadValue(key, string.Empty, valueName));
        }

        return new RegistryEntrySnapshot(hive, subKey, valueName, view, wholeKey, keys, values);
    }

    public bool Matches(RegistryEntrySnapshot? other) => other is not null
        && Hive == other.Hive && View == other.View && WholeKey == other.WholeKey
        && string.Equals(SubKey, other.SubKey, StringComparison.OrdinalIgnoreCase)
        && string.Equals(ValueName, other.ValueName, StringComparison.OrdinalIgnoreCase)
        && Keys.Count == other.Keys.Count && Values.Count == other.Values.Count
        && Keys.All(k => other.Keys.Contains(k, StringComparer.OrdinalIgnoreCase))
        && Values.All(v => other.Values.Any(o => v.Kind == o.Kind
            && string.Equals(v.RelativeKey, o.RelativeKey, StringComparison.OrdinalIgnoreCase)
            && string.Equals(v.Name, o.Name, StringComparison.OrdinalIgnoreCase)
            && v.Data.Span.SequenceEqual(o.Data.Span)));

    public RegistryEntrySnapshot? ReadCurrent() => Capture(Hive, SubKey, ValueName, View, WholeKey);

    private static RegistrySavedValue ReadValue(RegistryKey key, string relative, string name)
    {
        var kind = key.GetValueKind(name);
        var value = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames)
            ?? throw new IOException("значение исчезло во время чтения снимка");
        // Persist the type too; JSON alone turns DWORDs into philosophical suggestions.
        return new RegistrySavedValue(relative, name, kind, JsonSerializer.SerializeToUtf8Bytes(value));
    }

    private static void ReadTree(
        RegistryKey key, string relative, List<string> keys, List<RegistrySavedValue> values, int depth)
    {
        if (depth > 32 || keys.Count + values.Count > 10_000)
        {
            throw new IOException("поддерево слишком велико для точного снимка");
        }

        keys.Add(relative);
        foreach (var name in key.GetValueNames())
        {
            values.Add(ReadValue(key, relative, name));
        }

        foreach (var name in key.GetSubKeyNames())
        {
            using var child = key.OpenSubKey(name, writable: false)
                ?? throw new IOException("подключ исчез во время чтения снимка");
            ReadTree(child, relative.Length == 0 ? name : relative + "\\" + name, keys, values, depth + 1);
        }
    }
}
