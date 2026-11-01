// Copyright (c) 2026 Unseen Horizons
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0. If a copy of the MPL was not
// distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.

namespace Ballast.SqlServer.Tests;

/// <summary>The production connection-string check: which strings are developer databases, and the refusal outside Development.</summary>
public class ConnectionStringCheckTests
{
    [Theory]
    [InlineData("Server=localhost;Database=GetThereDB;Trusted_Connection=True;TrustServerCertificate=True", ConnectionStringProblem.LocalHost)]
    [InlineData("Data Source=LOCALHOST,1433;Initial Catalog=X;User ID=u;Password=p", ConnectionStringProblem.LocalHost)]
    [InlineData("Server=127.0.0.1,1433;Database=x;User Id=u;Password=p", ConnectionStringProblem.LocalHost)]
    [InlineData("Server=127.4.5.6;Database=x;User Id=u;Password=p", ConnectionStringProblem.LocalHost)]
    [InlineData("Server=[::1],1433;Database=x;User Id=u;Password=p", ConnectionStringProblem.LocalHost)]
    [InlineData("Server=.;Database=x;User Id=u;Password=p", ConnectionStringProblem.LocalHost)]
    [InlineData("Server=(local);Database=x;Trusted_Connection=True", ConnectionStringProblem.LocalHost)]
    [InlineData("Server=mysite.localhost;Database=x;User Id=u;Password=p", ConnectionStringProblem.LocalHost)]
    [InlineData("Server=lpc:.;Database=x;User Id=u;Password=p", ConnectionStringProblem.LocalHost)]
    [InlineData("Server=np:\\\\localhost\\pipe\\sql\\query;Database=x;User Id=u;Password=p", ConnectionStringProblem.LocalHost)]
    [InlineData("Server=(localdb)\\MSSQLLocalDB;Database=x;Integrated Security=True", ConnectionStringProblem.LocalDb)]
    [InlineData("Server=tcp:transit.database.windows.net,1433;Database=GetThereDB;User ID=u;Password=p;Encrypt=True", ConnectionStringProblem.None)]
    [InlineData("Server=np:\\\\prodhost\\pipe\\sql\\query;Database=x;User ID=u;Password=p", ConnectionStringProblem.None)]
    [InlineData("Server=localhost.example.test;Database=x;User ID=u;Password=p", ConnectionStringProblem.None)]
    [InlineData("Server=prod.example.test;Database=localhost;User ID=u;Password=p", ConnectionStringProblem.None)]
    [InlineData("Server=prod.example.test;Database=x;Trusted_Connection=False;User ID=u;Password=p", ConnectionStringProblem.None)]
    [InlineData("Server=prod.example.test;Database=x;Trusted_Connection=True", ConnectionStringProblem.IntegratedSecurity)]
    [InlineData("Server=prod.example.test;Database=x;Integrated Security=SSPI", ConnectionStringProblem.IntegratedSecurity)]
    [InlineData("Database=x;User ID=u;Password=p", ConnectionStringProblem.Unreadable)]
    [InlineData("", ConnectionStringProblem.Unreadable)]
    [InlineData("Server=;Database=x", ConnectionStringProblem.Unreadable)]
    [InlineData("Server=[::1;Database=x", ConnectionStringProblem.Unreadable)]
    [InlineData("this is not a connection string", ConnectionStringProblem.Unreadable)]
    [InlineData("Server=prod.example.test;Connect Timeout=abc", ConnectionStringProblem.Unreadable)]
    public void Classify_names_the_problem_with_the_string(string connectionString, ConnectionStringProblem expected)
    {
        Assert.Equal(expected, ConnectionStringCheck.Classify(connectionString));
    }

    [Fact]
    public void A_developer_string_is_refused_outside_Development_and_the_message_names_the_setting_and_not_the_secret()
    {
        const string connectionString = "Server=localhost;Database=x;User ID=u;Password=hunter2-secret";

        var thrown = Assert.Throws<InvalidOperationException>(() =>
            ConnectionStringCheck.EnsureNotDeveloperDatabase(connectionString, isDevelopment: false, "ConnectionStrings:DefaultConnection"));

        Assert.Contains("ConnectionStrings:DefaultConnection", thrown.Message, StringComparison.Ordinal);
        Assert.Contains("this computer", thrown.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_developer_string_is_accepted_in_Development()
    {
        ConnectionStringCheck.EnsureNotDeveloperDatabase(
            "Server=localhost;Database=x;Trusted_Connection=True", isDevelopment: true, "ConnectionStrings:DefaultConnection");
    }

    [Fact]
    public void A_string_for_another_computer_is_accepted_outside_Development()
    {
        ConnectionStringCheck.EnsureNotDeveloperDatabase(
            "Server=tcp:transit.database.windows.net,1433;Database=GetThereDB;User ID=u;Password=p", isDevelopment: false, "ConnectionStrings:DefaultConnection");
    }

    [Fact]
    public void Windows_authentication_is_refused_outside_Development_with_its_own_reason()
    {
        var thrown = Assert.Throws<InvalidOperationException>(() =>
            ConnectionStringCheck.EnsureNotDeveloperDatabase("Server=prod.example.test;Trusted_Connection=True", isDevelopment: false, "Setting"));

        Assert.Contains("Windows authentication", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unreadable_string_is_refused_outside_Development_and_never_guessed_at()
    {
        var thrown = Assert.Throws<InvalidOperationException>(() =>
            ConnectionStringCheck.EnsureNotDeveloperDatabase("", isDevelopment: false, "Setting"));

        Assert.Contains("not a readable", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_check_refuses_null_and_an_empty_setting_name()
    {
        Assert.Throws<ArgumentNullException>(() => ConnectionStringCheck.Classify(null!));
        Assert.Throws<ArgumentNullException>(() => ConnectionStringCheck.EnsureNotDeveloperDatabase(null!, false, "Setting"));
        Assert.Throws<ArgumentException>(() => ConnectionStringCheck.EnsureNotDeveloperDatabase("Server=prod", false, " "));
    }
}
