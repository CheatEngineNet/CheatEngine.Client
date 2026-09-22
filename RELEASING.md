# Releasing CheatEngine.Client

The seven Client packages are released together, with one version, from one tag:

| Package                                             | Content                                                     |
|-----------------------------------------------------|-------------------------------------------------------------|
| `CheatEngine.Client`                                | The umbrella package a plugin references                    |
| `CheatEngine.Client.Abstractions`                   | Contracts, requests, failures and value vocabulary          |
| `CheatEngine.Client.Core`                           | The SDK-facing implementation                               |
| `CheatEngine.Client.Extensions.DependencyInjection` | DI registrations, modules, codecs and options               |
| `CheatEngine.Client.Fluent`                         | Immutable fluent builders                                   |
| `CheatEngine.Client.Hosting`                        | The plugin host, the Lua generator and the consumer targets |
| `CheatEngine.Client.Templates`                      | The `dotnet new ceplugin` template                          |

No Client version has been tagged or published yet.

## One-time trusted publishing setup

nuget.org accepts the packages only from the release workflow, through
[trusted publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing): the workflow exchanges a GitHub
OIDC token for a short-lived API key, and no long-lived NuGet API key is stored anywhere. Create the policy at
<https://www.nuget.org/account/trustedpublishing> with these exact values:

| Field            | Value                                  |
|------------------|----------------------------------------|
| Repository owner | `CheatEngineNet`                       |
| Repository       | `CheatEngine.Client`                   |
| Workflow file    | `release.yml`                          |
| Environment      | `nuget`                                |
| Scope            | Push new packages and package versions |
| Package glob     | `CheatEngine.Client*`                  |

The glob covers the seven package ids, including `CheatEngine.Client.Templates`. None of them exists on nuget.org
yet, so the scope must allow new packages.

The GitHub environment `nuget` holds one environment secret, `NUGET_USER`: the nuget.org profile name of the person who
created the policy, not an e-mail address and not an organization name. Restrict the environment to deployment tags
matching `v*.*.*` and add the maintainers who approve a publication as required reviewers. A key obtained through
trusted publishing is valid for one hour, and each OIDC token yields one key, so the workflow logs in once, right
before it pushes the seven packages.

## Prepare a release

1. Move the entries of `## [Unreleased]` in [CHANGELOG.md](CHANGELOG.md) to a `## [X.Y.Z] - YYYY-MM-DD` section. Its
   body becomes the GitHub release notes; a stable tag fails without it.
2. Set `MinVerMinimumMajorMinor` in [Directory.Build.props](Directory.Build.props) to the line being released. The
   exact version comes from the `vX.Y.Z` tag; the property is only the floor for untagged commits.
3. Check the qualification gate below.
4. Rehearse the pack locally with the version the tag will produce, and inspect the seven packages:

   ```powershell
   dotnet restore CheatEngine.Client.slnx --locked-mode
   dotnet build CheatEngine.Client.slnx -c Release --no-restore
   dotnet pack CheatEngine.Client.slnx -c Release --no-build -o artifacts/rehearsal -p:MinVerVersionOverride=0.1.0
   ```

   Never leave `MinVerVersionOverride` set as an environment variable: MinVer reads it from the environment too.

## Qualification gate

A Client release is a claim about a tuple: the Client version, the exact `CheatEngine.SDK` package it consumes, that
package's native bridge, the Cheat Engine host profile and the load profile. Before tagging, record for that tuple:

- the receipts of the Client host scenarios Q09 and Q10 (two plugins in one host) and Q43 to Q46 (cleanup with a
  faulty module, contract-only APIs, sensitive probes, log redaction), or an explicit, dated waiver for each missing
  one;
- the result of Q40 (clean installation from the packages) on the exact host profile, in addition to the package
  consumption tests that CI runs on every change;
- a green consumer-contract run against the pinned SDK (Q48 at the managed-test level);
- that audit finding F05 (Client and SDK `main` diverge) stays open while the Client consumes `CheatEngine.SDK` 1.0.0.

A CI result is never presented as a host result, and a scenario that was not run stays not run.
