# Code signing policy

Last updated: 8 October 2026

## Current status

QuickGrab v0.2.0 and the initial GitHub Actions artifacts are **unsigned development
builds**. Smart App Control may block their executable files or libraries.

The project is preparing an application to the SignPath Foundation program.
Approval has not been granted and signing is not yet integrated. No current
release is claimed to be signed, sponsored or endorsed by SignPath.

A successful GitHub Actions build is evidence that the configured build and tests
completed. It is not a code signature or a Windows security acceptance test.

## Project and responsible roles

Source repository: https://github.com/Manash-git/QuickGrab  
Release downloads: https://github.com/Manash-git/QuickGrab/releases

The project currently has one maintainer:

| Responsibility | Maintainer |
| --- | --- |
| Author and committer | [Manash-git](https://github.com/Manash-git) |
| Reviewer of contributed changes | [Manash-git](https://github.com/Manash-git) |
| Designated signing approver, once signing is configured | [Manash-git](https://github.com/Manash-git) |

These roles are held by the same person; independent review is not implied.
GitHub two-factor authentication is configured. Access to any future signing
account must also use multifactor authentication before signing is enabled.

## Build origin

The Windows workflow is defined in `.github/workflows/windows-build.yml`. It
builds from the checked-out repository source, runs the configured C# and
JavaScript tests and publishes an unsigned Windows artifact with documentation
and a `BUILD-INFO.md` record identifying its source commit and workflow run.

Workflow runs: https://github.com/Manash-git/QuickGrab/actions

The originally uploaded v0.2.0 preview ZIP predates the GitHub Actions workflow;
it must not be represented as an artifact produced by that workflow.

## Requirements before signed releases

If signing is approved and configured, the maintainer will:

1. Review source and build-script changes and select the exact release commit.
2. Build through the approved automated integration with verifiable source origin.
3. Check the test results, cumulative release documentation and artifact contents.
4. Manually approve each release signing request through the configured signing
   service. The current unsigned workflow does not implement this approval gate.
5. Sign eligible QuickGrab-owned executable files and libraries. Preserve
   third-party ownership and signatures; do not sign unmodified third-party
   binaries as QuickGrab's own code under a Foundation subscription.
6. Verify signatures, record signing coverage and remaining unsigned dependencies,
   and test the complete package with Smart App Control enabled before claiming
   compatibility. Signing only an installer does not establish that all its
   bundled libraries will be accepted.
7. Publish signed artifacts with their source commit, release notes and signing
   status. Keep credentials and private signing material out of the repository.

The signing provider, certificate identity and applicable attribution will be
published here and linked from release pages only after approval and setup. If
SignPath Foundation supplies signing, this policy will then include its required
attribution with links to SignPath.io and SignPath Foundation.

A signature establishes publisher identity and integrity under its trust chain;
it does not guarantee bug-free software or universal Windows acceptance.

## Privacy and reports

See the [QuickGrab privacy policy](PRIVACY.md) for local data storage, network
requests, diagnostics and private-window behavior.

Report general release or signature concerns through
[QuickGrab issues](https://github.com/Manash-git/QuickGrab/issues). Do not post
private URLs, credentials or sensitive logs. Ask for a private contact method
before sending confidential details. The maintainer will investigate reported
signing misuse and work with the signing provider if one is involved.
