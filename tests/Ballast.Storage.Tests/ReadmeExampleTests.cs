// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text;

using Ballast.Storage;

namespace Ballast.Storage.Tests;

/// <summary>
/// Runs the example in src/Ballast.Storage/README.md exactly as it is written there. The README's using lines are at the top of
/// this file, because a test method cannot hold them. The example's folder is removed before and after the run, so the example
/// can run again.
/// </summary>
public class ReadmeExampleTests
{
    [Fact]
    public async Task The_readme_example_runs_as_written()
    {
        var folder = Path.Combine(Path.GetTempPath(), "ballast-storage-example");
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }

        try
        {
            // The example, copied verbatim from src/Ballast.Storage/README.md.
            // The data folder and a key that came from a request. SafePath refuses any key that could leave the folder.
            var root = Path.Combine(Path.GetTempPath(), "ballast-storage-example");
            var path = SafePath.Resolve(root, "uploads/2026/10/receipt.txt");

            // The bytes go to a temporary file in the same folder and are then moved into place. An existing file is never replaced.
            await AtomicFile.WriteAsync(path, Encoding.UTF8.GetBytes("Receipt 42"), AtomicWriteMode.NeverOverwrite);
            Console.WriteLine(await File.ReadAllTextAsync(path));

            // The type comes from the first bytes of the file, never from its name.
            var kind = FileKinds.Detect(Encoding.ASCII.GetBytes("%PDF-1.7"));
            Console.WriteLine($"{kind?.MediaType} {kind?.Extension}");

            Assert.Equal("Receipt 42", await File.ReadAllTextAsync(path));
            Assert.Equal("application/pdf", kind?.MediaType);
            Assert.Equal("pdf", kind?.Extension);
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    [Fact]
    public void The_readme_bounded_read_example_runs_as_written()
    {
        // The example, copied from src/Ballast.Storage/README.md.
        using var entry = new MemoryStream(new byte[100]);
        using var bounded = new BoundedReadStream(entry, 10, "stops.txt");
        var buffer = new byte[100];
        var ex = Assert.Throws<InvalidDataException>(() => bounded.Read(buffer, 0, buffer.Length));

        Assert.Equal("stops.txt expands past its declared size of 10 bytes", ex.Message);
    }
}
