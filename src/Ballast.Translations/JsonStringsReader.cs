// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text.Json;

namespace Ballast.Translations;

/// <summary>
/// Reads a JSON file whose root is an object into a <see cref="TranslationMap"/>. A member whose value is a string is a text.
/// A member whose value is an object is flattened: its members are read under the parent's key joined to the member's name with
/// the separator. With the default separator, <c>{"save":{"label":"Save"}}</c> gives the key <c>save.label</c>. A value that is
/// neither a string nor an object (a number, a boolean, null or an array) is reported and not read.
/// </summary>
public static class JsonStringsReader
{
    /// <summary>
    /// Reads the JSON file at a path. The map is named with the path as it was given.
    /// </summary>
    /// <param name="path">The file to read. Required.</param>
    /// <param name="separator">The text that joins a parent key and a member name in a flattened key. Default <c>.</c>.</param>
    /// <returns>The map of the file's strings, with nested objects flattened.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> or <paramref name="separator"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="path"/> or <paramref name="separator"/> is empty or only white space.</exception>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="DirectoryNotFoundException">The folder of the file does not exist.</exception>
    /// <exception cref="UnauthorizedAccessException">The file cannot be opened for reading.</exception>
    /// <exception cref="InvalidDataException">
    /// The file is not valid JSON, or its root is not an object. The message names the file.
    /// </exception>
    public static TranslationMap ReadFile(string path, string separator = ".")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(separator);
        using var stream = File.OpenRead(path);
        return Read(stream, path, separator);
    }

    /// <summary>
    /// Reads a JSON document from a stream. The stream is read to its end and is left open. A UTF-8 byte order mark is accepted.
    /// </summary>
    /// <param name="stream">The document. Required, and readable.</param>
    /// <param name="name">The name of the file, which every problem about the map names. Required.</param>
    /// <param name="separator">The text that joins a parent key and a member name in a flattened key. Default <c>.</c>.</param>
    /// <returns>The map of the document's strings, with nested objects flattened.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="separator"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> or <paramref name="separator"/> is empty or only white space.</exception>
    /// <exception cref="NotSupportedException"><paramref name="stream"/> cannot be read.</exception>
    /// <exception cref="InvalidDataException">
    /// The document is not valid JSON (comments and trailing commas are not valid), or its root is not an object. The message names
    /// <paramref name="name"/>, and the JSON error is the inner exception.
    /// </exception>
    /// <remarks>
    /// <para>
    /// A member with an empty or white-space name is not read and is reported as an unnamed entry. An object with no members adds
    /// nothing and is not reported, because it holds no text.
    /// </para>
    /// <para>
    /// A flattened key can collide with a member whose name holds the separator: <c>{"a":{"b":"1"},"a.b":"2"}</c> gives the key
    /// <c>a.b</c> twice. The second is reported as a duplicate, so the collision is never silent.
    /// </para>
    /// </remarks>
    public static TranslationMap Read(Stream stream, string name, string separator = ".")
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(separator);

        using var document = Parse(stream, name);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException($"{name} is not a JSON object: its root must be an object whose members are strings.");
        }

        List<TranslationEntry> entries = [];
        List<TranslationProblem> problems = [];
        Flatten(root, null, name, separator, entries, problems);
        return new TranslationMap(name, entries, problems);
    }

    private static JsonDocument Parse(Stream stream, string name)
    {
        try
        {
            return JsonDocument.Parse(stream);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"{name} is not valid JSON: {exception.Message}", exception);
        }
    }

    private static void Flatten(
        JsonElement obj,
        string? prefix,
        string name,
        string separator,
        List<TranslationEntry> entries,
        List<TranslationProblem> problems)
    {
        foreach (var member in obj.EnumerateObject())
        {
            if (string.IsNullOrWhiteSpace(member.Name))
            {
                var where = prefix is null ? "A top-level member" : $"A member under \"{prefix}\"";
                problems.Add(new TranslationProblem(name, prefix, null, TranslationProblemKind.UnnamedEntry,
                    $"{where} has no name, so it is not read."));
                continue;
            }

            var key = prefix is null ? member.Name : prefix + separator + member.Name;
            switch (member.Value.ValueKind)
            {
                case JsonValueKind.String:
                    entries.Add(new TranslationEntry(key, member.Value.GetString() ?? string.Empty));
                    break;
                case JsonValueKind.Object:
                    Flatten(member.Value, key, name, separator, entries, problems);
                    break;
                default:
                    problems.Add(new TranslationProblem(name, key, null, TranslationProblemKind.UnsupportedValue,
                        $"\"{key}\" is {DescribeKind(member.Value.ValueKind)}, not a string, so it is not read."));
                    break;
            }
        }
    }

    private static string DescribeKind(JsonValueKind kind) => kind switch
    {
        JsonValueKind.Number => "a number",
        JsonValueKind.True or JsonValueKind.False => "a boolean",
        JsonValueKind.Null => "null",
        JsonValueKind.Array => "an array",
        _ => "not a string",
    };
}
