# Profiling

How to find and fix slow C# in this plugin. It covers CPU, memory, allocations, locks, the state files, and HTTP. Every other language is out of scope.

## When to Profile

Profile only when there is a reason. Never profile to look for something to improve.

Start when one of these is true.

* A user or a test reports that something is slow, uses too much memory, or starves playback.
* A change adds or alters work on a path that runs for every event or every item, such as an event handler, `PlaybackTracker`, `ScriptDispatcher`, the 5 second tick, `ForEachExpander`, or a history write.
* A number in a test or a run's duration grew from one release to the next.

Without one of these, do not profile. Code that has no symptom and no change is not a problem.

## The Tiers

Profiling has three tiers. Start at Tier 1. Go up a tier only when the one below could not answer the question. Most issues end at Tier 1 or Tier 2.

| Tier | Answers | Budget | Needs a server |
| :--- | :--- | :--- | :--- |
| 1. Time One Method | Is this code slow by itself | 10 minutes | No |
| 2. Trace One Path | Which code dominates one slow event, run, or route | 30 minutes | Yes |
| 3. Go Deeper | Why memory grows, or what a second path costs | Ask first | Yes |

* **Tier 1** is a temporary timing test against realistic input, such as 50 scripts, a busy history, or a for each over 10,000 items. It needs no server.
* **Tier 2** is one CPU trace and counters of Jellyfin on a test server while one path runs, the one the issue points at.
* **Tier 3** is a heap dump, a scaled copy of a state file, or a second path. Stop and ask before you start it.

### The Budget

* Each tier has a time budget. When it runs out, stop and report what you have, even when it is inconclusive.
* Profile one path per issue. Never sweep every path "to be sure".
* Stop as soon as the goal is met or the cause is proven.
* Write the tier, the path, and the stop reason in the report.

## The Rule

Measure first, change second, measure again. A change made for speed is not done until a number shows it is faster and the tests still pass.

