// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

using Ballast.Maui.Storage;

namespace Ballast.Maui.Tests;

/// <summary>The install's own answers: kept in their named container, and an unreadable or unwritable container is logged and never thrown.</summary>
public sealed class InstallAnswersTests
{
    [Fact]
    public void A_stored_answer_reads_back_from_its_named_container_only()
    {
        var preferences = new FakePreferenceStore();
        var answers = new InstallAnswers(preferences, "app.install", new RecordingLog());

        answers.Set("permissions.locationRequested", true);

        Assert.True(answers.Get("permissions.locationRequested", false, false));
        Assert.Equal<object?>(true, preferences.Peek("permissions.locationRequested", "app.install"));
        Assert.Null(preferences.Peek("permissions.locationRequested"));
    }

    [Fact]
    public void A_missing_answer_is_the_missing_value()
    {
        var answers = new InstallAnswers(new FakePreferenceStore(), "app.install", new RecordingLog());

        Assert.Equal("missing", answers.Get("absent", "missing", "unreadable"));
    }

    [Fact]
    public void An_unreadable_container_answers_the_unreadable_value_and_is_logged()
    {
        var log = new RecordingLog();
        var answers = new InstallAnswers(new FakePreferenceStore { Fault = new InvalidOperationException("corrupt") }, "app.install", log);

        Assert.Equal("unreadable", answers.Get("key", "missing", "unreadable"));
        Assert.Single(log.Warnings);
    }

    [Fact]
    public void A_write_that_fails_is_logged_and_not_thrown()
    {
        var log = new RecordingLog();
        var answers = new InstallAnswers(new FakePreferenceStore { Fault = new InvalidOperationException("read-only") }, "app.install", log);

        answers.Set("key", 1);
        answers.Remove("key");

        Assert.Equal(2, log.Warnings.Count);
    }

    [Fact]
    public void Remove_takes_the_answer_away()
    {
        var answers = new InstallAnswers(new FakePreferenceStore(), "app.install", new RecordingLog());
        answers.Set("key", 7);

        answers.Remove("key");

        Assert.Equal(0, answers.Get("key", 0, -1));
    }

    [Fact]
    public void The_container_name_is_kept()
    {
        Assert.Equal("app.install", new InstallAnswers(new FakePreferenceStore(), "app.install", new RecordingLog()).SharedName);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void A_blank_container_name_is_refused(string name)
    {
        Assert.Throws<ArgumentException>(() => new InstallAnswers(new FakePreferenceStore(), name, new RecordingLog()));
    }
}
