// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text;

namespace Ballast.Translations.Tests;

/// <summary>
/// Builds the maps and streams that the tests read.
/// </summary>
internal static class TestData
{
    /// <summary>The path of a fixture file, which is copied beside the test assembly.</summary>
    public static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    /// <summary>A stream of the text in UTF-8.</summary>
    public static MemoryStream Utf8(string text) => new(Encoding.UTF8.GetBytes(text));

    /// <summary>A map of key and text pairs, in the order given. The entries have no lines.</summary>
    public static TranslationMap Map(string name, params (string Key, string Value)[] entries) =>
        new(name, entries.Select(entry => new TranslationEntry(entry.Key, entry.Value)));

    /// <summary>A map read from a .resx document that holds the given data elements.</summary>
    public static TranslationMap Resx(string dataElements, string name = "Strings.resx") =>
        ResxReader.Read(Utf8($"<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<root>{dataElements}</root>"), name);

    /// <summary>A map read from a flat or nested JSON document.</summary>
    public static TranslationMap Json(string text, string name = "messages.json", string separator = ".") =>
        JsonStringsReader.Read(Utf8(text), name, separator);
}
