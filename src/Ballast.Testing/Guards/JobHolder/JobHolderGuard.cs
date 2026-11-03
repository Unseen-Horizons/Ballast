// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Reflection;
using System.Text.RegularExpressions;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;

namespace Ballast.Testing.Guards.JobHolder;

/// <summary>The three classes a hosted service can have under the job lock.</summary>
public static class JobClass
{
    /// <summary>A worker that writes runs behind the lock: it is registered with the leader method and runs in the holder only.</summary>
    public const string HolderOnly = "holder only";

    /// <summary>A worker that only fills this copy's own memory: it is registered with the plain method and runs in every copy.</summary>
    public const string EveryCopy = "every copy";

    /// <summary>A worker split between writes that only the holder makes and memory every copy needs. It takes the job leadership to tell them apart.</summary>
    public const string Mixed = "mixed";

    /// <summary>True when <paramref name="value"/> is one of the three classes.</summary>
    /// <param name="value">The class name to test.</param>
    public static bool IsKnown(string value) => value is HolderOnly or EveryCopy or Mixed;
}

/// <summary>
/// The settings of the job-holder rules. The app names its own services, its controllers, its startup code and its registration methods;
/// the rules themselves are here.
/// </summary>
public sealed record JobHolderOptions
{
    /// <summary>The assemblies whose hosted services are classified: one per API, and a shared one where it runs in several. Required.</summary>
    public required IReadOnlyList<Assembly> HostedServiceAssemblies { get; init; }

    /// <summary>Each hosted service's type name and its <see cref="JobClass"/>. Required.</summary>
    public required IReadOnlyDictionary<string, string> Classification { get; init; }

    /// <summary>
    /// For each assembly, the text of each source that registers its hosted services. A service is registered once in each source
    /// that runs it. Required.
    /// </summary>
    public required IReadOnlyDictionary<Assembly, IReadOnlyList<string>> RegistrationSources { get; init; }

    /// <summary>The registration method of holder-only services. Default: <c>AddLeaderHostedService</c>.</summary>
    public string LeaderRegistration { get; init; } = "AddLeaderHostedService";

    /// <summary>The registration method of services that run in every copy. Default: <c>AddHostedService</c>.</summary>
    public string PlainRegistration { get; init; } = "AddHostedService";

    /// <summary>Text that a named service's registration must contain, such as <c>once: true</c>. Default: none.</summary>
    public IReadOnlyDictionary<string, string> RequiredRegistrationText { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// For each mixed service, the type whose constructor takes the job leadership: the service itself, or a manager it calls. Default: none.
    /// </summary>
    public IReadOnlyDictionary<string, Type> MixedCarriers { get; init; } = new Dictionary<string, Type>(StringComparer.Ordinal);

    /// <summary>The interface that gives a worker the job leadership. Required when a service is mixed.</summary>
    public Type? LeadershipType { get; init; }

    /// <summary>The attribute that marks an admin action as answered only by the job holder. Required.</summary>
    public required Type HolderAttribute { get; init; }

    /// <summary>The assemblies whose controllers' actions are checked for the holder attribute. Required.</summary>
    public required IReadOnlyList<Assembly> ControllerAssemblies { get; init; }

    /// <summary>
    /// The actions that carry the holder attribute, as <c>Controller.Action</c>, and <c>Controller.*</c> for a class-level attribute. Each
    /// API's copy of a controller is listed. Required; it may be empty.
    /// </summary>
    public required IReadOnlyList<string> HolderActions { get; init; }

    /// <summary>The job lock's registration call in a startup source. Default: <c>AddJobLeadership(</c>.</summary>
    public string LockCall { get; init; } = "AddJobLeadership(";

    /// <summary>The call that releases the lock on a graceful shutdown. Empty skips the check. Default: <c>ConfigureGracefulShutdown(</c>.</summary>
    public string ShutdownCall { get; init; } = "ConfigureGracefulShutdown(";

    /// <summary>The method-name prefix of publish-anyway actions. Empty skips the name test. Default: <c>PublishHeldVersion</c>.</summary>
    public string PublishMethodPrefix { get; init; } = "PublishHeldVersion";

    /// <summary>The route fragment of publish-anyway actions. Empty skips the route test. Default: <c>publish-anyway</c>.</summary>
    public string PublishRouteFragment { get; init; } = "publish-anyway";

    /// <summary>The publish-anyway actions expected, as <c>Controller.Action</c>. Default: none.</summary>
    public IReadOnlyList<string> PublishActions { get; init; } = [];
}

/// <summary>
/// The job-lock rules. Background work that writes runs in one copy of an app at a time, the copy that holds the lock. These checks keep
/// that true as the code changes: every hosted service is classified and registered to match, the lock is registered before the first
/// hosted service, every admin action that starts background work carries the holder attribute, and the files that write a fixed
/// temporary name are the ones listed.
/// </summary>
/// <remarks>
/// Each check returns problems as text, and an empty list passes. The checks read the app's assemblies and its source text. They start no
/// host and take no lock.
/// </remarks>
public static class JobHolderGuard
{
    /// <summary>The needle of a fixed temporary name: a path string that ends in <c>.tmp</c>.</summary>
    public const string FixedTmpNeedle = ".tmp\"";

