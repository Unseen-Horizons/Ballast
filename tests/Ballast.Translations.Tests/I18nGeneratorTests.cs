// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text;
using System.Text.Json;

namespace Ballast.Translations.Tests;

/// <summary>
/// The generator's rules: the texts it trims, the keys and status codes it writes, the order of the cultures, what it refuses,
/// and what it notices. The byte-for-byte comparison with the replaced script is in <see cref="I18nByteIdentityTests"/>.
/// </summary>
public class I18nGeneratorTests
{
    [Fact]
    public void Each_text_loses_the_white_space_javascript_trims_and_keeps_the_rest()
    {
        using var folder = TempFolder.Create();
        var source = WriteSource(folder, ("SharedResources.resx", Resx(("greeting", Ch(0xFEFF) + Ch(0xA0) + " Hi " + Ch(0x85) + Ch(0x3000)))));

        Generate(folder, source);

        var en = ReadObject(Path.Combine(folder.Path, "wwwroot", "i18n", "en.json"));
        // The space inside the text stays; the next-line character is not white space to JavaScript, so it stays too.
        Assert.Equal("Hi " + Ch(0x85), en["greeting"]);
    }

    [Fact]
    public void Strings_in_the_script_are_escaped_as_javascript_escapes_them()
    {
        using var folder = TempFolder.Create();
        var errorCodes = WriteFile(folder, "codes.json", "{\"k\": \"a\\\"b\\\\c\\n\\t\\u0001\\u007fé\"}");

        Generate(folder, EmptySource(folder), errorCodes: errorCodes);

        var script = File.ReadAllText(Path.Combine(folder.Path, "wwwroot", "js", "i18n-data.js"));
        var expected = "  \"k\": \"a\\\"b\\\\c\\n\\t\\u0001" + Ch(0x7F) + Ch(0xE9) + "\"";
        Assert.Contains("window.ERROR_CODE_MAP = {\n" + expected + "\n};\n", script);
    }

    [Fact]
    public void An_error_code_text_with_an_unpaired_surrogate_escape_is_refused()
    {
        using var folder = TempFolder.Create();
        var errorCodes = WriteFile(folder, "codes.json", "{\"k\": \"x\\ud800\"}");

        var ex = Assert.Throws<InvalidDataException>(() => Generate(folder, EmptySource(folder), errorCodes: errorCodes));

        Assert.Contains("\"k\"", ex.Message);
        Assert.Contains("unpaired surrogate", ex.Message);
    }

    [Fact]
    public void Array_index_keys_come_first_in_numeric_order_as_javascript_lists_them()
    {
        using var folder = TempFolder.Create();
        var errorCodes = WriteFile(folder, "codes.json", "{\"b\": \"B\", \"10\": \"ten\", \"2\": \"two\", \"a\": \"A\", \"01\": \"zero-one\"}");

        Generate(folder, EmptySource(folder), errorCodes: errorCodes);

        var script = File.ReadAllText(Path.Combine(folder.Path, "wwwroot", "js", "i18n-data.js"));
        var expected = "window.ERROR_CODE_MAP = {\n  \"2\": \"two\",\n  \"10\": \"ten\",\n  \"b\": \"B\",\n  \"a\": \"A\",\n  \"01\": \"zero-one\"\n};\n";
        Assert.Contains(expected, script);
    }

    [Fact]
    public void Status_keys_become_codes_in_pascal_case()
    {
        using var folder = TempFolder.Create();
        var source = WriteSource(folder, ("SharedResources.resx", Resx(("status_not_found", "Not found"), ("status_internal_error", "Failed"))));

        Generate(folder, source);

        var script = File.ReadAllText(Path.Combine(folder.Path, "wwwroot", "js", "i18n-data.js"));
        Assert.Contains("window.__STATUS_MAP = {\n  \"NotFound\": \"status_not_found\",\n  \"InternalError\": \"status_internal_error\"\n};\n", script);
    }

    [Fact]
    public void Two_status_keys_that_give_one_code_are_refused_and_nothing_is_written()
    {
        using var folder = TempFolder.Create();
        var source = WriteSource(folder, ("SharedResources.resx", Resx(("status_not_found", "Not found"), ("status_NOT_FOUND", "Gone"))));

        var ex = Assert.Throws<InvalidDataException>(() => Generate(folder, source));

        Assert.Contains("NotFound", ex.Message);
        Assert.False(Directory.Exists(Path.Combine(folder.Path, "wwwroot")));
    }

