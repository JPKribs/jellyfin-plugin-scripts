# Code

How to write, comment, and test code in this plugin. It covers C#, JavaScript, HTML, CSS, shell, and YAML.

## Base First

[JPKribs.Jellyfin.Base](https://github.com/JPKribs/jellyfin-plugin-base) holds everything two plugins could share. Its README lists every component and helper. Read it before you write anything new.

### Use What Base Has

* Use the Base type or component when one fits. Never write a local version, a wrapper that renames it, or a CSS override of a `base-` class.
* When Base almost fits, change Base. Never patch around it locally.
* Delete a local copy as soon as Base offers the same thing. Do it in the commit that moves to that Base version.
* Never shadow a Base name. A local `FormatUtilities` beside Base's `FormatUtilities` is ambiguous.
* Read Base from the package this plugin builds against. The C# source is in `~/.nuget/packages/jpkribs.jellyfin.base/<version>/src/`, and the JS typings are in `assets/jpkribs_shared.d.ts` beside it.
* Treat the local Base checkout as read only. Base has its own owner and its own release, so report a gap and wait for the version that closes it.
* When Base differs from the local code it replaces, keep the local behavior in a small step around the Base call, and cover it with a test. The HTTP request step turns a 5xx, a 429, or no answer from `JpkHttp` into the exception `RetryPolicy` retries.

| Need | Use from Base |
| :--- | :--- |
| Plugin class and config lock | `PluginBase<TPlugin, TConfiguration>` |
| Scheduled task | `PluginScheduledTask`, `TaskTriggers` |
| Outbound HTTP | `JpkHttp` and `HttpResult` |
| Retry and backoff | `RetryPolicy`, `BackoffPolicy`, `HttpRetry` |
| Addresses a user supplied, when the plugin must refuse private ones | `SsrfGuard` |
| Secrets | `SecretProtector`, `StableSecretProtection`, `AddPluginSecretProtector` |
| Comparing a presented secret | `SecretCompare.FixedTimeEquals` |
| Writing a file whole or not at all | `AtomicFile`, with `ownerOnly: true` for a key or secret |
| JSON state file | `JsonFileStore<T>` |
| Paths inside a folder | `PathContainment` |
| The clock | `TimeProvider`, and `UtcTime` for stored times |
| Activity log | `ActivityLogger`, `ActivitySummary`, `ActivityText` |
| Formatting and names | `FormatUtilities`, `ItemTitles`, `FileNameSanitizer` |
| Config page lifecycle | `createConfigPage`, through `createScriptsPage` in `scripts_common.js` |
| Page helpers | `createShared` |
| Tables | `createDataTable` |
| Lists and row editors | `createCheckboxList`, `createChipSelect`, `createUserMultiSelector`, `createSortableCardList`, `createRepeater`, `createRecordList` |
| Numbers and times | `createSliderField`, `createDurationField` |
| Saving and messages | `createSaveBar`, `setFieldState`, `createToaster` |
| Read only views | `createTimeline`, `createDefinitionList`, `createCopyField` |
| Dialogs | `openDialog`, `confirmDialog`, `promptDialog`, `openEditorDialog`, `bindDialog` |

### Move Code Into Base

Code belongs in Base when another plugin could use it unchanged. A helper with no Scripts types in its signature is a candidate.

1. Add it to `../Shared/jellyfin-plugin-base/src/`, one file per component, following Base's [code guide](https://github.com/JPKribs/jellyfin-plugin-base/blob/main/skills/code.md).
2. Add tests in `JPKribs.Jellyfin.Base.Tests/`, and JS tests in `JPKribs.Jellyfin.Base.Tests/js/`.
3. Document it in Base's docs site inventory.
4. Pack it locally with `scripts/pack.sh` and build this plugin against it with `-p:RestoreAdditionalProjectSources=<base>/nupkg`.
5. Commit and push to Base, then run Base's **Release** workflow.
6. Raise the `JPKribs.Jellyfin.Base` version in this plugin's csproj, delete the local copy, and add a `developer` changelog line naming the Base version.

Never commit this plugin against an unreleased Base package.

## Comments

### The Rules

* Every class, method, function, and component gets a header comment.
* The header is the name, then a summary of one or two sentences, leaning to one.
* List inputs and outputs when there are any. One line each, as `name [type]: What or why`.
* Write no inline comments, with one exception. A branch whose reason is not obvious gets one short line above it.
* A branch comment says why, not what. Keep it under 15 words.
* Never comment what the code already says. `// Check if values differ` is noise.
* Never leave commented out code, TODO, FIXME, or HACK. Open an issue instead.
* Never describe history. "Used to", "now", and "was changed to" belong in commit messages.
* Use no em dashes or semicolons in comments.

```text
<Name>
<What it does, in one sentence.>

Inputs
name [type]: What it is or why it is needed.

Output
[type]: What comes back.
```

Leave out **Inputs** when there are none, and **Output** when nothing comes back.

### C#

C# uses XML docs so the compiler and IntelliSense read them. The member name is the title. The type comes from the signature, so a `param` gives only the what or why.

```csharp
/// <summary>
/// Evaluates a step's own condition against the run's variables, including any an earlier step saved.
/// </summary>
/// <param name="operation">The step.</param>
/// <param name="variables">The run's variables.</param>
/// <returns>True when the step has no condition or it matches.</returns>
public static bool MatchesStep(ScriptOperation operation, IReadOnlyDictionary<string, string> variables)
{
    ArgumentNullException.ThrowIfNull(operation);
    ArgumentNullException.ThrowIfNull(variables);

    var name = StepVariable(operation.OnlyIfVariable);
    if (name.Length == 0)
    {
        return true;
    }

    var candidate = (variables.TryGetValue(name, out var found) ? found : string.Empty).Trim();
    var value = Placeholders.Fill(operation.OnlyIfValue, variables).Trim();
    var negative = operation.OnlyIfOperator is ConditionOperator.IsNot or ConditionOperator.DoesNotContain;

    // A missing value only ever equals nothing: it is never "at least 0" and never contains anything.
    if (candidate.Length == 0 && value.Length > 0)
    {
        return negative;
    }

    var matched = Test(operation.OnlyIfOperator, candidate, value);
    return negative ? !matched : matched;
}
```

* Every type and every member that is not private gets `<summary>`, every `<param>`, and `<returns>` when it returns a value.
* Private members get a `<summary>` when the name does not say enough.
* Use `<inheritdoc/>` on an interface or override member.
* Write `true` and `false` as `<c>true</c>` and `<c>false</c>`.
* A constructor summary is "Initializes a new instance of the <see cref="Name"/> class."
* Skip tautologies. `<param name="item">The item.</param>` says nothing, so write what the item is for.
* Put one type in each file, named after the type. A small enum used by one class may share its file.
* Match the namespace to the folder.

### JavaScript

```js
// renderRunSteps
// Builds a run's steps as a Base Timeline, each with its status, step type, message, and any captured output.
//
// Inputs
// shared [object]: The helper bag from createShared.
// run [object]: A run from the history or a test.
//
// Output
// [HTMLElement]: The timeline, or an empty state when the script has no steps.
export function renderRunSteps(shared, run) {
```

* Comment every top level function, every function assigned to `shared`, and every exported function.
* Do not comment the methods inside a returned handle. List them in the Output line instead.
* Open each file with a header naming the file and saying what it holds in one sentence. Use no `// ====` banners.
* Only `scripts_common.js` imports Base, by the relative path `./configurationpage?name=scripts_jpkribs_shared.js`. It exports each Base builder a page uses, and every page imports from `scripts_common.js`.

### HTML

```html
<!-- Running -->
<!-- How many scripts run at once, how long a command may take, and how many runs are kept. -->
<div class="base-section">
```

* One comment line naming each section or dialog where it starts, and an optional second line saying what it is for.
* Use no inline `style` attributes.

### CSS

```css
/* The Check path button under the command, a field gap above the next field. */
#btnCheckPath { margin: 0 0 var(--base-gap-wide); }
```

* `scripts_shared.css` holds only spacing between Base components that Base does not set, on Base's gap tokens. Colors, borders, and backgrounds come from Base.
* One comment line above each rule saying what it spaces.
* A new plugin class takes the `scripts-` prefix in kebab case, with three comment lines above it: the name, what it looks like, and one example. `base-` is reserved for Base.

### Shell and YAML

* A shell script opens with a comment saying what it does and how to run it.
* A YAML file opens with a comment saying what it is for. Comment a step only when its reason is not obvious.

## C# Conventions

* Target `net10.0` with `Nullable` on, `TreatWarningsAsErrors` on, and `AnalysisMode` set to `AllEnabledByDefault`.
* Keep `jellyfin.ruleset` identical across plugins. Raise a rule change in every plugin at once.
* Suppress a rule only at the narrowest scope, with a reason. `#pragma warning disable CA2100 // SQL is internal and parameterized.`
* Use file scoped namespaces, `sealed` classes, and records for data.
* Name private fields `_camelCase`.
* End every async method name in `Async`, pass the `CancellationToken` through, and use `ConfigureAwait(false)` on every await.
* Guard public arguments with `ArgumentNullException.ThrowIfNull`.
* Log through `ILogger<T>` with static, named templates, and start each message with `Scripts:`. Pass a value from a request or an event as a template argument, never inside the template.
* Catch a specific exception, or filter with `when`. Rethrow `OperationCanceledException` when the token was cancelled.
* Read the clock once in a method and reuse the value. Two reads can straddle a second and disagree.
* Take a `TimeProvider` wherever time decides an outcome, so a test can move it.
* Register services in `PluginServiceRegistrator`.

## JavaScript Conventions

* Each config page is an ES module whose default export calls `createScriptsPage`, which wraps Base's `createConfigPage` and holds the tab list.
* Keep one handle for each Base component a page draws. Call `destroy` before drawing it again, and `setValue` when only its values change.
* Drop an answer that arrives after the page moved on. Remember the picked script before the request and compare it when the answer lands.
* The pages are written with `var` and `function`. Keep a file in its own style, and use `const`, `let`, and arrow functions only in a file that already does.
* Escape every value placed in HTML with `escapeHtml`.
* Report results with `shared.setStatus`, and errors with `shared.errorText`.
* Register every poller and observer with the page handle so `viewhide` stops it.

## Security

Scripts run commands on the server and hold secrets. Every rule here guards one of those, and a change to any of them is a [stop and ask](../starthere.md#stop-and-ask) item.

* Start a command only through `CommandRunner`, with arguments in `ProcessStartInfo.ArgumentList`, one line per argument. Never build a command line from event values.
* Never fill placeholders into a shell line. Event values reach a shell only as environment variables.
* Store every secret with `SecretProtector`, from `StableSecretProtection`, in `SecretVault`. Never write a secret to the config, a file, or a log in plain text.
* Send a stored secret to the browser only as `SECRET_KEPT`. Read it back with `readSecretField` and resolve it with `ScriptSecrets`.
* Mask every secret value in a step's message and a command's output before a run is recorded.
* Leave secret values and webhook addresses out of every export.
* Keep the administrator policy, `[Authorize(Policy = "RequiresElevation")]`, on `ScriptsController`. A webhook needs both its address and a Jellyfin API key.
* Compare a webhook token in constant time, never with `==`.
* Log a value from a request, an event, or a file only as a template argument, and never log a secret.
* Never disable or sign out an administrator from a step.
* Escape every value placed in HTML with `escapeHtml`.
* Never put a secret or a user's data in a URL.

## Tests

Every source file with logic has a test file. See [testing.md](testing.md) for each layer and how to write tests.

## Checklist

* [ ] Base's source and typings were searched, nothing here duplicates Base, and anything reusable went to Base first.
* [ ] Every class, method, function, and component has a header.
* [ ] Inline comments appear only above branches, and say why.
* [ ] Every changed source file has tests that pass, per [testing.md](testing.md).
* [ ] The [security](#security) rules hold.
* [ ] `dotnet build` shows no warnings.
