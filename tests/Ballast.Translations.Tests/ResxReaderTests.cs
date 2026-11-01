// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.Translations.Tests;

public class ResxReaderTests
{
    private static readonly string[] SaveAndCancel = ["save", "cancel"];
    private static readonly string[] SaveAndCancelTexts = ["Save", "Cancel"];

    [Fact]
    public void Reads_the_data_elements_in_file_order_and_skips_the_headers()
    {
        var map = TestData.Resx("""
            <resheader name="version"><value>2.0</value></resheader>
            <metadata name="note"><value>not a text</value></metadata>
            <data name="save"><value>Save</value></data>
            <data name="cancel"><value>Cancel</value></data>
            """);

        Assert.Equal(SaveAndCancel, map.Entries.Select(entry => entry.Key));
        Assert.Equal(SaveAndCancelTexts, map.Entries.Select(entry => entry.Value));
        Assert.Empty(map.Problems);
    }

    [Fact]
    public void A_data_element_without_a_value_reads_as_an_empty_text()
    {
        var map = TestData.Resx("<data name=\"empty\" />");

        Assert.Equal(string.Empty, map.Entries.Single().Value);
        Assert.Empty(map.Problems);
    }

    [Fact]
    public void Each_entry_carries_the_line_its_data_element_starts_on()
    {
        // The root starts on line 2, so the first data element is on line 3 and the second, after two empty lines, on line 5.
        var map = TestData.Resx("\n<data name=\"a\"><value>1</value></data>\n\n<data name=\"b\"><value>2</value></data>");

        Assert.Equal(3, map.Entries[0].Line);
        Assert.Equal(5, map.Entries[1].Line);
    }

    [Fact]
    public void A_duplicate_key_in_the_resx_is_reported_with_its_line_and_the_first_value_is_kept()
    {
        var map = TestData.Resx("<data name=\"save\"><value>Save</value></data>\n<data name=\"save\"><value>Save again</value></data>", "AppResources.resx");

        Assert.Equal(1, map.Count);
        Assert.Equal("Save", map.Entries.Single().Value);

        var problem = Assert.Single(map.Problems);
        Assert.Equal(TranslationProblemKind.DuplicateKey, problem.Kind);
        Assert.Equal("save", problem.Key);
        Assert.Equal(3, problem.Line);
        Assert.Equal("AppResources.resx", problem.File);
        Assert.Equal("\"save\" appears more than once (first at line 2); the first value is used.", problem.Message);
    }

    [Fact]
    public void A_data_element_without_a_name_is_reported_and_not_read()
    {
        var map = TestData.Resx("<data><value>orphan</value></data>");

        Assert.Equal(0, map.Count);
        var problem = Assert.Single(map.Problems);
        Assert.Equal(TranslationProblemKind.UnnamedEntry, problem.Kind);
        Assert.Null(problem.Key);
    }

    [Fact]
    public void Malformed_xml_is_refused_with_a_message_that_names_the_file()
    {
        var error = Assert.Throws<InvalidDataException>(() => ResxReader.Read(TestData.Utf8("<root><data"), "Broken.resx"));

        Assert.StartsWith("Broken.resx is not a well-formed resx file:", error.Message);
        Assert.NotNull(error.InnerException);
    }

    [Fact]
    public void A_document_whose_root_is_not_root_is_refused()
    {
        var error = Assert.Throws<InvalidDataException>(() =>
            ResxReader.Read(TestData.Utf8("<config><data name=\"a\"><value>b</value></data></config>"), "config.resx"));

        Assert.Equal("config.resx is not a resx file: its root element is not \"root\".", error.Message);
    }

    [Fact]
    public void A_document_type_declaration_is_refused()
    {
        const string xml = "<?xml version=\"1.0\"?><!DOCTYPE root [<!ENTITY secret \"x\">]><root><data name=\"a\"><value>&secret;</value></data></root>";

        var error = Assert.Throws<InvalidDataException>(() => ResxReader.Read(TestData.Utf8(xml), "dtd.resx"));

        Assert.StartsWith("dtd.resx is not a well-formed resx file:", error.Message);
    }

    [Fact]
    public void A_missing_file_throws_file_not_found()
    {
        Assert.Throws<FileNotFoundException>(() => ResxReader.ReadFile(Path.Combine(AppContext.BaseDirectory, "no-such-file.resx")));
    }

    [Fact]
    public void A_file_is_read_and_named_with_the_path_it_was_given()
    {
        var path = TestData.Fixture("Strings.resx");

        var map = ResxReader.ReadFile(path);

        Assert.Equal(path, map.Name);
        Assert.Equal(7, map.Count);
        Assert.Equal(9, map.Entries[0].Line);
        Assert.Empty(map.Problems);
    }

    [Fact]
    public void The_reader_refuses_a_null_stream_and_a_blank_name()
    {
        Assert.Throws<ArgumentNullException>(() => ResxReader.Read(null!, "x.resx"));
        Assert.Throws<ArgumentException>(() => ResxReader.Read(TestData.Utf8("<root/>"), " "));
        Assert.Throws<ArgumentException>(() => ResxReader.ReadFile(" "));
    }
}
