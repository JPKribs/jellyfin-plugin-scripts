# Release

How to cut a release of this plugin, from the version number to the manifest every server reads.

## Versions

A version is `JJ.JJ.F.B`.

| Part | Means | Raise it when |
| :--- | :--- | :--- |
| `JJ.JJ` | The Jellyfin version the release targets, such as `12.4` | The plugin moves to a new Jellyfin version |
| `F` | The feature release | The release adds or changes behavior a user will notice |
| `B` | The bug release within that feature | The release only fixes bugs |

Raising a part sets every part after it to `0`. `12.4.1.3` becomes `12.4.2.0` for a feature and `12.4.1.4` for a fix.

## Branch

A feature release is cut on `unstable`, then **Sync Branches** with `promote` moves `master` to it. A bug release is cut on `master`, and the sync carries it to `unstable`. See [branches.md](branches.md).

## Before the Release

1. **Base.** When the release needs a Base change, release Base first with its **Release** workflow, then raise `JPKribs.Jellyfin.Base` in `Jellyfin.Plugin.Scripts/Jellyfin.Plugin.Scripts.csproj` and delete any local copy Base replaces. Wait until NuGet serves the version, and never release against a local package. Raise Base on a branch, and merge it once every check below has passed on it.
2. **State files.** When a script field, the history, or another state file changed shape, make sure an older file still loads, and that a newer field an older release does not know is ignored rather than lost. This is a [stop and ask](../starthere.md#stop-and-ask) item.
3. **Build.**

    ```bash
    dotnet build
    ```

4. **Tests.**

    ```bash
    dotnet test
    ```

    ```bash
    for f in Jellyfin.Plugin.Scripts/Web/*.js; do node --check "$f"; done
    ```

5. **Test server.** Install the zip from `./build.sh` on a Jellyfin 12.4 test server, open each tab, and run **Test** and **Run now** on a script that uses every step the release changed. See [testing.md](testing.md#test-server).

    Every check must run on the commit that is released. Run them again after any later change.

6. **Docs.** Every page describes this release. Retake any screenshot whose subject changed, and all of them when the Base version changed, with the [screenshot kit](documentation.md#the-screenshot-kit). Update [Versioning](../docs/Versioning.md) when the supported Jellyfin version changed, and [Architecture](../docs/Architecture.md) when the parts or their connections changed.

    ```bash
    mkdocs build --strict
    ```

7. **Review.** Run [review.md](review.md) over everything since the last release.

## Set the Release

Run these steps in this order, and skip none of them.

Merge the release branch into `master` and release from `master`.

```bash
git checkout master
```

```bash
git merge --no-ff <branch>
```

Then edit `build.yaml`.

1. Set `version` to the new version.
2. Replace the whole `changelog` with this release's entries. Drop every line that shipped in the last release, and every line a later change in this release replaced. See [documentation.md](documentation.md#release-notes) for how to write them.
3. Say in `changed` when a script field or a state file changed, so a user knows an older release may not read it.
4. Name the Base version in `developer` when it changed, such as "Uses JPKribs.Jellyfin.Base 12.4.1", with what it replaced locally.
5. Keep `description` and `overview` true for this release. `manifest.json` takes its own description, so update both together.

Commit it on its own.

```bash
git commit -am "Set the release to 12.4.2.0"
```

```bash
git push
```

## Publish

1. **Draft.** The **Create/Update Release Draft** workflow builds the zip and writes a draft release named `v<version>` with the changelog as its notes. A push that only changes `build.yaml` does not start it, so start it from the Actions tab then. Draft builds run one at a time, so a push and a manual start that overlap build one after the other.
2. **Check.** Wait until no draft build is queued or running, since each one replaces the zip. Then open the draft on GitHub. The notes must match `build.yaml`, and the zip and its `.md5` must be attached.
3. **Release.** Publish the draft. The **Publish Plugin** workflow waits for any draft build still running, hashes the attached zip, checks it against the `.md5`, adds the version to `manifest.json`, and commits "Update manifest for release v<version>". A draft build never replaces the assets of a published release.
4. **Confirm.** Open that commit. The new entry must name the version, and its `checksum` must equal the `.md5` attached to the release.
5. **Pull.**

    ```bash
    git pull
    ```

6. **Verify.** On a test server, add the repository URL, install the release from the catalog, restart, reload the dashboard, and open each tab.

## When a Release Is Wrong

* Never edit or reuse a published version. Fix it in a new bug release.
* When a release breaks servers, publish the fix as the next bug release at once.
* When the manifest entry itself is wrong, run **Publish Plugin** by hand with the release tag after fixing the cause.

## Checklist

* [ ] The version follows `JJ.JJ.F.B`, and later parts are `0`.
* [ ] Base is released and referenced, with local copies deleted.
* [ ] An older state file still loads.
* [ ] `dotnet build`, `dotnet test`, and `node --check` pass on the released commit.
* [ ] The build was installed on a test server and each tab was opened.
* [ ] Docs, screenshots, Versioning, and Architecture describe this release.
* [ ] `mkdocs build --strict` passes.
* [ ] The changelog lists only this release, in the four keys.
* [ ] The release branch is merged into `master`, and the release is set there.
* [ ] The published draft has the notes, the zip, and its `.md5`, and matches `build.yaml`.
* [ ] The manifest commit names the version with the zip's checksum, and is pulled.
