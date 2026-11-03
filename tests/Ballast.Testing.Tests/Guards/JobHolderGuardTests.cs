// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Reflection;

using Ballast.Testing.Guards;
using Ballast.Testing.Guards.JobHolder;

namespace Ballast.Testing.Tests.Guards;

/// <summary>The job-holder rules, run against this test assembly's fixtures: classification, registration, the lock, the holder actions, publish and tmp names.</summary>
public class JobHolderGuardTests
{
    private static readonly Assembly Own = typeof(JobHolderGuardTests).Assembly;

    private const string GoodStartup =
        "services.AddJobLeadership();\n"
        + "services.AddLeaderHostedService<FakeHolderWorker>();\n"
        + "services.AddHostedService<FakeEveryCopyWorker>();\n"
        + "services.AddHostedService<FakeMixedWorker>();\n"
        + "host.ConfigureGracefulShutdown(options);\n";

    private static JobHolderOptions Good() => new()
    {
        HostedServiceAssemblies = [Own],
        Classification = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["FakeHolderWorker"] = JobClass.HolderOnly,
            ["FakeEveryCopyWorker"] = JobClass.EveryCopy,
            ["FakeMixedWorker"] = JobClass.Mixed,
        },
        RegistrationSources = new Dictionary<Assembly, IReadOnlyList<string>> { [Own] = [GoodStartup] },
        MixedCarriers = new Dictionary<string, Type>(StringComparer.Ordinal) { ["FakeMixedWorker"] = typeof(FakeLeadershipCarrier) },
        LeadershipType = typeof(IFakeLeadership),
        HolderAttribute = typeof(FakeHolderAttribute),
        ControllerAssemblies = [Own],
        HolderActions = ["FakeAllHolderController.*", "FakeJobsController.PublishAnyway", "FakeJobsController.Start"],
        PublishActions = ["FakeJobsController.PublishAnyway"],
    };

    [Fact]
    public void A_complete_classification_has_no_problems()
    {
        Assert.Empty(JobHolderGuard.ClassificationProblems(Good()));
    }

    [Fact]
    public void A_hosted_service_with_no_class_is_unclassified()
    {
        var options = Good() with { Classification = Without(Good().Classification, "FakeMixedWorker") };

        var problem = Assert.Single(JobHolderGuard.ClassificationProblems(options));

        Assert.Equal("UNCLASSIFIED FakeMixedWorker: add it to the classification as holder only, every copy or mixed, and register it to match.", problem);
    }

    [Fact]
    public void A_classification_entry_that_names_no_service_is_stale()
    {
        var options = Good() with { Classification = With(Good().Classification, "GoneWorker", JobClass.HolderOnly) };

        var problem = Assert.Single(JobHolderGuard.ClassificationProblems(options));

        Assert.Equal("STALE GoneWorker: classified, but no hosted service has that name. Remove the entry.", problem);
    }

    [Fact]
    public void A_class_that_is_not_one_of_the_three_is_reported()
    {
        var options = Good() with { Classification = With(Good().Classification, "FakeHolderWorker", "sometimes") };

        var problem = Assert.Single(JobHolderGuard.ClassificationProblems(options));

        Assert.Equal("FakeHolderWorker: 'sometimes' is not one of the job classes: holder only, every copy or mixed.", problem);
    }

    [Fact]
    public void A_complete_registration_has_no_problems()
    {
        Assert.Empty(JobHolderGuard.RegistrationProblems(Good()));
    }

    [Fact]
    public void A_holder_only_worker_registered_as_every_copy_is_reported()
    {
        var options = Good() with
        {
            RegistrationSources = new Dictionary<Assembly, IReadOnlyList<string>>
            {
                [Own] = [GoodStartup.Replace("AddLeaderHostedService<FakeHolderWorker>", "AddHostedService<FakeHolderWorker>", StringComparison.Ordinal)],
            },
        };

        var problem = Assert.Single(JobHolderGuard.RegistrationProblems(options));

        Assert.Equal("FakeHolderWorker is holder only but is registered with AddHostedService.", problem);
    }

    [Fact]
    public void A_worker_that_runs_in_every_copy_registered_with_the_leader_method_is_reported()
    {
        var options = Good() with
        {
            RegistrationSources = new Dictionary<Assembly, IReadOnlyList<string>>
            {
                [Own] = [GoodStartup.Replace("AddHostedService<FakeEveryCopyWorker>", "AddLeaderHostedService<FakeEveryCopyWorker>", StringComparison.Ordinal)],
            },
        };

        var problem = Assert.Single(JobHolderGuard.RegistrationProblems(options));

        Assert.Equal("FakeEveryCopyWorker runs in every copy but is registered with AddLeaderHostedService.", problem);
    }

    [Fact]
    public void A_service_registered_twice_in_one_source_is_reported()
    {
        var options = Good() with
        {
            RegistrationSources = new Dictionary<Assembly, IReadOnlyList<string>>
            {
                [Own] = [GoodStartup + "services.AddLeaderHostedService<FakeHolderWorker>();\n"],
            },
        };

        var problem = Assert.Single(JobHolderGuard.RegistrationProblems(options));

        Assert.Equal("FakeHolderWorker is registered 2 times, expected once in each API that runs it.", problem);
    }

    [Fact]
    public void A_service_with_no_registration_is_reported()
    {
        var options = Good() with
        {
            RegistrationSources = new Dictionary<Assembly, IReadOnlyList<string>> { [Own] = ["services.AddJobLeadership();"] },
        };

        Assert.Equal(3, JobHolderGuard.RegistrationProblems(options).Count);
    }

    [Fact]
    public void A_required_registration_text_that_is_missing_is_reported()
    {
        var options = Good() with { RequiredRegistrationText = new Dictionary<string, string>(StringComparer.Ordinal) { ["FakeHolderWorker"] = "once: true" } };

        var problem = Assert.Single(JobHolderGuard.RegistrationProblems(options));

        Assert.Equal("FakeHolderWorker: its registration must contain 'once: true'.", problem);
    }

    [Fact]
    public void A_registration_source_missing_for_an_assembly_is_reported_rather_than_skipped()
    {
        var options = Good() with { RegistrationSources = new Dictionary<Assembly, IReadOnlyList<string>>() };

        var problems = JobHolderGuard.RegistrationProblems(options);

        Assert.Equal(3, problems.Count);
        Assert.All(problems, problem => Assert.Contains("no registration source is named for its assembly", problem, StringComparison.Ordinal));
    }

    [Fact]
    public void A_mixed_worker_with_a_carrier_that_takes_the_leadership_has_no_problems()
    {
        Assert.Empty(JobHolderGuard.MixedLeadershipProblems(Good()));
    }

    [Fact]
    public void A_mixed_worker_with_no_carrier_is_reported()
    {
        var options = Good() with { MixedCarriers = new Dictionary<string, Type>(StringComparer.Ordinal) };

        var problem = Assert.Single(JobHolderGuard.MixedLeadershipProblems(options));

        Assert.StartsWith("FakeMixedWorker is split between the holder and every copy, but names no carrier.", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_carrier_that_does_not_take_the_leadership_is_reported()
    {
        var options = Good() with { MixedCarriers = new Dictionary<string, Type>(StringComparer.Ordinal) { ["FakeMixedWorker"] = typeof(FakeNoLeadershipCarrier) } };

        var problem = Assert.Single(JobHolderGuard.MixedLeadershipProblems(options));

        Assert.Equal(
            "FakeNoLeadershipCarrier is the carrier of FakeMixedWorker, but no constructor takes IFakeLeadership, so it cannot tell the holder from the copies.",
            problem);
    }

    [Fact]
    public void A_mixed_service_with_no_leadership_type_is_refused_rather_than_guessed()
    {
        var options = Good() with { LeadershipType = null };

        Assert.Throws<ArgumentException>(() => JobHolderGuard.MixedLeadershipProblems(options));
    }

    [Fact]
    public void A_startup_that_registers_the_lock_first_and_shuts_down_in_order_has_no_problems()
    {
        Assert.Empty(JobHolderGuard.StartupProblems("Fake", GoodStartup, Good()));
    }

    [Fact]
    public void A_hosted_service_registered_before_the_job_lock_is_reported()
    {
        const string startup = "services.AddLeaderHostedService<FakeHolderWorker>();\nservices.AddJobLeadership();\nhost.ConfigureGracefulShutdown(o);";

        var problem = Assert.Single(JobHolderGuard.StartupProblems("Fake", startup, Good()));

        Assert.Equal("Fake registers a hosted service before the job lock.", problem);
    }

    [Fact]
    public void A_startup_with_no_job_lock_is_reported_and_nothing_else_is_read()
    {
        var problem = Assert.Single(JobHolderGuard.StartupProblems("Fake", "services.AddLeaderHostedService<FakeHolderWorker>();", Good()));

        Assert.Equal("Fake does not register the job lock: no 'AddJobLeadership(' call was found.", problem);
    }

    [Fact]
    public void A_startup_without_the_graceful_shutdown_call_is_reported()
    {
        var problem = Assert.Single(JobHolderGuard.StartupProblems("Fake", "services.AddJobLeadership();\nservices.AddHostedService<X>();", Good()));

        Assert.StartsWith("Fake does not call ConfigureGracefulShutdown(", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_call_that_must_follow_the_lock_passes_when_it_does_and_is_reported_when_it_precedes_or_is_absent()
    {
        var after = JobHolderGuard.StartupProblems("Fake", GoodStartup + "services.AddAppSession();\n", Good(), ["AddAppSession("]);
        var before = JobHolderGuard.StartupProblems("Fake", "services.AddAppSession();\n" + GoodStartup, Good(), ["AddAppSession("]);
        var absent = JobHolderGuard.StartupProblems("Fake", GoodStartup, Good(), ["AddAppSession("]);

        Assert.Empty(after);
        Assert.Equal("Fake calls AddAppSession( before the job lock.", Assert.Single(before));
        Assert.Equal("Fake does not call AddAppSession(, which must follow the job lock.", Assert.Single(absent));
    }

    [Fact]
    public void The_holder_actions_listed_are_exactly_the_ones_that_carry_the_attribute()
    {
        Assert.Empty(JobHolderGuard.HolderActionProblems(Good()));
    }

    [Fact]
    public void A_holder_action_that_is_not_listed_is_reported()
    {
        var options = Good() with { HolderActions = ["FakeAllHolderController.*", "FakeJobsController.PublishAnyway"] };

        var problem = Assert.Single(JobHolderGuard.HolderActionProblems(options));

        Assert.Equal("FakeJobsController.Start carries the job-holder attribute but is not in the holder action list. Add it there, so the list stays reviewed.", problem);
    }

    [Fact]
    public void A_listed_action_that_carries_no_attribute_is_reported()
    {
        var options = Good() with { HolderActions = [.. Good().HolderActions, "FakeJobsController.Status"] };

        var problem = Assert.Single(JobHolderGuard.HolderActionProblems(options));

        Assert.Equal(
            "FakeJobsController.Status is in the holder action list, but no action of that name carries the job-holder attribute. Remove it, or add the attribute.",
            problem);
    }

    [Fact]
    public void A_publish_anyway_action_with_the_attribute_and_the_expected_name_has_no_problems()
    {
        Assert.Empty(JobHolderGuard.PublishActionProblems(Good()));
    }

    [Fact]
    public void A_publish_anyway_action_without_the_holder_attribute_is_reported()
    {
        var options = Good() with
        {
            PublishRouteFragment = "probe-publish",
            PublishActions = ["FakeUnheldController.Probe"],
        };

        var problem = Assert.Single(JobHolderGuard.PublishActionProblems(options));

        Assert.Equal(
            "FakeUnheldController.Probe is a publish-anyway action and must carry the job-holder attribute: publishing runs the housekeeping, which writes.",
            problem);
    }

    [Fact]
    public void A_publish_action_not_in_the_expected_list_is_reported()
    {
        var options = Good() with { PublishActions = [] };

        var problem = Assert.Single(JobHolderGuard.PublishActionProblems(options));

        Assert.Equal("FakeJobsController.PublishAnyway is a publish-anyway action but is not in the expected list. Add it there.", problem);
    }

    [Fact]
    public void A_file_that_writes_a_fixed_tmp_name_must_be_listed()
    {
        var sources = new[] { ("ApiOne/Writer.cs", "File.WriteAllText(\"state.tmp\", text);") };

        var problem = Assert.Single(JobHolderGuard.FixedTmpWriterProblems(sources, []));

        Assert.StartsWith("ApiOne/Writer.cs writes a fixed .tmp name", problem, StringComparison.Ordinal);
        Assert.Empty(JobHolderGuard.FixedTmpWriterProblems(sources, ["ApiOne/Writer.cs"]));
    }

    [Fact]
    public void A_listed_tmp_writer_that_no_longer_writes_one_is_reported_and_a_comment_is_not_a_write()
    {
        var sources = new[] { ("ApiOne/Quiet.cs", "// writes \"state.tmp\" in the old build\nvar x = 1;") };

        var problem = Assert.Single(JobHolderGuard.FixedTmpWriterProblems(sources, ["ApiOne/Quiet.cs"]));

        Assert.Equal("ApiOne/Quiet.cs is listed as a fixed .tmp writer, but it no longer writes one. Remove it from the list.", problem);
        Assert.Empty(JobHolderGuard.FixedTmpWriterProblems(sources, []));
    }

    [Fact]
    public void The_planted_registration_is_caught_and_a_complete_registration_passes()
    {
        var planted = GoodStartup.Replace("AddLeaderHostedService<FakeHolderWorker>", "AddHostedService<FakeHolderWorker>", StringComparison.Ordinal);

        GuardSelfTest.AssertCatches(source => JobHolderGuard.RegistrationProblems(Registering(source)), planted, "FakeHolderWorker is holder only");
        GuardSelfTest.AssertPasses(source => JobHolderGuard.RegistrationProblems(Registering(source)), GoodStartup);
    }

    [Fact]
    public void A_registration_rule_that_sees_no_registration_fails_its_own_self_test()
    {
        Assert.ThrowsAny<Exception>(() =>
            GuardSelfTest.AssertCatches(source => JobHolderGuard.RegistrationProblems(Registering(source)), "// nothing registered", "FakeHolderWorker is holder only"));
    }

    [Fact]
    public void The_csharp_sources_of_folders_are_read_and_skip_build_output()
    {
        using var repo = new TempRepository();
        repo.Write("ApiOne/Writer.cs", "class W {}");
        repo.Write("ApiOne/obj/Generated.cs", "class G {}");

        var source = Assert.Single(JobHolderGuard.CSharpSources(repo.Root, ["ApiOne"]));

        Assert.Equal("ApiOne/Writer.cs", source.RelativePath);
    }

    private static JobHolderOptions Registering(string source) => Good() with
    {
        RegistrationSources = new Dictionary<Assembly, IReadOnlyList<string>> { [Own] = [source] },
    };

    private static Dictionary<string, string> Without(IReadOnlyDictionary<string, string> map, string key)
    {
        var copy = new Dictionary<string, string>(map, StringComparer.Ordinal);
        copy.Remove(key);
        return copy;
    }

    private static Dictionary<string, string> With(IReadOnlyDictionary<string, string> map, string key, string value)
    {
        var copy = new Dictionary<string, string>(map, StringComparer.Ordinal) { [key] = value };
        return copy;
    }
}
