# Security Policy

## Reporting a vulnerability

Please use **GitHub's private vulnerability reporting**: open the repository's **Security** tab and choose **Report a vulnerability**. This keeps the report private between you and the maintainers.

Please do **not** open a public issue, discussion, or pull request for an unpatched vulnerability.

Include what you can of: package version and couplet, host ROCm/driver/GPU details, a minimal reproduction (model + code), and the impact you observed. We will acknowledge reports and keep you informed of progress toward a fix and release.

## Supported versions

Support is tracked per **couplet** (ONNX Runtime source version + ROCm runtime version baked into the native libraries):

| Package version | Couplet | Supported |
| :--- | :--- | :--- |
| `1.24.1.x` | ORT v1.24.1 + ROCm 7.2.x | :white_check_mark: Supported |
| `1.19.2.x` (ROCm EP era) | ORT v1.19.2 + ROCm EP | :x: Unsupported — superseded by the MIGraphX migration |

Security fixes land on the supported couplet line only. If you are on an unsupported version, upgrade to the current line before reporting.
