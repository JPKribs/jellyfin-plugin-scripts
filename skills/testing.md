# Testing

How to test this plugin, from a single method to a script running on a real server. A change is done when the tests for every layer it touches pass.

## The Rule

Every source file with logic has a test file, and every test proves that given inputs produce the expected output. A bug fix starts with a test that fails without the fix.

## Layers

Test each behavior at the lowest layer that can prove it. Go up a layer only for what the lower one cannot reach.

| Layer | Proves | Runs | Time |
| :--- | :--- | :--- | :--- |
| Unit | One class or function gives the right output for each input | `dotnet test` | Seconds |
| Runner | A script runs its steps through `ScriptRunner` and `OperationExecutor`, with the history and secret vault in a temporary folder | `dotnet test` | Seconds |
| Resources | Every dashboard file is embedded, so no page loads blank | `dotnet test` | Seconds |
| Page scripts | Each page module parses, and every Base name it imports exists | `node --check` and the typings | Seconds |
| Test server | A page looks right and a trigger runs its script on a real Jellyfin 12.4 | By hand | Minutes |

| A change to | Needs |
| :--- | :--- |
| A model, a condition, placeholders, the trigger catalog, or validation | Unit |
| A step, the runner, cooldowns, for each, secrets, or the history | Runner |
| A new or renamed dashboard file | Resources |
| A config page script | Page scripts, then the test server |
| An event subscription or a scheduled task | Unit where it can reach, then the test server |

## Layout

The tests sit in the root of `Jellyfin.Plugin.Scripts.Tests/`, one file per subject, named after it.

| Source | Test |
| :--- | :--- |
| `Jellyfin.Plugin.Scripts/Services/ConditionEvaluator.cs` | `Jellyfin.Plugin.Scripts.Tests/ConditionEvaluatorTests.cs` |
| `Jellyfin.Plugin.Scripts/Services/ScriptSecrets.cs` | `Jellyfin.Plugin.Scripts.Tests/ScriptSecretsTests.cs` |
| `Jellyfin.Plugin.Scripts/Plugin.cs` pages | `Jellyfin.Plugin.Scripts.Tests/EmbeddedResourceTests.cs` |

* `TestServices.cs` wires a runner, its step executor, the history, and the secret vault to substitutes for the server, in a temporary folder it deletes on `Dispose`.

## C# Tests

Tests use xUnit with plain `Assert`, and NSubstitute for the Jellyfin interfaces a class takes.

```csharp
/// <summary>
/// A step whose condition names no variable always runs.
/// True: a step with an empty condition runs as it always has.
/// False: every step without a condition would be skipped.
/// </summary>
[Fact]
public void MatchesStep_NoVariable_Runs()
{
    var op = new ScriptOperation { Type = OperationType.Wait };

    var result = ConditionEvaluator.MatchesStep(op, new Dictionary<string, string>());

    Assert.True(result);
}
```

```bash
dotnet test
```

### Naming and Comments

* Name the class `<Subject>Tests` and each method `Method_Scenario_Expectation`.
* Give the class a one sentence summary of what its subject must guarantee.
* Give each new test the three line summary. What it proves, what passing means, and what failing would cause.

### Writing a Test

* Arrange, act, and assert, separated by blank lines.
* Assert one behavior per test. Several asserts are fine when they check one outcome.
* Use `[Theory]` with `[InlineData]` for one rule over several inputs.
* Test the edges. Null, empty, the largest value, a cancelled token, a command that fails or times out, and a secret that must stay masked.
* Never sleep. Pass a clock or a delay in, and control it from the test. A test that must wait on a background run polls with a deadline, as `ScriptDispatcherTests` does.
* A test that fails once in many runs has found a race. Fix the source or the test, and never run it again until it passes.
* A test never reaches the network. Substitute `IHttpClientFactory`, or give it a stub `HttpMessageHandler`.
* A test that runs a real command uses `/bin/sh` and skips on Windows.

### Substitutes

* Build the runner and its services through `TestServices`, and extend it rather than wiring a second copy in a test file.
* Substitute a Jellyfin interface with `Substitute.For<T>()`, and assert on what it received.
* Keep the state files in the temporary folder `TestServices` makes. Never write to a real Jellyfin folder.

## Page Scripts

There are no JavaScript unit tests. Check each page module before you call a change done.

```bash
for f in Jellyfin.Plugin.Scripts/Configuration/*.js; do node --check "$f"; done
```

Then check that every name `scripts_common.js` imports from Base is in the typings of the version the csproj names.

```bash
grep -E "export (function|var|const) createTimeline\b" ~/.nuget/packages/jpkribs.jellyfin.base/<version>/assets/jpkribs_shared.d.ts
```

* Only `scripts_common.js` imports Base, so its import list is the one to check.
* A function that needs elements can be tried against Base's `fake-dom.mjs` from `../Shared/jellyfin-plugin-base/JPKribs.Jellyfin.Base.Tests/js/`. Keep such a check outside the repository.

## Test Server

Install a build on a Jellyfin 12.4 test server to look at a page or raise a real event. See [documentation.md](documentation.md#installing-a-build-by-hand).

* Reload the dashboard after installing a build, since it caches page scripts.
* Use **Test** for a step's output with sample values, and **Run now** for the saved script. The **History** tab shows each run's steps, output, and variables.
* Raise the trigger itself for anything a sample cannot show, such as playback starting or an item being added.
* Use a test server, never one people watch on. A step can stop playback, sign users out, or change items.

## Checklist

* [ ] Every changed source file with logic has tests.
* [ ] A bug fix has a test that fails without it.
* [ ] Edges are covered, including null, empty, cancelled, a failing command, and a masked secret.
* [ ] Substitutes come from `TestServices` or NSubstitute.
* [ ] `dotnet test` passes.
* [ ] Every page script passes `node --check`, and its Base imports exist.
* [ ] Every changed page and trigger was tried on a test server.
