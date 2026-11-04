# Ballast.Storage: usage guide

Ballast.Storage gives code that writes files from requests, and reads archive entries, six things:

1. **Atomic writes** (`AtomicFile.WriteAsync`, and `AtomicFile.Write` for synchronous code): a file appears whole or not at all, and an existing file is replaced or refused
   as the caller chooses.
2. **A sweep** (`AtomicFile.SweepStale`): the temporary files that a crash left behind are removed, and nothing else is.
3. **Safe keys** (`SafePath.Resolve`): a key that came from a request becomes a path under a root, or it is refused with a
   reason.
4. **The data folder** (`DataRoot.Resolve`): on a host whose content folder is read-only, the writable data folder is named in
   configuration and checked before anything is written.
5. **File types** (`FileKinds.Detect`): a file is recognised from its first bytes, never from its name or its declared type.
6. **Declared sizes** (`BoundedReadStream`): an archive entry that expands past the size it declared throws, while it is read.

It depends on .NET alone and no package. It uses no reflection and no dynamic code, so it stays trimmable and
ahead-of-time compatible. The package README has a short example that runs as written.

## How the pieces fit

1. A request sends a key. `SafePath.Resolve` turns it into a full path under the data folder, or refuses it with a
   `SafePathReason`.
2. `DataRoot.Resolve` gives the data folder once, at startup. Outside development it refuses to start on a folder that is
   missing or unmarked, so a wrong path is never written to.
3. `FileKinds.Detect` reads the first bytes of the content and says what it is, if it is one of the recognised types.
4. `AtomicFile.WriteAsync` writes the content to a temporary file in the same folder, flushes it, and moves it into place.
5. `AtomicFile.SweepStale`, run at startup or on a timer, removes the temporary files that a crash left in a folder.

## AtomicWriteMode

```csharp
public enum AtomicWriteMode { Overwrite, NeverOverwrite }
```

| Member | Meaning |
|---|---|
| `Overwrite` | The new content replaces an existing file. The replacement is one rename, so a reader sees the old file or the new one. |
| `NeverOverwrite` | Create-new. An existing file is refused with an `IOException` and left untouched. A name is taken once. |

There is no default: every write names its mode.

## AtomicFile

```csharp
public static class AtomicFile
```

### WriteAsync: bytes

```csharp
public static Task WriteAsync(string path, ReadOnlyMemory<byte> content, AtomicWriteMode mode, CancellationToken ct = default)
```

### WriteAsync: stream

```csharp
public static Task WriteAsync(string path, Stream content, AtomicWriteMode mode, CancellationToken ct = default)
```

The stream is read from its current position to its end, and it is not disposed.

### WriteAsync: writer

```csharp
public static Task WriteAsync(string path, Func<Stream, CancellationToken, Task> writeContent, AtomicWriteMode mode, CancellationToken ct = default)
```

The library gives `writeContent` a temporary stream and the token. The writer writes into the stream and must not dispose it:
the library flushes it to disk, closes it, and then moves the file into place.

| Parameter | Required | Meaning |
|---|---|---|
| `path` | yes | The final file path. Its folder is created when it does not exist. |
| `content`, `writeContent` | yes | The bytes, the stream, or the writer. |
| `mode` | yes | An `AtomicWriteMode`. |
| `ct` | no | Cancels the write. Defaults to none. |

Fixed behaviour, which is not an option:

- The temporary file is created in the target's folder, with `FileShare.None`, under a name of 32 lower-case hex digits and
  `.atomic-tmp`. Two writers never share a temporary file.
- The buffer is 80 KB.
- Before the move, the content is flushed to disk.
- `Overwrite` moves the file with an overwrite. `NeverOverwrite` checks that the target does not exist before it writes, and
  again when it moves the file in, so a second writer that got past the first check is refused the same way.

Argument errors are thrown when the method is called. Every other failure, and the cancellation, is thrown by the returned task.
The exception is the one the failing step raised, unchanged.

### Ways it fails

