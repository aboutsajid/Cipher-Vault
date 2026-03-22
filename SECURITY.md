# Security Policy

## Supported versions

Security fixes are applied to the latest code on the default branch.

## Reporting a vulnerability

Do not open public issues for sensitive vulnerabilities.

Use one of these channels:

1. GitHub Security Advisories (preferred)
2. Private disclosure to repository maintainers

If no private channel is available, open a minimal issue without exploit details and request a private contact path.

## What to include

- Vulnerability type and impact
- Affected components/files
- Reproduction steps or PoC
- Suggested remediation (if available)

## Response expectations

- Initial acknowledgment: within 7 days
- Triage and severity assessment: as soon as reproducible
- Fix timeline: based on severity and exploitability

## Handling secrets

- Never commit real keys/passwords.
- Treat exported vault files and captured credentials as sensitive.
- Use redacted test data only.
