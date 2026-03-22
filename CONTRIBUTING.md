# Contributing

## Prerequisites

- .NET SDK 8.x
- Windows 10/11 (WPF target)

## Local setup

1. Restore packages:
   - `dotnet restore CipherVault.sln`
2. Build solution:
   - `dotnet build CipherVault.sln -c Release`
3. Run tests:
   - `dotnet test CipherVault.sln -c Release`
4. Run app:
   - `dotnet run --project CipherVault.UI/CipherVault.UI.csproj -c Release`

## Branching and PRs

- Create a focused branch per change.
- Keep PRs small and scoped.
- Include tests for behavior changes.
- Ensure CI is green before requesting review.

## Coding standards

- Nullable reference types must stay enabled.
- Non-test projects treat warnings as errors.
- Prefer explicit, defensive validation for crypto and persistence boundaries.
- Avoid logging secrets (passwords, keys, plaintext vault data).

## Commit guidance

- Use clear, imperative commit titles.
- Mention affected areas (Core/Data/UI/Tests).
- Document any migration or compatibility impact.

## Reporting bugs

Include:

- Steps to reproduce
- Expected vs actual behavior
- OS version and .NET SDK version
- Relevant logs/errors (without sensitive data)
