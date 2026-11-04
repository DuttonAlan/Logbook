# Security policy

Logbook stores the logs of other applications, which often contain personal data, internal addresses and details of failures, so security reports are taken seriously and handled before other work.

## Supported versions

Logbook has not reached version 1.0. Only the latest release receives security fixes. Once 1.0 is released, this table will list the supported versions.

| Version | Supported |
|---|---|
| Latest release | ✅ |
| Older releases | ❌ |
| `main` branch | Fixed there first |

## Reporting a vulnerability

**Do not open a public issue, discussion or pull request for a vulnerability.**

Report it privately through GitHub: open the [**Security** tab](https://github.com/DuttonAlan/Logbook/security) of the repository and click **Report a vulnerability**. Only the maintainers can see the report.

Please include:

- the affected version or commit, and the component (server, web interface or SDK),
- a description of the issue and its impact,
- steps to reproduce, ideally a minimal proof of concept,
- your configuration, if relevant (never send real API Keys or log data),
- whether and how you want to be credited.

## What happens next

Logbook is maintained by one person in their spare time, so these are targets, not guarantees:

| Step | Target |
|---|---|
| Acknowledgement of your report | within 5 days |
| First assessment (accepted or not, severity) | within 14 days |
| Fix released for critical and high severity issues | within 90 days |

You will be kept informed in the advisory. Once a fix is released, a GitHub Security Advisory is published and, if you wish, you are credited. Please do not disclose the issue publicly before the advisory is published or 90 days have passed, whichever comes first; if more time is needed, we will ask you.

## Scope

In scope: everything in this repository, including the log and query APIs, the web interface, the SDK packages, the container images and the default configuration.

Out of scope:

- vulnerabilities that need an already compromised host, database or administrator account;
- missing features that are listed on the [roadmap](docs/roadmap.md), e.g. sign-in to the web interface before milestone M4;
- denial of service through sheer request volume;
- reports from automated scanners without a demonstrated impact.

## Safe harbor

Good-faith security research on your own installation of Logbook is welcome. Do not test against installations you do not own or have no permission to test, do not access or change other people's data, and do not degrade other people's services.