    [Fact]
    public void A_key_in_a_file_twice_is_refused_and_nothing_is_written()
    {
        using var folder = TempFolder.Create();
        var source = WriteSource(folder, ("SharedResources.resx", Resx(("save", "Save"), ("save", "Save again"))));

        var ex = Assert.Throws<InvalidDataException>(() => Generate(folder, source));

        Assert.Contains("\"save\" appears more than once", ex.Message);
        Assert.False(Directory.Exists(Path.Combine(folder.Path, "wwwroot")));
    }

    [Fact]
    public void An_entry_with_no_name_is_left_out_and_reported_as_a_warning()
    {
        using var folder = TempFolder.Create();
        var source = WriteSource(folder, ("SharedResources.resx",
            Resx(("save", "Save")).Replace("</root>", "<data xml:space=\"preserve\"><value>No name</value></data></root>")));

        var result = Generate(folder, source);

        var warning = Assert.Single(result.Warnings);
        Assert.Equal(TranslationProblemKind.UnnamedEntry, warning.Kind);
        var en = ReadObject(Path.Combine(folder.Path, "wwwroot", "i18n", "en.json"));
        Assert.Equal("save", string.Join(",", en.Keys));
    }

    [Fact]
    public void English_comes_first_and_the_other_cultures_follow_in_ordinal_order()
    {
        using var folder = TempFolder.Create();
        var source = WriteSource(folder,
            ("SharedResources.hr.resx", Resx(("save", "Spremi"))),
            ("SharedResources.resx", Resx(("save", "Save"))),
            ("SharedResources.de.resx", Resx(("save", "Speichern"))));

        var result = Generate(folder, source);

        Assert.Equal("en,de,hr", string.Join(",", result.Cultures));
        Assert.Contains("window.__I18N_LANGS = [\"en\",\"de\",\"hr\"];\n", File.ReadAllText(Path.Combine(folder.Path, "wwwroot", "js", "i18n-data.js")));
    }

    [Fact]
    public void Two_files_for_one_culture_are_refused()
    {
        using var folder = TempFolder.Create();
        var source = WriteSource(folder,
            ("SharedResources.resx", Resx(("save", "Save"))),
            ("SharedResources.en.resx", Resx(("save", "Save too"))));

        var ex = Assert.Throws<InvalidDataException>(() => Generate(folder, source));

        Assert.Contains("\"en\"", ex.Message);
    }

    [Fact]
    public void An_error_code_whose_value_is_not_text_is_refused()
    {
        using var folder = TempFolder.Create();
        var errorCodes = WriteFile(folder, "codes.json", "{\"ACCESS_DENIED\": 401}");

        var ex = Assert.Throws<InvalidDataException>(() => Generate(folder, EmptySource(folder), errorCodes: errorCodes));

        Assert.Contains("ACCESS_DENIED", ex.Message);
        Assert.Contains("not text", ex.Message);
    }

    [Fact]
    public void A_named_error_code_file_that_does_not_exist_is_an_error()
    {
        using var folder = TempFolder.Create();

        Assert.Throws<FileNotFoundException>(() =>
            Generate(folder, EmptySource(folder), errorCodes: Path.Combine(folder.Path, "missing.json")));
    }

    [Fact]
    public void Without_an_error_code_file_the_map_is_empty()
    {
        using var folder = TempFolder.Create();

        Generate(folder, EmptySource(folder));

        Assert.Contains("window.ERROR_CODE_MAP = {};\n", File.ReadAllText(Path.Combine(folder.Path, "wwwroot", "js", "i18n-data.js")));
    }

    [Fact]
    public void The_header_is_the_first_line_as_it_was_given()
    {
        using var folder = TempFolder.Create();

        Generate(folder, EmptySource(folder), header: "// Written by a test");

        var firstLine = File.ReadAllLines(Path.Combine(folder.Path, "wwwroot", "js", "i18n-data.js"))[0];
        Assert.Equal("// Written by a test", firstLine);
    }

    [Fact]
    public void Files_are_written_as_utf8_without_a_byte_order_mark_and_with_line_feeds()
    {
        using var folder = TempFolder.Create();
        var source = WriteSource(folder, ("SharedResources.resx", Resx(("save", "Spremi " + Ch(0x10D)))));

        Generate(folder, source);

        foreach (var file in new[] { Path.Combine(folder.Path, "wwwroot", "i18n", "en.json"), Path.Combine(folder.Path, "wwwroot", "js", "i18n-data.js") })
        {
            var bytes = File.ReadAllBytes(file);
            Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "a byte order mark");
            Assert.DoesNotContain((byte)'\r', bytes);
        }

