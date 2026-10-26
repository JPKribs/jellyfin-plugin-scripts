using System;
using System.Collections.Generic;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Scripts.Models;
using Jellyfin.Plugin.Scripts.Services;
using Xunit;

namespace Jellyfin.Plugin.Scripts.Tests;

public class ConditionEvaluatorTests
{
    private static EventContext Playback(BaseItemKind kind = BaseItemKind.Movie)
    {
        var context = new EventContext(ScriptTrigger.PlaybackStart) { ItemKind = kind };
        context.Set("USER_ID", "abc");
        context.Set("USER_NAME", "alice");
        context.AddCandidate(ConditionField.User, "abc");
        context.AddCandidate(ConditionField.User, "alice");
        context.AddCandidate(ConditionField.User, "bob");
        context.Set("ITEM_NAME", "The Matrix");
        context.Set("PLAY_METHOD", "Transcode");
        return context;
    }

    private static ScriptDefinition Script(params ScriptCondition[] conditions)
        => new() { Conditions = new List<ScriptCondition>(conditions) };

    private static ScriptCondition Condition(ConditionField field, ConditionOperator op, params string[] values)
        => new() { Field = field, Operator = op, Values = new List<string>(values) };

    [Fact]
    public void EmptyItemKindsRunForEveryKind()
    {
        Assert.True(ConditionEvaluator.Matches(Script(), Playback(BaseItemKind.Person)));
    }

    [Fact]
    public void ItemKindsFilterToTheChosenKinds()
    {
        var script = Script();
        script.ItemKinds = new List<BaseItemKind> { BaseItemKind.Episode, BaseItemKind.MusicVideo };

        Assert.True(ConditionEvaluator.Matches(script, Playback(BaseItemKind.MusicVideo)));
        Assert.False(ConditionEvaluator.Matches(script, Playback(BaseItemKind.Movie)));
    }

    [Fact]
    public void ItemKindsAreIgnoredForEventsWithoutAnItem()
    {
        var script = Script();
        script.ItemKinds = new List<BaseItemKind> { BaseItemKind.Episode };

        Assert.True(ConditionEvaluator.Matches(script, new EventContext(ScriptTrigger.TaskCompleted)));
    }

    [Fact]
    public void AnItemEventWithoutAKindFailsAFilter()
    {
        var script = Script();
        script.ItemKinds = new List<BaseItemKind> { BaseItemKind.Episode };

        Assert.False(ConditionEvaluator.Matches(script, new EventContext(ScriptTrigger.PlaybackStart)));
    }

    [Theory]
    [InlineData(ConditionOperator.Is, "ALICE", true)]
    [InlineData(ConditionOperator.Is, "bob", true)]
    [InlineData(ConditionOperator.Is, "carol", false)]
    [InlineData(ConditionOperator.IsNot, "carol", true)]
    [InlineData(ConditionOperator.IsNot, "abc", false)]
    public void UserMatchesAnyOfTheSessionsUsersByIdOrName(ConditionOperator op, string value, bool expected)
    {
        Assert.Equal(expected, ConditionEvaluator.Matches(Condition(ConditionField.User, op, value), Playback()));
    }

    [Theory]
    [InlineData(ConditionOperator.Contains, "matrix", true)]
    [InlineData(ConditionOperator.DoesNotContain, "matrix", false)]
    [InlineData(ConditionOperator.StartsWith, "the", true)]
    [InlineData(ConditionOperator.MatchesRegex, "^the\\s+m", true)]
    [InlineData(ConditionOperator.MatchesRegex, "^matrix", false)]
    [InlineData(ConditionOperator.MatchesRegex, "(", false)]
    public void TextOperators(ConditionOperator op, string value, bool expected)
    {
        Assert.Equal(expected, ConditionEvaluator.Matches(Condition(ConditionField.ItemName, op, value), Playback()));
    }