    private static readonly string[] CSharpExtension = [".cs"];

    /// <summary>
    /// The classification's problems: a hosted service with no class, a class that is not one of the three, and an entry that names no
    /// hosted service.
    /// </summary>
    /// <param name="options">The settings. Required.</param>
    public static IReadOnlyList<string> ClassificationProblems(JobHolderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var found = HostedServices(options.HostedServiceAssemblies).Select(type => type.Name).ToHashSet(StringComparer.Ordinal);
        List<string> problems = [];
        foreach (var name in found.Order(StringComparer.Ordinal))
        {
            if (!options.Classification.ContainsKey(name))
            {
                problems.Add($"UNCLASSIFIED {name}: add it to the classification as holder only, every copy or mixed, and register it to match.");
            }
        }

        foreach (var (name, job) in options.Classification.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            if (!JobClass.IsKnown(job))
            {
                problems.Add($"{name}: '{job}' is not one of the job classes: holder only, every copy or mixed.");
            }

            if (!found.Contains(name))
            {
                problems.Add($"STALE {name}: classified, but no hosted service has that name. Remove the entry.");
            }
        }

        return problems;
    }

    /// <summary>
    /// The registration problems: each hosted service is registered once in each source that runs it, with the method its class needs,
    /// and with any text the options require.
    /// </summary>
    /// <param name="options">The settings. Required.</param>
    public static IReadOnlyList<string> RegistrationProblems(JobHolderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var statement = RegistrationPattern(options);
        List<string> problems = [];
        foreach (var type in HostedServices(options.HostedServiceAssemblies))
        {
            if (!options.Classification.TryGetValue(type.Name, out var job) || !JobClass.IsKnown(job))
            {
                continue;
            }

            if (!options.RegistrationSources.TryGetValue(type.Assembly, out var sources))
            {
                problems.Add($"{type.Name}: no registration source is named for its assembly, so its registration cannot be checked.");
                continue;
            }

            var name = new Regex(@"\b" + Regex.Escape(type.Name) + @"\b", RegexOptions.CultureInvariant);
            List<List<(string Method, string Text)>> perSource = [];
            foreach (var source in sources)
            {
                var code = SourceScan.StripCSharpComments(source);
                perSource.Add(Registrations(code, statement).Where(r => name.IsMatch(r.Text)).ToList());
            }

            var statements = perSource.SelectMany(s => s).ToList();
            if (statements.Count == 0 || perSource.Any(s => s.Count > 1))
            {
                problems.Add($"{type.Name} is registered {statements.Count} times, expected once in each API that runs it.");
                continue;
            }

            foreach (var (method, text) in statements)
            {
                if (job == JobClass.HolderOnly && method != options.LeaderRegistration)
                {
                    problems.Add($"{type.Name} is holder only but is registered with {method}.");
                }

                if (job != JobClass.HolderOnly && method != options.PlainRegistration)
                {
                    problems.Add($"{type.Name} runs in every copy but is registered with {method}.");
                }

                if (options.RequiredRegistrationText.TryGetValue(type.Name, out var needed) && !text.Contains(needed, StringComparison.Ordinal))
                {
                    problems.Add($"{type.Name}: its registration must contain '{needed}'.");
                }
            }
        }

        return problems;
    }

    /// <summary>
    /// The mixed-class problems: each mixed service names a carrier, each carrier is a mixed service, and each carrier has a constructor
    /// that takes the job leadership. A split worker that cannot see the leadership would run its holder-only part in every copy.
    /// </summary>
    /// <param name="options">The settings. Required. <see cref="JobHolderOptions.LeadershipType"/> is required when a service is mixed.</param>
    /// <exception cref="ArgumentException">A service is mixed, but no leadership type is named.</exception>
    public static IReadOnlyList<string> MixedLeadershipProblems(JobHolderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var mixed = options.Classification.Where(entry => entry.Value == JobClass.Mixed).Select(entry => entry.Key).Order(StringComparer.Ordinal).ToList();
        if (mixed.Count > 0 && options.LeadershipType is null)
        {
            throw new ArgumentException("A leadership type is required, because a hosted service is classified as mixed.", nameof(options));
        }

        List<string> problems = [];
        foreach (var name in mixed.Where(member => !options.MixedCarriers.ContainsKey(member)))
        {
            problems.Add($"{name} is split between the holder and every copy, but names no carrier. Add the type whose constructor takes the job leadership.");
        }

        foreach (var (name, carrier) in options.MixedCarriers.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            if (!options.Classification.TryGetValue(name, out var job) || job != JobClass.Mixed)
            {
                problems.Add($"{name} names a carrier, but it is not classified as mixed. Fix the class or remove the carrier.");
            }
            else if (!TakesLeadership(carrier, options.LeadershipType!))
            {
                problems.Add($"{carrier.Name} is the carrier of {name}, but no constructor takes {options.LeadershipType!.Name}, so it cannot tell the holder from the copies.");
            }
        }

        return problems;
    }