        // The JSON file has no line break at its end, as the script wrote it.
        var json = File.ReadAllText(Path.Combine(folder.Path, "wwwroot", "i18n", "en.json"));
        Assert.EndsWith("}", json);
    }

    [Fact]
    public void A_loader_without_the_data_script_before_it_is_named_and_refused_after_the_files_are_written()
    {
        using var folder = TempFolder.Create();
        var source = WriteSource(folder, ("SharedResources.resx", Resx(("save", "Save"))));
        var web = Path.Combine(folder.Path, "site");
        Directory.CreateDirectory(Path.Combine(web, "sub"));
        Directory.CreateDirectory(Path.Combine(web, "i18n"));
        File.WriteAllText(Path.Combine(web, "good.html"), "<script src=\"/js/i18n-data.js\"></script><script src=\"/js/i18n.js\"></script>");
        File.WriteAllText(Path.Combine(web, "bad.html"), "<script src=\"/js/i18n.js\"></script>");
        File.WriteAllText(Path.Combine(web, "sub", "nested.html"), "<script src=\"/js/i18n.js\"></script>");
        File.WriteAllText(Path.Combine(web, "i18n", "skipped.html"), "<script src=\"/js/i18n.js\"></script>");

        var ex = Assert.Throws<InvalidOperationException>(() => Generate(folder, source, webRoot: web, loaderScript: "/js/i18n.js"));

        Assert.Contains("bad.html", ex.Message);
        Assert.Contains(Path.Combine("sub", "nested.html"), ex.Message);
        Assert.DoesNotContain("good.html", ex.Message);
        Assert.DoesNotContain("skipped.html", ex.Message);
        Assert.True(File.Exists(Path.Combine(folder.Path, "wwwroot", "js", "i18n-data.js")));
    }

    [Fact]
    public void A_malformed_resx_file_is_refused_with_its_name_and_nothing_is_written()
    {
        using var folder = TempFolder.Create();
        var source = WriteSource(folder, ("SharedResources.resx", "<root><data name=\"x\">"));

        var ex = Assert.Throws<InvalidDataException>(() => Generate(folder, source));

        Assert.Contains("SharedResources.resx", ex.Message);
        Assert.False(Directory.Exists(Path.Combine(folder.Path, "wwwroot")));
    }

    [Fact]
    public void A_blank_option_is_refused_by_name()
    {
        var ex = Assert.Throws<ArgumentException>(() => I18nGenerator.Generate(new I18nGeneratorOptions
        {
            SourceDirectory = " ",
            OutputDirectory = "out",
            DataFile = "data.js",
        }));

        Assert.Equal("SourceDirectory", ex.ParamName);
    }

    [Fact]
    public void A_null_options_object_is_refused()
    {
        Assert.Throws<ArgumentNullException>(() => I18nGenerator.Generate(null!));
    }

    [Fact]
    public void A_source_folder_that_does_not_exist_is_refused()
    {
        using var folder = TempFolder.Create();

        Assert.Throws<DirectoryNotFoundException>(() => Generate(folder, Path.Combine(folder.Path, "missing")));
    }

    // Runs the generator with its output under the temporary folder.
    private static I18nGenerationResult Generate(
        TempFolder folder,
        string sourceDirectory,
        string? errorCodes = null,
        string? webRoot = null,
        string? loaderScript = null,
        string? header = null)
    {
        var options = new I18nGeneratorOptions
        {
            SourceDirectory = sourceDirectory,
            OutputDirectory = Path.Combine(folder.Path, "wwwroot", "i18n"),
            DataFile = Path.Combine(folder.Path, "wwwroot", "js", "i18n-data.js"),
            ErrorCodesFile = errorCodes,
            WebRoot = webRoot,
            LoaderScript = loaderScript,
        };
        if (header is not null)
        {
            options.Header = header;
        }

        return I18nGenerator.Generate(options);
    }

    // Writes .resx files into a source folder of their own, and returns the folder's path.
    private static string WriteSource(TempFolder folder, params (string Name, string Content)[] files)
    {
        var source = Path.Combine(folder.Path, "source");
        Directory.CreateDirectory(source);
        foreach (var (name, content) in files)
        {
            File.WriteAllText(Path.Combine(source, name), content);
        }

        return source;
    }

    // A source folder with no .resx files in it.
    private static string EmptySource(TempFolder folder)
    {
        var source = Path.Combine(folder.Path, "empty");
        Directory.CreateDirectory(source);
        return source;
    }

    private static string WriteFile(TempFolder folder, string name, string content)
    {
        var path = Path.Combine(folder.Path, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static string Resx(params (string Name, string Value)[] data)
    {
        var body = string.Concat(data.Select(item => $"<data name=\"{item.Name}\" xml:space=\"preserve\"><value>{item.Value}</value></data>"));
        return "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<root>" + body + "</root>";
    }

    private static string Ch(int code) => ((char)code).ToString();

    private static Dictionary<string, string> ReadObject(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
        return document.RootElement.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.GetString()!, StringComparer.Ordinal);
    }
}
