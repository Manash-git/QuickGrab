# Build vX.Y.Z — release purpose

Date: YYYY-MM-DD | Platform: Windows x64

## New implementations
- Describe each implemented feature, or explicitly write None.

## Modifications and why
- Change: ...
- Reason: ...

## Bug fixes
- Symptom, known cause, fix and verification; or explicitly write None.

## Validation
- Commands/environment and actual results.
- Distinguish automated tests, compilation, manual Windows checks and user confirmation.
- List pending checks; never claim tests that were not run.

## Upgrade and data impact
- Schema, compatibility, registration, backup and migration steps.

## Known limitations and privacy impact
- Remaining gaps and supported scope.

## Before delivery
1. Append this record to docs/releases.json; retain every earlier version from 0.1.1.
2. Update CHANGELOG.md and the cumulative project handbook, oldest to newest.
3. Review architecture, prerequisites and operating instructions against current code.
4. Run CheckReleaseDocs.ps1 and relevant tests; record evidence.
5. Include current documentation with the package. Review Word/PDF pages if supplied.

The structural build gate does not verify prose accuracy or execute test claims.