    [Fact]
    public void AFieldTheTriggerLacksNeverMatches()
    {
        var context = new EventContext(ScriptTrigger.TaskCompleted);

        Assert.False(ConditionEvaluator.Matches(Condition(ConditionField.User, ConditionOperator.IsNot, "alice"), context));
    }

    [Fact]
    public void AllNeedsEveryConditionAndAnyNeedsOne()
    {
        var script = Script(
            Condition(ConditionField.PlayMethod, ConditionOperator.Is, "Transcode"),
            Condition(ConditionField.ItemName, ConditionOperator.Is, "Something Else"));

        Assert.False(ConditionEvaluator.Matches(script, Playback()));

        script.Match = ConditionMatch.Any;
        Assert.True(ConditionEvaluator.Matches(script, Playback()));
    }

    [Fact]
    public void EmptyUsersRunForEveryone()
    {
        Assert.True(ConditionEvaluator.Matches(Script(), Playback()));
    }

    [Fact]
    public void UsersFilterMatchesAnyOfTheSessionsUsers()
    {
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        var context = Playback();
        context.UserIds.Add(alice);
        context.UserIds.Add(bob);

        var script = Script();
        script.UserIds = new List<Guid> { bob };
        Assert.True(ConditionEvaluator.Matches(script, context));

        script.UserIds = new List<Guid> { Guid.NewGuid() };
        Assert.False(ConditionEvaluator.Matches(script, context));
    }

    [Fact]
    public void UsersFilterIsIgnoredForEventsWithoutAUser()
    {
        var script = Script();
        script.UserIds = new List<Guid> { Guid.NewGuid() };

        Assert.True(ConditionEvaluator.Matches(script, new EventContext(ScriptTrigger.TaskCompleted)));
        Assert.True(ConditionEvaluator.Matches(script, new EventContext(ScriptTrigger.ItemAdded)));
    }

    [Fact]
    public void AFailedSignInForAnUnknownNameNeverPassesAUsersFilter()
    {
        var script = Script();
        script.UserIds = new List<Guid> { Guid.NewGuid() };

        Assert.False(ConditionEvaluator.Matches(script, new EventContext(ScriptTrigger.AuthenticationFailure)));
    }

    [Theory]
    [InlineData(ConditionOperator.IsNot)]
    [InlineData(ConditionOperator.DoesNotContain)]
    public void NegativeOperatorsDoNotMatchAMissingValue(ConditionOperator op)
    {
        // A playback of a movie has no series name.
        Assert.False(ConditionEvaluator.Matches(Condition(ConditionField.SeriesName, op, "Kids Show"), Playback()));
    }

    [Theory]
    [InlineData(ConditionOperator.AtLeast, "4", true)]
    [InlineData(ConditionOperator.AtLeast, "5", false)]
    [InlineData(ConditionOperator.AtMost, "4", true)]
    [InlineData(ConditionOperator.AtMost, "3.5", false)]
    [InlineData(ConditionOperator.AtLeast, "lots", false)]
    public void NumericOperatorsCompareNumbers(ConditionOperator op, string value, bool expected)
    {
        var context = Playback();
        context.Set("ACTIVE_TRANSCODES", "4");

        Assert.Equal(expected, ConditionEvaluator.Matches(Condition(ConditionField.ActiveTranscodes, op, value), context));
    }

    [Fact]
    public void GenreMatchesAnyOfTheItemsGenres()
    {
        var context = Playback();
        context.Set("GENRES", "Action, Comedy");
        context.AddCandidate(ConditionField.Genre, "Action");
        context.AddCandidate(ConditionField.Genre, "Comedy");

        Assert.True(ConditionEvaluator.Matches(Condition(ConditionField.Genre, ConditionOperator.Is, "comedy"), context));
        Assert.False(ConditionEvaluator.Matches(Condition(ConditionField.Genre, ConditionOperator.IsNot, "comedy"), context));
    }

    [Fact]
    public void AConditionWithoutValuesNeverMatches()
    {
        Assert.False(ConditionEvaluator.Matches(Condition(ConditionField.ItemName, ConditionOperator.IsNot), Playback()));
    }
}
