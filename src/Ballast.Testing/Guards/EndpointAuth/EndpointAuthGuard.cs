// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Reflection;
using System.Text.RegularExpressions;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace Ballast.Testing.Guards.EndpointAuth;

/// <summary>The settings of the endpoint-authorization rules: the app-session marker's name, and the startup code's words.</summary>
public sealed record EndpointAuthOptions
{
    /// <summary>
    /// The name of the app-session marker attribute. It is matched by name, so the rule reads any API's assembly without referencing it.
    /// Default: <c>RequireAppSessionAttribute</c>.
    /// </summary>
    public string AppSessionMarker { get; init; } = "RequireAppSessionAttribute";

    /// <summary>The variable the startup code maps minimal routes on. Default: <c>app</c>.</summary>
    public string AppVariable { get; init; } = "app";

    /// <summary>The one health call the startup code must make, exactly once. Default: <c>app.MapBallastHealth()</c>.</summary>
    public string HealthCall { get; init; } = "app.MapBallastHealth()";
}

/// <summary>One controller action, as the resolver found it: its key, its route and the authorization it resolves to.</summary>
/// <param name="Key">The controller and the action, as <c>Controller.Action</c>.</param>
/// <param name="Route">The verb and the route, such as <c>GET /orders/{id}</c>.</param>
/// <param name="Auth">The authorization value, as <see cref="EndpointAuthGuard.Anonymous"/> or a sorted list of policies and roles.</param>
public sealed record DiscoveredEndpoint(string Key, string Route, string Auth);

/// <summary>
/// The endpoint-authorization rules. Every controller action of an API carries an authorization that a reviewed table names. A new action
/// with no entry fails, a removed one leaves a stale entry that fails, and an action whose authorization changed fails with the old and new
/// values side by side. So an anonymous action added by accident, or an authorize attribute dropped, is a failed check, not a finding found
/// later.
/// </summary>
/// <remarks>
/// The resolver reads the same attributes the framework turns into endpoint metadata, so it is a unit-level check with no host and no
/// database. The tables are the app's: this library supplies the discovery and the comparisons, and the app supplies the expected values.
/// </remarks>
public static class EndpointAuthGuard
{
    /// <summary>An action that anyone may call.</summary>
    public const string Anonymous = "Anonymous";

    /// <summary>An action that needs a signed-in user and no specific permission.</summary>
    public const string Authenticated = "Authenticated";

    /// <summary>A data action that the app (an app session) or a signed-in user may call. The action is anonymous, and it carries the marker.</summary>
    public const string AppOrUser = "AppOrUser";

    /// <summary>The marker on an action that is not anonymous. The framework would demand a user token before the gate ran, so it is always a mistake.</summary>
    public const string AppOrUserBlockedByAuthorize = "AppOrUser-BLOCKED-BY-AUTHORIZE";

    /// <summary>No authorize attribute and no anonymous attribute anywhere. The API sets no fallback policy, so such an action is open.</summary>
    public const string Unprotected = "UNPROTECTED";