| When | What is thrown | What is left on disk |
|---|---|---|
| `path` is null, empty or only white space | `ArgumentNullException` or `ArgumentException` (`path`), at the call | Nothing |
| `path` names a root, such as `C:\` | `ArgumentException` (`path`), at the call | Nothing |
| `mode` is not a value of `AtomicWriteMode` | `ArgumentOutOfRangeException` (`mode`), at the call | Nothing |
| `writeContent` is null | `ArgumentNullException` (`writeContent`), at the call | Nothing |
| `content` (stream) is null | `ArgumentNullException` (`content`), at the call | Nothing |
| `NeverOverwrite` and the file exists when the call is made | `IOException` ("the file already exists"), at the call | Nothing: the existing file is untouched |
| `NeverOverwrite` and the file appears while the content is written | `IOException`, in the task, with the file system's error inside | The existing file is untouched; the temporary file is removed |
| The folder cannot be created, for example a file is in its place | The `IOException` or `UnauthorizedAccessException` from creating the folder, in the task | Nothing |
| The writer throws | The writer's own exception, unchanged, in the task | The target is untouched; the temporary file is removed |
| `ct` is cancelled | `OperationCanceledException`, in the task | The target is untouched; the temporary file is removed |
| The flush to disk fails, for example the disk is full | The `IOException`, in the task | The target is untouched; the temporary file is removed |
| `Overwrite` and the file system refuses to replace the target, for example another process holds it open in a way that blocks replacement, or the target is read-only | `IOException` or `UnauthorizedAccessException`, in the task | The old file is untouched; the temporary file is removed |
| The write fails and the temporary file cannot be removed either | The original exception, unchanged. `Data[AtomicFile.TempFileNotRemovedKey]` holds the temporary file's path and the removal's reason. | The temporary file remains until `SweepStale` removes it |

`TempFileNotRemovedKey` is `"Ballast.Storage.TempFileNotRemoved"`. A caller that logs failures should read it.

### Write: synchronous

```csharp
public static void Write(string path, ReadOnlySpan<byte> content, AtomicWriteMode mode)
public static void Write(string path, Action<Stream> writeContent, AtomicWriteMode mode)
```

These are the synchronous forms, for code that returns no task. The bytes form takes a span, and the writer form takes an
`Action<Stream>`. The parameters mean what they mean for `WriteAsync`, and there is no cancellation token. Both forms share the
temporary file, flush, move and cleanup code with `WriteAsync`, so every rule above applies unchanged: the temporary name, the
flush before the move, the two modes, the removal of the temporary file on every failure, and `TempFileNotRemovedKey`.

The writer form must not dispose the stream, as for `WriteAsync`.

Every failure is thrown by the call itself, because there is no task. The "Ways it fails" table above applies, with "by the call"
in place of "in the task". The cancellation row does not apply.

### SweepStale

```csharp
public static AtomicSweepResult SweepStale(string folder, TimeSpan olderThan)
```

| Parameter | Required | Meaning |
|---|---|---|
| `folder` | yes | The folder to sweep. Only that folder is looked at, not its subfolders. |
| `olderThan` | yes | A temporary file is removed only when its last write is at least this old. There is no default: the caller chooses the age. |

What it removes: the files in `folder` whose names are 32 lower-case hex digits followed by `.atomic-tmp`, and whose last write
(in UTC) is at least `olderThan` ago. Every other file is left alone, including `gtfs.zip.tmp`, `*.part`, a name with 31 digits,
a name in upper-case hex, and a folder whose name looks the same.

Choose `olderThan` longer than the longest write into the folder. A file that a live writer is still filling is young, and a
young file is never removed.

The result is an `AtomicSweepResult`:

| Member | Meaning |
|---|---|
| `Removed` | The full paths of the removed files. Never null; empty when none. |
| `Failed` | The old files that could not be removed, each as an `AtomicSweepFailure` with its `FullPath` and `Error`. Never null. |

`AtomicSweepFailure` has the members `FullPath` (string) and `Error` (exception).

| When | What happens |
|---|---|
| `folder` is null, empty or only white space | `ArgumentException` (`folder`), at the call |
| `olderThan` is negative | `ArgumentOutOfRangeException` (`olderThan`), at the call |
| `folder` does not exist | The result is empty. Nothing is created and no error is raised. |
| `folder` exists but cannot be listed | `IOException` or `UnauthorizedAccessException`, thrown: nothing could be swept |
| A matching file cannot be read or removed, for example another handle holds it open | It is listed in `Failed`. The sweep goes on with the other files. |
| A matching file is gone by the time it is checked | It is skipped without an entry. Another sweep or a finishing write removed it. |

## SafePath

```csharp
public static class SafePath
{
    public static string Resolve(string root, string key);
}
```

| Parameter | Required | Meaning |
|---|---|---|
| `root` | yes | The folder every key must stay inside. A relative root is resolved against the working directory. |
| `key` | yes | A relative key. Its segments are separated by `/`. |

There are no options: the rules are the same everywhere, so a key that is safe on one system is safe on all of them. Nothing is
created and nothing is read from the file system. The result is the full path of the key under the root.

### The rules

A key is refused when any of these holds:

- it is null, empty or only white space (`Empty`);
- it contains a backslash (`Backslash`), a colon (`Colon`), or starts with `/` (`RootedKey`);
- a segment is empty, which is two slashes in a row or a slash at the end (`EmptySegment`);
- a segment is `.` or `..` (`DotSegment`);
- a segment contains one of `< > " | ? *` (`ReservedCharacter`);
- a segment contains a control character, such as a tab or a NUL (`ControlCharacter`);
- a segment starts or ends with white space (`SurroundingSpace`);
- a segment ends with a dot (`TrailingDot`);
- a segment is a Windows device name with or without an extension: `CON`, `PRN`, `AUX`, `NUL`, `COM1` to `COM9`, `LPT1` to
  `LPT9` (`ReservedName`). The comparison ignores case, and it is made on every system.
