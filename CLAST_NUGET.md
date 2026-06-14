# Clast republish

This is a **Clast** package — an independent republish (from the [`clast-project`](https://github.com/clast-project) fork) of a Google .NET library. **It is not an official Google package and is not affiliated with or endorsed by Google.**

**How it differs from the upstream package:**

- `Newtonsoft.Json` is replaced with **source-generated `System.Text.Json`** (internal JSON handling such as `UrlSigner` V4 signing via `Utf8JsonWriter` and BigQuery row parsing via `JsonElement`).
- The library is **trimming / Native-AOT compatible**, with a **`net10.0`** target added (alongside `netstandard2.0` and `net8.0`).
- **Namespaces and public type names are unchanged.** Only the package id, assembly name, and strong-name key differ, so you recompile against the `Clast.*` packages rather than dropping them in as binary replacements.

**Packages republished from this repository:** `Clast.Google.Cloud.Storage.V1`, `Clast.Google.Cloud.BigQuery.V2`, `Clast.Google.Cloud.BigQuery.Storage.V1` (the last is a pure-gRPC/protobuf package — it has no JSON of its own and is built on the trimming/AOT-compatible Clast gRPC stack).

Full design notes and the behavior-change catalogue live in the [`clast-project/google-api-dotnet-client`](https://github.com/clast-project/google-api-dotnet-client) repository (`PLAN.md`, `BEHAVIORAL-CHANGES.md`).
