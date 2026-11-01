// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Storage;

namespace Ballast.Storage.Tests;

/// <summary>
/// The data folder: outside development it must be named, must exist and must carry its marker, and a refusal happens before
/// anything is written. The setting name and the marker name are given as parameters, so each test names its own.
/// None of these tests creates a folder that the code is meant to refuse to create.
/// </summary>
public class DataRootTests : IDisposable
{
    private const string Setting = "Storage:DataRoot";
    private const string Marker = ".data-root";

    private readonly string _base = Path.Combine(Path.GetTempPath(), "ballast-dataroot-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        if (Directory.Exists(_base))
        {
            Directory.Delete(_base, recursive: true);
        }
    }

    /// <summary>A path under the test's own folder. Not created.</summary>
    private string PathNamed(string name) => Path.Combine(_base, name);

    /// <summary>A folder under the test's own folder, created empty.</summary>
    private string FolderNamed(string name)
    {
        var path = PathNamed(name);
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>A folder that carries the marker file, as a correctly prepared data folder does.</summary>
    private string MarkedFolder(string name)
    {
        var path = FolderNamed(name);
        File.WriteAllText(Path.Combine(path, Marker), string.Empty);
        return path;
    }

    private static DataRoot Resolve(string? configured, string defaultPath, bool isDevelopment) =>
        DataRoot.Resolve(configured, defaultPath, isDevelopment, Setting, Marker);

    [Fact]
    public void Development_with_a_blank_setting_uses_the_default()
    {
        var content = PathNamed("content");

        var root = Resolve(null, content, isDevelopment: true);

        Assert.Equal(Path.GetFullPath(content), root.FullPath);
    }

    [Fact]
    public void Development_checks_and_creates_nothing()
    {
        var content = PathNamed("content");
        var configured = PathNamed("configured");

        var fromDefault = Resolve("   ", content, isDevelopment: true);
        var fromSetting = Resolve(configured, content, isDevelopment: true);

        Assert.Equal(Path.GetFullPath(content), fromDefault.FullPath);
        Assert.Equal(Path.GetFullPath(configured), fromSetting.FullPath);
        Assert.False(Directory.Exists(content));
        Assert.False(Directory.Exists(configured));
    }

    [Fact]
    public void Development_with_a_configured_path_and_no_marker_is_accepted()
    {
        var configured = PathNamed("configured");

        var root = Resolve(configured, PathNamed("content"), isDevelopment: true);

        Assert.Equal(Path.GetFullPath(configured), root.FullPath);
        Assert.False(root.MarkerExists());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Outside_development_a_blank_setting_refuses_to_start_and_names_the_setting(string? configured)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Resolve(configured, PathNamed("content"), isDevelopment: false));

        Assert.Contains(Setting, ex.Message, StringComparison.Ordinal);
        Assert.Contains(Marker, ex.Message, StringComparison.Ordinal);
        Assert.Contains("read-only", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Outside_development_a_blank_setting_is_refused_even_when_a_default_exists()
    {
        var content = FolderNamed("content");

        Assert.Throws<InvalidOperationException>(() => Resolve(null, content, isDevelopment: false));
    }

    [Fact]
    public void Outside_development_a_missing_folder_refuses_to_start_names_the_path_and_is_not_created()
    {
        var missing = PathNamed("missing");

        var ex = Assert.Throws<InvalidOperationException>(() => Resolve(missing, PathNamed("content"), isDevelopment: false));

        Assert.Contains(Path.GetFullPath(missing), ex.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(missing));
    }

    [Fact]
    public void Outside_development_a_folder_without_the_marker_refuses_to_start_and_names_the_marker()
    {
        var unmarked = FolderNamed("unmarked");

        var ex = Assert.Throws<InvalidOperationException>(() => Resolve(unmarked, PathNamed("content"), isDevelopment: false));

        Assert.Contains(Marker, ex.Message, StringComparison.Ordinal);
        Assert.Contains("wrong or empty data folder", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Outside_development_a_marker_that_is_a_directory_is_refused()
    {
        var folder = FolderNamed("marker-dir");
        Directory.CreateDirectory(Path.Combine(folder, Marker));

        var ex = Assert.Throws<InvalidOperationException>(() => Resolve(folder, PathNamed("content"), isDevelopment: false));

        Assert.Contains(Marker, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Outside_development_a_marked_folder_is_accepted_and_resolves_to_an_absolute_path()
    {
        var marked = MarkedFolder("marked");

        var root = Resolve(marked, PathNamed("content"), isDevelopment: false);

        Assert.True(Path.IsPathFullyQualified(root.FullPath));
        Assert.Equal(Path.GetFullPath(marked), root.FullPath);
        Assert.Equal(Marker, root.MarkerFileName);
        Assert.Equal(Path.Combine(root.FullPath, Marker), root.MarkerPath);
        Assert.True(root.MarkerExists());
    }

    [Fact]
    public void The_setting_name_given_is_the_one_the_messages_name()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => DataRoot.Resolve(null, PathNamed("content"), false, "Reports:Folder", ".reports-root"));

        Assert.Contains("Reports:Folder", ex.Message, StringComparison.Ordinal);
        Assert.Contains(".reports-root", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Setting, ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_default_is_refused(string? defaultPath)
    {
        Assert.ThrowsAny<ArgumentException>(() => DataRoot.Resolve(null, defaultPath!, true, Setting, Marker));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void A_blank_setting_name_or_marker_name_is_refused(string? name)
    {
        Assert.ThrowsAny<ArgumentException>(() => DataRoot.Resolve(null, PathNamed("content"), true, name!, Marker));
        Assert.ThrowsAny<ArgumentException>(() => DataRoot.Resolve(null, PathNamed("content"), true, Setting, name!));
    }

    [Fact]
    public void A_marker_name_with_a_folder_in_it_is_refused()
    {
        var ex = Assert.Throws<ArgumentException>(() => DataRoot.Resolve(null, PathNamed("content"), true, Setting, "sub/.data-root"));

        Assert.Equal("markerFileName", ex.ParamName);
    }
}
