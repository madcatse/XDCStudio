# Publishing this fork

The local repository starts from `MobyGamer/XDC` commit `56a5dc1951e1f0b8747fe8fa4614c607e2f91c8b` and retains all 26 upstream commits. `upstream` points to the original project. The Windows changes are prepared locally; no GitHub repository or release has been created by this preparation step.

## Create the GitHub relationship

Use **Fork** on [MobyGamer/XDC](https://github.com/MobyGamer/XDC), select your account and keep the default branch. Creating an unrelated empty repository does not establish the GitHub fork relationship. See [GitHub's fork instructions](https://docs.github.com/en/pull-requests/how-tos/work-with-forks/fork-a-repo).

After the fork exists, connect this working directory to it. Replace `YOUR-ACCOUNT` with its actual owner:

```powershell
git remote add origin https://github.com/YOUR-ACCOUNT/XDC.git
git fetch origin
git status
git diff --cached --stat
```

Configure your real Git author name and verified or GitHub-provided noreply email locally if needed, review the staged changes, then commit and push:

```powershell
git commit -m "Add XDC Studio native Windows converter and preview player"
git push -u origin master
```

Do not force-push. If the fork already has independent changes, reconcile them before pushing. Keep `upstream` for the original repository and `origin` for your fork. The preparatory work intentionally does not guess an account, email or Git author identity.

## Build and release

Run the build and tests described in [BUILDING.txt](../BUILDING.txt). GitHub Actions is configured to run on pushes, pull requests and manual dispatch, with read-only repository permissions. It produces CI artifacts without creating releases or tags automatically.

For a reviewed release, prepare the current portable ZIP, source ZIP, SHA-256 file and notes from [CHANGELOG.md](../CHANGELOG.md). Upload only those reviewed artifacts, not the entire workspace. Keep dependency licences and the exact corresponding-source information in [DEPENDENCIES.md](DEPENDENCIES.md) with redistributed binaries. The application icon has separate provenance in [assets/README.md](../assets/README.md).

`local/`, `.tools/`, `downloads/`, `dist/`, generated media, build caches and private test results are excluded from Git. The source-packaging script also selects explicit source directories. Reference reports in `docs/validation/` have local absolute paths replaced with placeholders.