    /// <summary>
    /// The startup problems of one project: the job lock is registered, the shutdown call is made, a hosted service is registered only after
    /// the lock, and each call in <paramref name="callsAfterLock"/> is made after it.
    /// </summary>
    /// <param name="project">The project's name, which starts each message. Required.</param>
    /// <param name="startupSource">The text of the project's startup code. Required.</param>
    /// <param name="options">The settings. Required.</param>
    /// <param name="callsAfterLock">Calls that must come after the lock in this startup code. Default: none.</param>
    public static IReadOnlyList<string> StartupProblems(
        string project,
        string startupSource,
        JobHolderOptions options,
        IEnumerable<string>? callsAfterLock = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(project);
        ArgumentNullException.ThrowIfNull(startupSource);
        ArgumentNullException.ThrowIfNull(options);

        var code = SourceScan.StripCSharpComments(startupSource);
        List<string> problems = [];

        // Hosted services start in registration order and stop in the reverse, so the lock must be registered first.
        var lockAt = code.IndexOf(options.LockCall, StringComparison.Ordinal);
        if (lockAt < 0)
        {
            problems.Add($"{project} does not register the job lock: no '{options.LockCall}' call was found.");
            return problems;
        }

        if (options.ShutdownCall.Length > 0 && !code.Contains(options.ShutdownCall, StringComparison.Ordinal))
        {
            problems.Add($"{project} does not call {options.ShutdownCall}, so the lock is not released in order on shutdown.");
        }

        var first = RegistrationPattern(options).Match(code);
        if (!first.Success)
        {
            problems.Add($"{project} registers no hosted service.");
        }
        else if (lockAt > first.Index)
        {
            problems.Add($"{project} registers a hosted service before the job lock.");
        }

        if (callsAfterLock is not null)
        {
            foreach (var call in callsAfterLock)
            {
                var at = code.IndexOf(call, StringComparison.Ordinal);
                if (at < 0)
                {
                    problems.Add($"{project} does not call {call}, which must follow the job lock.");
                }
                else if (at < lockAt)
                {
                    problems.Add($"{project} calls {call} before the job lock.");
                }
            }
        }

        return problems;
    }

    /// <summary>
    /// The holder-action problems: every action that carries the holder attribute is listed, and every listed action carries it.
    /// </summary>
    /// <param name="options">The settings. Required.</param>
    public static IReadOnlyList<string> HolderActionProblems(JobHolderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var found = HolderActionKeys(options.ControllerAssemblies, options.HolderAttribute);
        List<string> problems = [];
        foreach (var key in GuardText.Surplus(found, options.HolderActions))
        {
            problems.Add($"{key} carries the job-holder attribute but is not in the holder action list. Add it there, so the list stays reviewed.");
        }

        foreach (var key in GuardText.Surplus(options.HolderActions, found))
        {
            problems.Add($"{key} is in the holder action list, but no action of that name carries the job-holder attribute. Remove it, or add the attribute.");
        }

        return problems;
    }

    /// <summary>
    /// The publish-anyway problems: every publish-anyway action carries the holder attribute, because it runs the housekeeping that writes,
    /// and the publish-anyway actions found are the expected ones. A publish action is found by its name or its route.
    /// </summary>
    /// <param name="options">The settings. Required.</param>
    public static IReadOnlyList<string> PublishActionProblems(JobHolderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string> found = [];
        List<string> problems = [];
        foreach (var assembly in options.ControllerAssemblies)
        {
            foreach (var type in ControllerTypes(assembly))
            {
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    var route = method.GetCustomAttribute<HttpPostAttribute>()?.Template ?? string.Empty;
                    if (!IsPublishShape(method.Name, route, options))
                    {
                        continue;
                    }

                    var key = $"{type.Name}.{method.Name}";
                    found.Add(key);
                    if (method.GetCustomAttribute(options.HolderAttribute) is null)
                    {
                        problems.Add($"{key} is a publish-anyway action and must carry the job-holder attribute: publishing runs the housekeeping, which writes.");
                    }
                }
            }
        }