- the resolved path is not inside the root (`OutsideRoot`). The rules above keep every key inside, so this is the final guard
  and no key that passes the others reaches it.

A dot inside a segment is an ordinary character: `a..b`, `.hidden` and `COM10.log` are accepted. A dot at the end of a segment is
refused (`TrailingDot`), and a segment that is exactly `.` or `..` is refused (`DotSegment`).

The containment check is textual: it compares full paths. On Windows it ignores case. A symbolic link inside the root that points
outside is not detected.

### Refusals

`SafePath.Resolve` throws a `SafePathException`, which is an `ArgumentException` on the parameter `key`. Its `Reason` says why:

| Reason | Example key |
|---|---|
| `Empty` | `""`, `"   "` |
| `Backslash` | `uploads\a.png`, `..\evil.txt` |
| `Colon` | `C:evil.txt` |
| `RootedKey` | `/etc/passwd` |
| `EmptySegment` | `uploads//a.png`, `uploads/` |
| `DotSegment` | `../evil.txt`, `uploads/./a.png`, `uploads/a/..` |
| `ReservedCharacter` | `uploads/a<b>.png`, `uploads/a|b.png` |
| `ControlCharacter` | `uploads/a\0b.png` |
| `SurroundingSpace` | `uploads/a.png `, ` uploads/a.png` |
| `TrailingDot` | `uploads/a.` |
| `ReservedName` | `uploads/NUL.txt`, `COM1`, `uploads/aux.tar.gz` |
| `OutsideRoot` | Not reached by any key that passes the rules above |

`SafePathException.Message` names the reason and not the key, so a log never receives a name that a request chose.

A blank `root` is an `ArgumentException` on `root`. A root that holds a character a path cannot hold is an `ArgumentException` from
the path functions.

### Keys that older copies allowed

Applications that named uploads and feed files with their own checks may have stored a name that this rule now refuses. Such a
name would no longer resolve. The shapes this rule refuses that a plain containment check allowed are: a backslash, a colon,
`< > " | ? *`, a control character, white space at either end of a segment, a trailing dot, an empty segment, a `.` segment,
and a Windows device name. On Linux the file system accepts all of these. Before a switch, list the stored names and check
them against `SafePath.Resolve`.

## DataRoot

```csharp
public sealed class DataRoot
{
    public static DataRoot Resolve(string? configured, string defaultPath, bool isDevelopment, string settingName, string markerFileName);

    public string FullPath { get; }
    public string MarkerFileName { get; }
    public string MarkerPath { get; }
    public bool MarkerExists();
}
```

| Parameter | Required | Meaning |
|---|---|---|
| `configured` | no | The folder named in configuration. Null or blank means none. |
| `defaultPath` | yes | The folder used in development when nothing is configured. Usually the content root. It is required in every environment. |
| `isDevelopment` | yes | True in the development environment. |
| `settingName` | yes | The configuration key as an operator writes it, such as `Storage:DataRoot`. It appears in every message. |
| `markerFileName` | yes | A plain file name that marks a correct data folder, such as `.data-root`. It must contain no folder. |

There are no defaults. The caller names each value, so the library holds no application name, configuration section or
environment variable.

Development: a configured folder is used as it is, and a blank one falls back to `defaultPath`. Nothing is checked and nothing is
created. The marker file is not needed.

Outside development: the setting is required. It must name an existing folder that holds the marker file. Nothing is created.

A relative `configured` path is resolved against the working directory, as `Path.GetFullPath` does. On a host where the working
directory is read-only, name the folder with an absolute path.

### Ways it fails

