# QuickGrab release requirements

For every delivered build, update documentation alongside the implementation.
Maintain docs/releases.json, CHANGELOG.md and docs/PROJECT-HANDBOOK.md with all
build records from v0.1.1 through the current version in ascending order.
Each record must describe new implementations, modifications AND their reasons,
and bug fixes. Explicitly state when a category has no changes. Also record tests,
known limitations, upgrade/data impact and remaining Windows acceptance checks.
Never present a compile as a runtime test or inherit a past test count as a new run.
Preserve previous records; annotate corrections explicitly. Review architecture,
stack, prerequisites and setup against actual source for each release.
Include an updated readable handbook with the release package; use the release
template in docs/RELEASE-TEMPLATE.md. Run scripts/CheckReleaseDocs.ps1 before
building or publishing. A documentation-only revision does not invent a new
software version. Do not remove this requirement to bypass a failed check.
