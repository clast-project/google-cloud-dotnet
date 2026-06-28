# Clast upstream sync & release process

This fork republishes specific Google/gRPC .NET libraries under `Clast.*` ids. We keep up with
upstream and publish on a unified version. The mechanism spans four sibling forks under
[`clast-project`](https://github.com/clast-project):

| Repo | Upstream | Republished packages |
|---|---|---|
| `gax-dotnet` | googleapis/gax-dotnet | `Clast.Google.Api.Gax`, `.Rest`, `.Grpc` |
| `google-api-dotnet-client` | googleapis/google-api-dotnet-client | `Clast.Google.Apis.Core`, `.Google.Apis`, `.Auth`, `.Storage.v1`, `.Bigquery.v2` |
| `google-cloud-dotnet` | googleapis/google-cloud-dotnet | `Clast.Google.Cloud.Storage.V1`, `.BigQuery.V2`, `.BigQuery.Storage.V1` |
| `grpc-dotnet` | grpc/grpc-dotnet | `Clast.Grpc.Auth` |

## How it works

1. **Detect** — `.github/workflows/upstream-watch.yml` runs weekly in each repo. It reads
   `.clast/upstream-baseline.json` (the upstream NuGet versions we last synced to) and compares
   each against nuget.org. If any upstream package shipped a newer stable version, it opens or
   updates a single issue labeled **`upstream-sync`** in that repo.

2. **Sync** — a scheduled **Claude routine** reads open `upstream-sync` issues across the four
   repos. For each, it fetches `upstream`, merges `upstream/main` into a `sync/upstream-<date>`
   branch, resolves the Clast port conflicts (System.Text.Json instead of Newtonsoft.Json,
   trimming/AOT, the `-p:Clast=true` packaging flag), builds + tests, refreshes
   `.clast/upstream-baseline.json` to the new versions, and opens a PR that closes the issue.
   **A human reviews and merges** the sync PR.

3. **Publish** — once the relevant sync PRs are merged, push a unified release tag `v<X.Y.Z>` on
   **`google-api-dotnet-client`**. Its `clast-publish.yml` packs all `Clast.*` packages across the
   four repos at that single version and pushes them to NuGet.org. The Clast version is its own
   line (independent of each library's upstream version).

## Requirements

- `clast-publish.yml` needs a `NUGET_API_KEY` secret (org-level secret on `clast-project` covers
  all repos). The watch workflow needs no secrets — it uses the built-in `GITHUB_TOKEN`.
- Adding a new fork to the system: drop in `upstream-watch.yml` (unchanged) + a
  `.clast/upstream-baseline.json` listing its upstream packages, and add the repo to the routine.
