# Feedback
[![Issues](https://img.shields.io/badge/Issues-_?color=2C3439&logo=GitHub)](https://github.com/apkd/Medicine/issues) [![Open Issues](https://img.shields.io/github/issues/apkd/medicine?label)](https://github.com/apkd/Medicine/issues) [![Closed Issues](https://img.shields.io/github/issues-closed/apkd/medicine?label&color=33cc33)](https://github.com/apkd/Medicine/issues?q=is%3Aissue%20state%3Aclosed)

### Bug reports

Please include enough detail that anyone can reproduce the problem quickly without guessing.

- **What happened**: include the exact error text, screenshots, and logs if relevant.
- **Repro steps**: smallest code/config that still shows the issue.
- **Environment details**: Unity version, package version, and anything else that could affect behavior.
  - Edit mode/play mode/build, Mono/IL2CPP, release/debug?...

Pull requests with tests that reproducibly demonstrate the problem are even better, and help prevent future regressions.

### Feature requests and suggestions

Feedback is welcome; feel free to create as many issues as you like.

# Contributing
[![Pull requests badge](https://img.shields.io/badge/Pull%20Requests-_?color=2C3439&logo=GitHub)](https://github.com/apkd/Medicine/pulls) [![Open PR count](https://img.shields.io/github/issues-pr/apkd/medicine?label=)](https://github.com/apkd/Medicine/pulls)
[![Closed PR count](https://img.shields.io/github/issues-pr-closed/apkd/medicine?label=&color=33cc33)](https://github.com/apkd/Medicine/pulls?q=is%3Apr+is%3Aclosed)

Please ensure compatibility with the project's license (MIT).
Only submit code that you have the rights to.

### Bug fixes, improvements and new features
[![Contributors](https://img.shields.io/github/contributors/apkd/medicine?labelColor=2C3439&label=Contributors&logo=counterstrike)](https://github.com/apkd/Medicine/graphs/contributors)

Bug fixes are appreciated. I will also merge new features that fit the project's theme and aesthetic.

### Test suite enhancements
[![Test status badge](https://github.com/apkd/Medicine/actions/workflows/test.yml/badge.svg?branch=master&event=push)](https://github.com/apkd/Medicine/actions/workflows/test.yml)

You're welcome to create pull requests with additions and improvements to the test suite.
 
- The test suite is run by the CI to keep things stable. All tests must pass before release.
- Tests in the `Common` directory are run in play mode, edit mode, as well as in a build.
  - This is the preferred location for tests that are not specific to play mode or edit mode.

### Documentation
[![Wiki page count](https://img.shields.io/badge/dynamic/regex?url=https%3A%2F%2Fgithub.com%2Fapkd%2FMedicine%2Fwiki&logo=wikipedia&labelColor=2C3439&label=Wiki%20pages&search=Pages%5B%5Cs%5CS%5D%2A%3FCounter--primary%5B%5E%3E%5D%2A%3E(%5B0-9%5D%2B)%3C&replace=%241&cacheSeconds=86400)](https://github.com/apkd/Medicine/wiki)
[![Readme lines](https://img.shields.io/badge/dynamic/regex?url=https://api.codetabs.com/v1/loc/%3Fgithub%3Dapkd/Medicine%26branch%3Dmaster%26ignored%3D.github,Medicine.Runtime,Medicine.SourceGenerator,Medicine.SourceGenerator~,Medicine.Tests&label=README%20lines&labelColor=2C3439&search=%22language%22%5Cs%2A:%5Cs%2A%22Markdown%22%5B%5E%7D%5D%2A%22lines%22%5Cs%2A:%5Cs%2A%28%5B0-9%5D%2B%29&replace=%241&logo=markdown&cacheSeconds=86400)](https://github.com/apkd/Medicine/blob/master/README.md)

Access to the wiki is unrestricted, so go wild. I reserve the right to revert and refactor changes.

Pull requests updating the README file are also welcome.