* Never optimize from a guess. A hot path you cannot show is not a hot path.
* Never trade correctness, a safety limit, or a [security](code.md#security) rule for speed.
* Keep the simplest code that meets the goal. Stop when the goal is met.

## Goals

State the goal before you measure. A goal names the operation, the data size, and the limit.

| Good | Bad |
| :--- | :--- |
| Matching one playback event against 100 scripts takes under 1 ms | Make matching faster |
| A for each over 20,000 items stays under 200 MB | Reduce memory |

The plugin runs inside Jellyfin on the user's server. Treat its CPU, memory, and disk as shared. A task that is fast but starves playback has failed.

## Check These First

These causes are common. Check each against the code before you measure anything.

| Check | Usual cause |
| :--- | :--- |
| A loop that asks Jellyfin the same question for every item or every user and item pair | The answer does not change inside a run, so ask once per run |
| Work in an event handler before the job is queued | Handlers must build the context and queue, and leave the rest to `ScriptRunner` |
| Every script tested against every event | Scripts are not looked up by trigger first, as `ScriptDispatcher.ScriptsFor` does |
| The history file written whole on every run in a burst | Writes are not batched or debounced |
| Play state read for every user when no condition asks for it | `ForEachExpander` reads more than the conditions need |
| A log string built in a hot loop | The string is built even when the level is off |

If the code shows the cause, a Tier 1 test is enough to prove it.

## Test Data

Tier 2 needs data that is big enough to show the problem and no bigger.

* Use a test server with a library of a few thousand small items, and the scripts the issue describes.
* Write down the data size in the report.
* Never build a bigger library to prove a scaling claim. Scale a copy of the state file or the input in a Tier 1 test, or ask for Tier 3.

## Tools

Use only the tools Microsoft ships for .NET. They need the .NET 10 SDK, the same as the repo, so check it first.

```bash
dotnet --list-sdks
```

Install the tools once. Update them with `dotnet tool update --global` and the same name.

```bash
dotnet tool install --global dotnet-trace
```

```bash
dotnet tool install --global dotnet-counters
```

```bash
dotnet tool install --global dotnet-gcdump
```

The tools install to `~/.dotnet/tools`. On macOS that folder can be missing from `PATH`, because the dotnet installer writes the literal text `~/.dotnet/tools` to `/etc/paths.d`, and a shell does not expand it. Add this line to `~/.zshrc` and open a new shell.

```bash
export PATH="$PATH:$HOME/.dotnet/tools"
```

Check that each tool runs.

```bash
dotnet-trace --version
```

| Question | Tool |
| :--- | :--- |
| Which methods use the CPU | `dotnet-trace` |
| Is the process healthy over time | `dotnet-counters` |
| What holds the memory | `dotnet-gcdump` |
| How fast is this one method | A temporary test with `Stopwatch` and `GC.GetAllocatedBytesForCurrentThread()` |
| How long did this run take | A `Stopwatch` in a temporary test, or `dotnet test` timing |

A trace is a `.nettrace` file, which `dotnet-trace` writes by default. Open it in PerfView on Windows, or turn it into a speedscope file and open that at [speedscope.app](https://www.speedscope.app). Pass `--format Speedscope` to `collect` to get both files in one step, or convert a trace later.

```bash
dotnet-trace convert run.nettrace --format Speedscope -o run
```

This writes `run.speedscope.json`. Keep every trace, dump, and test output outside the repository.

## Tier 1: Time One Method

A method with no I/O is best measured alone. For a state file, time the read or write in a test against a copy in a temporary folder, as `TestServices` makes.

* Write a temporary xUnit test in the test project that times the method with `Stopwatch` and reads `GC.GetAllocatedBytesForCurrentThread()` before and after. Take the difference on the same thread, since the counter covers only the current thread. Write the numbers with `ITestOutputHelper`, and read them with `--logger "console;verbosity=detailed"`. Never commit the test.
* Build and run in `Release`, with `dotnet test -c Release`, and without a debugger.
* Warm up first. Call the method a few times before you measure, so the JIT is not in the number.
* Run it many times and take the median. One run proves nothing.
* Run it before and after the change, on the same machine, with nothing else busy.
* Give it realistic input. A list of three scripts hides what a list of 100 shows.
* Never commit the test, its output, or any trace.

To see where the time goes inside the method, trace the test host. Make the test loop for at least 10 seconds, so a trace has time to attach. Do not use `dotnet-trace collect -- dotnet test`. It traces the `dotnet test` command and not the test code, and it can hang. Run the tests in one shell and attach from another.

```bash
dotnet test -c Release --filter FullyQualifiedName~MyTimingTest
```

Find the test host process id, which is the newest `testhost` process.

```bash
pgrep -f testhost
```

```bash
dotnet-trace collect --process-id <pid> --duration 00:00:08 --format Speedscope -o ~/Desktop/method.nettrace
```

For what holds the memory, take a heap dump of the same process, and read it.

```bash
dotnet-gcdump collect --process-id <pid> -o ~/Desktop/method.gcdump
```

```bash
dotnet-gcdump report ~/Desktop/method.gcdump
```

## Tier 2: Trace One Path

The plugin runs in Jellyfin's process, so attach to that process on a test server. Trace one path, the one the issue points at, such as a burst of playback events or one for each run.

1. Install a `Release` build from `./build.sh` on the test server and restart it.
2. Find Jellyfin's process id on the machine that runs it.

    ```bash
    dotnet-trace ps
    ```

    In Docker the tools must run inside the container, from the same `/tmp` as Jellyfin, since a tool outside cannot reach its diagnostic socket. Copy the standalone Linux builds of the tools into the container, unpack a .NET runtime beside them, and point `DOTNET_ROOT` at it on every call. Jellyfin is then process 1.
3. Record a CPU trace while the slow path runs. Use the `dotnet-sampled-thread-time` profile. The default profile adds JIT and loader events and makes a trace of 100 MB or more for a few minutes.

    ```bash
    dotnet-trace collect --process-id <pid> --profile dotnet-sampled-thread-time --duration 00:01:00 -o ~/Desktop/run.nettrace
    ```

    The trace ends by itself after the duration, so pick a duration longer than the run. Wait for the tool to exit before reading the file. A trace read while the tool still runs is cut short and has no method names.
4. Convert the trace and read it.

    ```bash
    dotnet-trace convert ~/Desktop/run.nettrace --format Speedscope -o ~/Desktop/run
    ```

5. Read the `.speedscope.json` file in speedscope, or sum it with a script. Every thread is sampled, so an idle thread shows as `UNMANAGED_CODE_TIME`, and a thread running managed code shows as `CPU_TIME`. Native work such as file and process calls also shows as `UNMANAGED_CODE_TIME`, so credit it to the nearest plugin frame above it. Ignore `System.GC.RunFinalizers`, which reports the idle finalizer thread as busy. A plugin frame that wraps Jellyfin's own work, such as an event handler, is inclusive of that work, so look at its callees before blaming it.
6. For counters, record while the run goes. The output is `.json` and ends without its closing brackets when the tool is stopped with `SIGINT`, so add them before parsing. Read allocation (`dotnet.gc.heap.total_allocated`), collections, thread pool queue length, and lock contentions (`dotnet.monitor.lock_contentions`). Counters named `/ 1 sec` are per second, so add them up over the run.

    ```bash
    dotnet-counters collect --process-id <pid> --counters System.Runtime --format json --duration 00:01:00 -o ~/Desktop/counters
    ```

7. For what holds the memory, take a heap dump in the middle of the run and again after it, and read both. The report lists types by size, so sort it by size times count.

    ```bash
    dotnet-gcdump collect --process-id <pid> -o ~/Desktop/mid.gcdump
    ```

    ```bash
    dotnet-gcdump report ~/Desktop/mid.gcdump
    ```

8. To time a route, send a Jellyfin API key in an `Authorization: MediaBrowser Token="<key>"` header.

    ```bash
    curl -s -o /dev/null -w "%{time_total}" -H 'Authorization: MediaBrowser Token="<key>"' "http://localhost:8096/Scripts/History?take=50"
    ```

9. To inspect a state file, copy it out of the plugin configurations folder and open the copy only.

## Tier 3: Go Deeper

Stop and ask before you start Tier 3. Say what Tier 2 left unanswered and what you will do.

* Take a heap dump in the middle of the run and after it, per the memory step of Tier 2.
* Scale a copy of a state file to the size the issue needs, and time the plugin against the copy. Never edit the live file.
* Trace a second path only when the first trace points at it.

## Reading a Trace

* **Inclusive time** is a method plus everything it calls. **Exclusive time** is the method alone. Start from the largest inclusive plugin frame and walk down to the largest exclusive one.
* A frame in `System.Threading` or a wait is time spent blocked. Find what it waits on, since more CPU will not help.
* Many small frames in the allocator or the GC point to allocation pressure. Read the allocation counters, then find the allocating call.
* Time spent in a `Task` that never shows CPU is waiting on I/O. Count the calls before you tune one call.

## Common Fixes

Try these in order. Each is cheaper and safer than the next.

1. **Do less.** Skip a script whose triggers do not include the event. Stop a match at the first condition that decides it.
2. **Do it once.** Hoist work out of a loop. Cache a result for the length of one run, never across runs unless it is invalidated.
3. **Do it in bulk.** One library query for a set, not one per item. One history write for a burst of runs. One page, not the whole history.
4. **Do it together.** Run independent calls with `ConcurrentTaskRunner` from Base, with a limit that respects **Runs at once**.
5. **Allocate less.** Use `Span<T>`, `StringBuilder`, pooled buffers, and `static` lambdas where a trace shows allocation pressure. Never do this without a number.
6. **Stream, never buffer.** Read a command's output or a response as a stream, capped as `CommandRunner` and the HTTP step cap them.

### Async and Threads

* Never block on a task with `.Result` or `.Wait()`. It starves the thread pool.
* Pass the `CancellationToken` through, so a stopped run stops working.
* Hold a lock for the shortest time, and never across an `await`.
* Bound every queue and every degree of parallelism.

### HTTP

* Send every request through `BaseHttp` and the `IHttpClientFactory` the plugin takes. Never build a new `HttpClient` per call.
* Keep the HTTP step's retry count and backoff as the user set them. Never retry more to hide a slow service.

### Logging

* Use the `ILogger` templates in [code.md](code.md#c-conventions). Never build the message with string interpolation.
* A log in a hot loop costs even when the level is off. Move it out of the loop or guard it with `IsEnabled`.

## Proving the Result

1. Measure the same scenario before and after, with the same data and the same machine. Record both numbers.
2. Run the tests for every layer the change touches, per [testing.md](testing.md#layers).
3. For a change to how events, matching, or steps behave, run the trigger on a test server and check the history. A faster wrong answer is a bug.
4. Check that memory and the thread count stay flat across a long run.
5. Report the numbers in the commit message or the report, as before and after for the same operation.

## What Not to Do

* Never ship a profiler, a timing test, a `Stopwatch`, an allocation counter, or a timing log in the plugin.
* Never commit trace files, gcdumps, or timing output.
* Never raise a timeout or lower a retry count to hide a slow call.
* Never remove a check, a cap, or a guard to gain speed, such as the run queue's bound or the output caps.
* Never profile a Debug build. Use `Release`.
* Never change a [stop and ask](../starthere.md#stop-and-ask) item without approval, even when it is the cause.

## Checklist

* [ ] The reason to profile is a symptom, a change to a hot path, or a number that grew.
* [ ] The tier, the path, and the stop reason are in the report.
* [ ] The goal names the operation, the data size, and the limit.
* [ ] A trace, a counter, or a timed test shows where the time or memory goes.
* [ ] The fix is the simplest one that meets the goal.
* [ ] Before and after numbers exist for the same operation and data.
* [ ] A `Release` build was measured.
* [ ] The tests for every touched layer pass, and the trigger was run on a test server.
* [ ] No profiler code, trace file, timing test, or output is in the diff.
* [ ] Anything on the stop and ask list, such as a state file's shape, was approved first.
