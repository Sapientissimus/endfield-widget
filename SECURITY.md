# Security policy

## Reporting a vulnerability

Please use GitHub's [Report a vulnerability](https://github.com/Sapientissimus/endfield-widget/security/advisories/new) form to contact the maintainer privately. Please keep security-sensitive details out of public issues and pull requests.

Include the following information where possible:

- The Endfield Widget version and Windows version.
- The affected feature or source file.
- Steps to reproduce the problem, or a minimal proof of concept using test data.
- The expected security behavior, actual behavior and potential impact.
- Any workaround you have identified.

Reports do not need real credentials. Please remove SKPORT tokens, session credentials, account identifiers and other personal information from text, screenshots and attachments, including private reports.

For ordinary bugs, please use the [bug report form](https://github.com/Sapientissimus/endfield-widget/issues/new?template=bug_report.yml).

## Supported versions

Security fixes target the [latest published release](https://github.com/Sapientissimus/endfield-widget/releases/latest). Please include the exact affected version in your report. If the problem was found in an older version, indicate whether it also occurs in the latest release when you can safely check.

## Project scope and security expectations

Endfield Widget is a local Windows desktop application. It retrieves account status from SKPORT, reads public official announcements and stores settings and cached data in the current user's local profile. Saved account tokens use Windows DPAPI protection for the current Windows user.

Reports about the code in this repository and the release builds published here are welcome. Examples include:

- Exposure of credentials or private account data through requests, logs, diagnostic exports or release files.
- Use of account credentials outside their intended account or service.
- Execution of untrusted announcement or cache content, unsafe external links, or unintended file access caused by untrusted input.

Remote content and cached data must be treated as untrusted input. Credentials and private account data must stay out of public reports, diagnostic exports and distributed builds. These are security expectations, not a claim that every possible issue has been audited.

## Coordinated disclosure

Please keep exploit details private while the report is assessed and a fix is prepared. Any applicable fix and public disclosure can then be coordinated through the private advisory.