| When | What is thrown |
|---|---|
| `defaultPath`, `settingName` or `markerFileName` is null, empty or only white space | `ArgumentException` (the parameter's name), at the call |
| `markerFileName` contains a folder | `ArgumentException` (`markerFileName`), at the call |
| Outside development and `configured` is null or blank | `InvalidOperationException`. The message names `settingName` and `markerFileName`, and says that the content folder is normally read-only. |
| Outside development and the folder does not exist | `InvalidOperationException`. The message names the setting and the full path. The folder is not created. |
| Outside development and the folder has no marker file, or the marker is a folder | `InvalidOperationException`. The message names the marker. |
| `configured` contains a character that a path cannot hold | `ArgumentException` from the path functions |

A `DataRoot` that resolved is always a full path. `MarkerExists()` is checked when `Resolve` runs outside development, and it can be
asked again later. `MarkerPath` is `FullPath` with `MarkerFileName` added.

## FileKinds

```csharp
public static class FileKinds
{
    public static FileKindInfo? Detect(ReadOnlySpan<byte> header);
}

public enum FileKind { Jpeg, Png, Webp, Pdf, Zip, Heic }

public readonly record struct FileKindInfo(FileKind Kind, string MediaType, string Extension);
```

`Detect` takes the first bytes of the file. The first 12 bytes are enough for every type. A longer span is read the same way.

| Kind | Media type | Extension | Signature |
|---|---|---|---|
| `Jpeg` | `image/jpeg` | `jpg` | `FF D8 FF` |
| `Png` | `image/png` | `png` | the eight bytes `89 50 4E 47 0D 0A 1A 0A` |
| `Webp` | `image/webp` | `webp` | `RIFF` at byte 0 and `WEBP` at byte 8 (needs 12 bytes) |
| `Pdf` | `application/pdf` | `pdf` | `%PDF-` |
| `Zip` | `application/zip` | `zip` | `PK 03 04` (any ZIP archive, not a particular kind of file packed as one) |
| `Heic` | `image/heic` | `heic` | `ftyp` at byte 4 and an image brand at byte 8 (needs 12 bytes) |

The extension has no dot. The brands for `Heic` are `heic`, `heix`, `hevc`, `hevx`, `mif1`, `msf1`, `heim` and `heis`, compared
without regard to case. A brand such as `isom` is an MP4, and is not recognised.

Where the copies that an application had differed, this library takes the stricter signature. A PNG needs all eight bytes of its
signature, not four. A PDF needs the dash after `%PDF`.

### Ways it fails

`Detect` never throws. An empty span, a span shorter than a signature, or bytes that match none of them gives `null`. A
`null` result means unknown: the caller decides whether that is a refusal. The library never guesses from the name.

Text formats are not recognised: iCalendar, email, HTML and plain text have no signature, so an application identifies them
itself.

### Why the signatures are written here

The `FileSignatures` package (7.3.0, MIT) was checked first. It recognises JPEG, PNG, WebP, PDF and ZIP, but not HEIC or HEIF. It
depends on another package, `OpenMcdf`, and it finds its formats by reflection over the assembly. This library has no package
dependency and no reflection, so the five signatures and the HEIC brands are written here. They are short, and tests cover each
one.

## BoundedReadStream

`BoundedReadStream` is a read-only wrapper that throws once more bytes have been read than its limit. It holds an archive entry to the size
the entry declared. A limit on the whole archive is only a sum of the declared sizes, so without this an entry that lies about its size could
still expand past the limit while it is parsed or hashed.

```csharp
public BoundedReadStream(Stream inner, long limit, string what)
```

- `inner` is the stream to read. It is disposed with the wrapper. Null gives `ArgumentNullException`.
- `limit` is the most bytes that may be read, the declared size. A negative value gives `ArgumentOutOfRangeException`. Zero is allowed: an empty
  stream reads, and one byte is refused.
- `what` names the stream in the error, such as a file name. It is used as written; a null is written as an empty string.

### Ways it fails

| Situation | What happens |
|---|---|
| A read takes the total past `limit` | `InvalidDataException`, with the message `{what} expands past its declared size of {limit} bytes`. The bytes read before the refused read were returned already. |
| A read ends exactly at `limit` | No error. The next read returns the end of the stream, as a read of the inner stream does. |
| `Length`, `Position`, `Seek`, `SetLength`, `Write` | `NotSupportedException`: the wrapper is read-only and does not seek. |
| Disposed | The inner stream is disposed too. |

The refusal is an `InvalidDataException` (the type a reader already catches for a bad archive), not a type of the library's own. Read, ReadAsync
and the span read count the same bytes.

## Trimming and ahead-of-time compilation

The library's project sets `IsTrimmable` and `IsAotCompatible` through the repository's shared properties. It uses no reflection,
no dynamic code and no `Regex`. The build with those analyzers on and warnings as errors is the check for this.

## Platform notes

- Temporary files and their names are the same on every system. The sweep matches a name, not a location.
- The reserved names of `SafePath` are refused on every system, so a key does not change meaning between a development machine
  and a server.
- Windows is the only system where a file held open by another handle can refuse a delete or a replace. The guide's failure
  tables mark where that applies.