    /// <summary>
    /// Every public, non-special action of the controllers in an assembly, with the route and the authorization it resolves to. Controllers
    /// marked <c>[NonController]</c> and actions marked <c>[NonAction]</c> are left out.
    /// </summary>
    /// <param name="api">The assembly that holds the controllers. Required.</param>
    /// <param name="options">The settings. Default: <see cref="EndpointAuthOptions"/> with its defaults.</param>
    public static IReadOnlyList<DiscoveredEndpoint> Discover(Assembly api, EndpointAuthOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(api);
        var settings = options ?? new EndpointAuthOptions();

        List<DiscoveredEndpoint> result = [];
        var controllers = api.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false, IsPublic: true } && typeof(ControllerBase).IsAssignableFrom(type))
            .Where(type => !type.IsDefined(typeof(NonControllerAttribute), inherit: true))
            .OrderBy(type => type.Name, StringComparer.Ordinal);

        foreach (var controller in controllers)
        {
            var classRoute = controller.GetCustomAttributes<RouteAttribute>(inherit: true).FirstOrDefault()?.Template ?? string.Empty;
            var actions = controller
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(method => !method.IsSpecialName && !method.IsDefined(typeof(NonActionAttribute), inherit: true))
                .OrderBy(method => method.Name, StringComparer.Ordinal)
                .ThenBy(method => method.GetParameters().Length);

            foreach (var action in actions)
            {
                var verbs = action.GetCustomAttributes<HttpMethodAttribute>(inherit: true).ToList();
                var verb = verbs.Count == 0 ? "ANY" : string.Join("|", verbs.SelectMany(v => v.HttpMethods).Distinct());
                var template = verbs.Select(v => v.Template).FirstOrDefault(t => t is not null)
                    ?? action.GetCustomAttribute<RouteAttribute>()?.Template
                    ?? string.Empty;
                var route = NormaliseRoute($"{verb} /{classRoute}/{template}", controller);

                result.Add(new DiscoveredEndpoint($"{controller.Name}.{action.Name}", route, ResolveAuth(controller, action, settings)));
            }
        }

        return result;
    }

    /// <summary>
    /// The authorization an action resolves to, as the framework would apply it. An anonymous attribute on the class or the action wins: ASP.NET
    /// lets a request through when an anonymous attribute sits anywhere on the action or its controller. Otherwise the authorize data of the
    /// controller and the action applies together, and is summarised as sorted policies and roles.
    /// </summary>
    /// <param name="controller">The controller type. Required.</param>
    /// <param name="action">The action method. Required.</param>
    /// <param name="options">The settings. Default: <see cref="EndpointAuthOptions"/> with its defaults.</param>
    public static string ResolveAuth(Type controller, MethodInfo action, EndpointAuthOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(action);
        var settings = options ?? new EndpointAuthOptions();

        var anonymous = action.GetCustomAttributes(inherit: true).OfType<IAllowAnonymous>().Any()
            || controller.GetCustomAttributes(inherit: true).OfType<IAllowAnonymous>().Any();

        var appSession = action.GetCustomAttributes(inherit: true).Concat(controller.GetCustomAttributes(inherit: true))
            .Any(attribute => attribute.GetType().Name == settings.AppSessionMarker);
        if (appSession)
        {
            return anonymous ? AppOrUser : AppOrUserBlockedByAuthorize;
        }

        if (anonymous)
        {
            return Anonymous;
        }

        var data = controller.GetCustomAttributes(inherit: true).OfType<IAuthorizeData>()
            .Concat(action.GetCustomAttributes(inherit: true).OfType<IAuthorizeData>())
            .ToList();
        if (data.Count == 0)
        {
            return Unprotected;
        }

        var parts = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var item in data)
        {
            if (!string.IsNullOrEmpty(item.Policy))
            {
                parts.Add("Policy:" + item.Policy);
            }

            if (!string.IsNullOrEmpty(item.Roles))
            {
                parts.Add("Roles:" + item.Roles);
            }

            if (!string.IsNullOrEmpty(item.AuthenticationSchemes))
            {
                parts.Add("Schemes:" + item.AuthenticationSchemes);
            }
        }

        return parts.Count == 0 ? Authenticated : string.Join("+", parts);
    }

    /// <summary>
    /// Compares what discovery found with the reviewed table. One line per problem; empty means the table is complete, current and correct.
    /// An action with no entry is unclassified. An action whose value differs from its entry has changed. An entry with no action is stale.
    /// An entry set to <see cref="Unprotected"/> is itself a problem.
    /// </summary>
    /// <param name="actual">The discovered actions. Required.</param>
    /// <param name="expected">The reviewed table: each action's key and the authorization it must have. Required.</param>
    /// <param name="tableName">The table's name, which the messages name. Required.</param>
    public static IReadOnlyList<string> Compare(
        IEnumerable<DiscoveredEndpoint> actual,
        IReadOnlyDictionary<string, string> expected,
        string tableName)
    {
        ArgumentNullException.ThrowIfNull(actual);
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        var discovered = actual.ToList();
        var actualKeys = discovered.Select(endpoint => endpoint.Key).ToHashSet(StringComparer.Ordinal);
        List<string> problems = [];
        foreach (var endpoint in discovered.OrderBy(endpoint => endpoint.Key, StringComparer.Ordinal))
        {
            if (!expected.TryGetValue(endpoint.Key, out var want))
            {
                problems.Add($"UNCLASSIFIED {endpoint.Key} ({endpoint.Route}) resolves to {endpoint.Auth}: add \"{endpoint.Key}\" to {tableName} with the auth it is meant to have.");
            }
            else if (want != endpoint.Auth)
            {
                problems.Add($"CHANGED {endpoint.Key} ({endpoint.Route}) resolves to {endpoint.Auth} but {tableName} expects {want}: fix the attribute, or update the table if the change is intended and reviewed.");
            }
        }

        foreach (var key in expected.Keys.Where(key => !actualKeys.Contains(key)).OrderBy(key => key, StringComparer.Ordinal))
        {
            problems.Add($"STALE {key} is in {tableName} but no such action exists any more: remove the entry.");
        }

        foreach (var key in expected.Where(entry => entry.Value == Unprotected).Select(entry => entry.Key).OrderBy(key => key, StringComparer.Ordinal))
        {
            problems.Add($"UNPROTECTED {key} is expected to carry no auth attribute at all: give it [Authorize] or [AllowAnonymous].");
        }

        return problems;
    }

    /// <summary>
    /// The policies named by the authorization values that the API does not define. A policy named in the table or on an action must be a
    /// permission key the API defines, unless it is one of the exempt policies (a policy that is not a permission).
    /// </summary>
    /// <param name="apiName">The API's name, which starts each message. Required.</param>
    /// <param name="authValues">The authorization values to check: the table's values and the discovered ones. Required.</param>
    /// <param name="definedPolicies">The policy names the API defines. Required.</param>
    /// <param name="exemptPolicies">Policies that need not be defined keys. Default: none.</param>
    public static IReadOnlyList<string> UndefinedPolicyProblems(
        string apiName,
        IEnumerable<string> authValues,
        IEnumerable<string> definedPolicies,
        IEnumerable<string>? exemptPolicies = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiName);
        ArgumentNullException.ThrowIfNull(authValues);
        ArgumentNullException.ThrowIfNull(definedPolicies);

        var defined = definedPolicies.ToHashSet(StringComparer.Ordinal);
        var exempt = new HashSet<string>(StringComparer.Ordinal);
        if (exemptPolicies is not null)
        {
            exempt.UnionWith(exemptPolicies);
        }

        List<string> problems = [];
        foreach (var auth in authValues)
        {
            foreach (var policy in PolicyNames(auth))
            {
                if (!defined.Contains(policy) && !exempt.Contains(policy))
                {
                    problems.Add($"{apiName} names an undefined policy '{policy}'.");
                }
            }
        }

        return problems.Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// The duplicate problems: two actions that share a table key (an overload), and two actions that answer the same verb and route.
    /// </summary>
    /// <param name="actual">The discovered actions. Required.</param>
    public static IReadOnlyList<string> DuplicateProblems(IEnumerable<DiscoveredEndpoint> actual)
    {
        ArgumentNullException.ThrowIfNull(actual);

        var discovered = actual.ToList();
        var duplicateKeys = discovered.GroupBy(endpoint => endpoint.Key, StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => group.Key).ToList();
        var duplicateRoutes = discovered.GroupBy(endpoint => endpoint.Route, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1).Select(group => group.Key).ToList();

        List<string> problems = [];
        if (duplicateKeys.Count > 0)
        {
            problems.Add("Overloaded actions share a table key: " + string.Join(", ", duplicateKeys) + ".");
        }

        if (duplicateRoutes.Count > 0)
        {
            problems.Add("Two actions answer the same verb and route: " + string.Join(", ", duplicateRoutes) + ".");
        }

        return problems;
    }

    /// <summary>
    /// The inventory problem for one authorization: the routes that resolve to <paramref name="auth"/> are exactly the documented routes. A
    /// route in code but not documented, or documented but not in code, is one line. Routes compare without regard to case.
    /// </summary>
    /// <param name="actual">The discovered actions. Required.</param>
    /// <param name="auth">The authorization value to inventory, such as <see cref="Anonymous"/>. Required.</param>
    /// <param name="documentedRoutes">The documented routes, as <c>VERB /route</c>. Required.</param>
    /// <param name="section">The documented section's name, which the message names. Required.</param>
    public static IReadOnlyList<string> RouteInventoryProblems(
        IEnumerable<DiscoveredEndpoint> actual,
        string auth,
        IEnumerable<string> documentedRoutes,
        string section)
    {
        ArgumentNullException.ThrowIfNull(actual);
        ArgumentException.ThrowIfNullOrWhiteSpace(auth);
        ArgumentNullException.ThrowIfNull(documentedRoutes);
        ArgumentException.ThrowIfNullOrWhiteSpace(section);

        var inCode = actual.Where(endpoint => endpoint.Auth == auth)
            .Select(endpoint => endpoint.Route.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        var documented = documentedRoutes.Select(route => route.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        var undocumented = inCode.Except(documented, StringComparer.Ordinal).ToList();
        var missing = documented.Except(inCode, StringComparer.Ordinal).ToList();
        if (undocumented.Count == 0 && missing.Count == 0)
        {
            return [];
        }

        return
        [
            $"The {auth} surface differs from the documented '{section}'. "
            + "In code but not documented: [" + string.Join(", ", undocumented) + "]. "
            + "Documented but not in code: [" + string.Join(", ", missing) + "]. "
            + "A new endpoint with this authorization is a standing decision: document it and extend the list.",
        ];
    }

    /// <summary>The actions that carry the app-session marker without being anonymous: the framework would refuse the app before the gate ran.</summary>
    /// <param name="actual">The discovered actions. Required.</param>
    public static IReadOnlyList<string> AppSessionBlockedProblems(IEnumerable<DiscoveredEndpoint> actual)
    {
        ArgumentNullException.ThrowIfNull(actual);

        return actual
            .Where(endpoint => endpoint.Auth == AppOrUserBlockedByAuthorize)
            .Select(endpoint => $"{endpoint.Key} carries the app-session marker but is not anonymous, so the authorize attribute refuses the app before the gate runs.")
            .ToList();
    }

    /// <summary>
    /// The minimal-route problems of a startup source. Routes mapped there bypass the authorization matrix, so none may be mapped. The
    /// health call is made exactly once, and no anonymous call is made on the app.
    /// </summary>
    /// <param name="startupSource">The text of the startup code. Required.</param>
    /// <param name="options">The settings. Default: <see cref="EndpointAuthOptions"/> with its defaults.</param>
    public static IReadOnlyList<string> MinimalRouteProblems(string startupSource, EndpointAuthOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(startupSource);
        var settings = options ?? new EndpointAuthOptions();

        var source = SourceScan.StripCSharpComments(startupSource);
        var mapped = Regex.Matches(
                source,
                @"\b" + Regex.Escape(settings.AppVariable) + @"\.Map(?:Get|Post|Put|Patch|Delete|Methods|HealthChecks|Group|BuildInfo|Liveness|Readiness)\(")
            .Select(match => match.Value)
            .ToList();

        List<string> problems = [];
        if (mapped.Count > 0)
        {
            problems.Add("Routes are mapped in the startup code, outside the authorization matrix: " + string.Join(", ", mapped) + ". Map them on a controller instead.");
        }

        var healthCalls = Regex.Count(source, Regex.Escape(settings.HealthCall));
        if (healthCalls != 1)
        {
            problems.Add($"{settings.HealthCall} must be called exactly once; the startup code calls it {healthCalls} time(s).");
        }

        if (Regex.Count(source, @"\.AllowAnonymous\(\)") != 0)
        {
            problems.Add("The startup code calls .AllowAnonymous(), which opens a route outside the matrix.");
        }

        return problems;
    }

    private static IEnumerable<string> PolicyNames(string auth) =>
        auth.Split('+', StringSplitOptions.RemoveEmptyEntries)
            .Where(token => token.StartsWith("Policy:", StringComparison.Ordinal))
            .Select(token => token["Policy:".Length..]);

    // Rewrites [controller] to the lower-case controller name, and drops route constraints such as {id:int}, so the route reads as the
    // reference does.
    private static string NormaliseRoute(string route, Type controller)
    {
        var name = controller.Name.EndsWith("Controller", StringComparison.Ordinal)
            ? controller.Name[..^"Controller".Length]
            : controller.Name;
        route = route.Replace("[controller]", name.ToLowerInvariant(), StringComparison.Ordinal);
        route = Regex.Replace(route, @"\{(\w+):[^}]+\}", "{$1}");
        while (route.Contains("//", StringComparison.Ordinal))
        {
            route = route.Replace("//", "/", StringComparison.Ordinal);
        }

        return route.TrimEnd('/');
    }
}
