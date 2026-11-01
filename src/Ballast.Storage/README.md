# Ballast.Storage

Four things for code that writes files from requests. An atomic write puts a file in place whole or not at all. A sweep
removes the temporary files that a crash leaves behind. A safe key cannot name a path outside its folder, and it gives a
reason for each refusal. A file type is read from its first bytes. A data folder check stops a host from writing into a
read-only content folder. It depends on .NET alone, so a server and a phone app can both use it.

Install:

```
dotnet add package Ballast.Storage --prerelease
```

## Example

```csharp
using System.Text;
using Ballast.Storage;

// The data folder and a key that came from a request. SafePath refuses any key that could leave the folder.
var root = Path.Combine(Path.GetTempPath(), "ballast-storage-example");
var path = SafePath.Resolve(root, "uploads/2026/10/receipt.txt");

// The bytes go to a temporary file in the same folder and are then moved into place. An existing file is never replaced.
await AtomicFile.WriteAsync(path, Encoding.UTF8.GetBytes("Receipt 42"), AtomicWriteMode.NeverOverwrite);
Console.WriteLine(await File.ReadAllTextAsync(path));

// The type comes from the first bytes of the file, never from its name.
var kind = FileKinds.Detect(Encoding.ASCII.GetBytes("%PDF-1.7"));
Console.WriteLine($"{kind?.MediaType} {kind?.Extension}");
```

The example prints `Receipt 42` and then `application/pdf pdf`.

The usage guide, with every option and every way the library fails, is `docs/Storage.md` in the repository.
