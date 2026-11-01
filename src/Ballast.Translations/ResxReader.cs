// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Xml;
using System.Xml.Linq;

namespace Ballast.Translations;

/// <summary>
/// Reads a .resx file, the XML format that .NET projects use for texts, into a <see cref="TranslationMap"/>. Only the
/// <c>data</c> elements are read, in file order. A <c>data</c> element's name is its key and its <c>value</c> child is its text.
/// The <c>resheader</c> and <c>metadata</c> elements are not texts and are skipped. A <c>data</c> element without a
/// <c>value</c> child has an empty text, which the parity checks report as blank.
/// </summary>
public static class ResxReader
{
    /// <summary>
    /// Reads the .resx file at a path. The map is named with the path as it was given.
    /// </summary>
    /// <param name="path">The file to read. Required.</param>
    /// <returns>The map of the file's data elements.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="path"/> is empty or only white space.</exception>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="DirectoryNotFoundException">The folder of the file does not exist.</exception>
    /// <exception cref="UnauthorizedAccessException">The file cannot be opened for reading.</exception>
    /// <exception cref="InvalidDataException">
    /// The file is not well-formed XML, its root element is not <c>root</c>, or it contains a DTD. The message names the file.
    /// </exception>
    public static TranslationMap ReadFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var stream = File.OpenRead(path);
        return Read(stream, path);
    }

    /// <summary>
    /// Reads a .resx document from a stream. The stream is read to its end and is left open.
    /// </summary>
    /// <param name="stream">The document. Required, and readable.</param>
    /// <param name="name">The name of the file, which every problem about the map names. Required.</param>
    /// <returns>The map of the document's data elements.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty or only white space.</exception>
    /// <exception cref="NotSupportedException"><paramref name="stream"/> cannot be read.</exception>
    /// <exception cref="InvalidDataException">
    /// The document is not well-formed XML, its root element is not <c>root</c>, or it contains a DTD. The message names
    /// <paramref name="name"/>, and the XML error is the inner exception.
    /// </exception>
    /// <remarks>
    /// A DTD is refused. A resx file has none, and a DTD could declare an entity that reads a local file.
    /// </remarks>
    public static TranslationMap Read(Stream stream, string name)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        XDocument document;
        try
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            using var xml = XmlReader.Create(stream, settings);
            document = XDocument.Load(xml, LoadOptions.SetLineInfo);
        }
        catch (XmlException exception)
        {
            throw new InvalidDataException($"{name} is not a well-formed resx file: {exception.Message}", exception);
        }

        var root = document.Root;
        if (root is null || root.Name.LocalName != "root")
        {
            throw new InvalidDataException($"{name} is not a resx file: its root element is not \"root\".");
        }

        List<TranslationEntry> entries = [];
        foreach (var data in root.Elements("data"))
        {
            var key = (string?)data.Attribute("name") ?? string.Empty;
            var value = (string?)data.Element("value") ?? string.Empty;
            entries.Add(new TranslationEntry(key, value, LineOf(data)));
        }

        return new TranslationMap(name, entries);
    }

    private static int? LineOf(XElement element)
    {
        if (element is IXmlLineInfo info && info.HasLineInfo())
        {
            return info.LineNumber;
        }

        return null;
    }
}