        foreach (var key in GuardText.Surplus(found, options.PublishActions))
        {
            problems.Add($"{key} is a publish-anyway action but is not in the expected list. Add it there.");
        }

        foreach (var key in GuardText.Surplus(options.PublishActions, found))
        {
            problems.Add($"{key} is in the expected publish-anyway list, but no such action exists. Remove it.");
        }

        return problems;
    }

    /// <summary>
    /// The fixed-temporary-name problems: the files whose code writes a path ending in <c>.tmp</c> are exactly the expected ones. Two copies
    /// of an app that write one fixed name tear each other's file.
    /// </summary>
    /// <param name="sources">The C# sources, as <see cref="CSharpSources"/> returns them. Required.</param>
    /// <param name="expected">The files expected to write a fixed name, with a reason in the app's own list. Required; it may be empty.</param>
    public static IReadOnlyList<string> FixedTmpWriterProblems(IEnumerable<(string RelativePath, string Source)> sources, IEnumerable<string> expected)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(expected);

        var listed = expected.ToList();
        var found = sources
            .Where(source => SourceScan.StripCSharpComments(source.Source).Contains(FixedTmpNeedle, StringComparison.Ordinal))
            .Select(source => source.RelativePath)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        List<string> problems = [];
        foreach (var path in GuardText.Surplus(found, listed))
        {
            problems.Add($"{path} writes a fixed .tmp name, so two copies of the app can tear the file. Write through a temp name of its own, or list it with a reason.");
        }

        foreach (var path in GuardText.Surplus(listed, found))
        {
            problems.Add($"{path} is listed as a fixed .tmp writer, but it no longer writes one. Remove it from the list.");
        }

        return problems;
    }

    /// <summary>
    /// The C# sources under the given folders of a repository, as a path with forward slashes from the root and the file's text. Build
    /// output and the IDE's state are skipped, and a file over 3 MB is skipped.
    /// </summary>
    /// <param name="root">The repository root. Required.</param>
    /// <param name="folders">The folders to scan, relative to the root. Required.</param>
    /// <exception cref="DirectoryNotFoundException">A folder does not exist.</exception>
    public static IReadOnlyList<(string RelativePath, string Source)> CSharpSources(string root, IEnumerable<string> folders)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(folders);

        List<(string RelativePath, string Source)> sources = [];
        foreach (var folder in folders)
        {
            foreach (var file in SourceScan.Files(Path.Combine(root, folder), CSharpExtension))
            {
                sources.Add(($"{folder}/{file.RelativePath}", File.ReadAllText(file.FullPath)));
            }
        }

        return sources;
    }

    internal static List<Type> HostedServices(IEnumerable<Assembly> assemblies) =>
        assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(IHostedService).IsAssignableFrom(type))
            .ToList();

    internal static List<string> HolderActionKeys(IEnumerable<Assembly> assemblies, Type holderAttribute)
    {
        List<string> keys = [];
        foreach (var assembly in assemblies)
        {
            foreach (var type in ControllerTypes(assembly))
            {
                if (type.GetCustomAttribute(holderAttribute) is not null)
                {
                    keys.Add($"{type.Name}.*");
                }

                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (method.GetCustomAttribute(holderAttribute) is not null)
                    {
                        keys.Add($"{type.Name}.{method.Name}");
                    }
                }
            }
        }

        return keys;
    }

    private static IEnumerable<Type> ControllerTypes(Assembly assembly) =>
        assembly.GetTypes().Where(type => typeof(ControllerBase).IsAssignableFrom(type));

    private static Regex RegistrationPattern(JobHolderOptions options) =>
        new(@"\b(" + Regex.Escape(options.PlainRegistration) + "|" + Regex.Escape(options.LeaderRegistration) + @")\b[^;]*;", RegexOptions.CultureInvariant);

    private static List<(string Method, string Text)> Registrations(string code, Regex statement)
    {
        List<(string Method, string Text)> found = [];
        foreach (Match match in statement.Matches(code))
        {
            found.Add((match.Groups[1].Value, match.Value));
        }

        return found;
    }

    private static bool IsPublishShape(string methodName, string route, JobHolderOptions options) =>
        (options.PublishMethodPrefix.Length > 0 && methodName.StartsWith(options.PublishMethodPrefix, StringComparison.Ordinal))
        || (options.PublishRouteFragment.Length > 0 && route.Contains(options.PublishRouteFragment, StringComparison.Ordinal));

    private static bool TakesLeadership(Type type, Type leadership) =>
        type.GetConstructors().Any(constructor => constructor.GetParameters().Any(parameter => parameter.ParameterType == leadership));
}
