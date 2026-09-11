# Contribution information

## General workflow

1. Clone repo locally
2. Create a feature branch referencing the JIRA ticket and a short description (e.g. `SB-1234-add-foo-button`)
3. Develop and test locally
    - Ideally add new unit tests when possible
    - Make sure existing tests and linter pass, these will also be run in the CI pipeline
4. Open pull request against whatever release branch the ticket is for (e.g. `release/1.2.0.0`)
    - When opening the PR, **be sure to check the box saying to close the feature branch when it is merged**, this helps make sure we don't have a bunch of old branches we don't need clogging up the repo
5. Wait for approvals from code review, historically we've waited for 2 approvals
6. Merge feature branch into the release branch

After this process the QA team validates tickets against the release branch.

### Commit guidelines

On top of the common commit guidelines, we want to include Jira ticket numbers in commit messages so they are linked in the ticket. This git hook prepends commit messages with ticket numbers whenever the branch name has a ticket number. This is a nice "set it and forget it" solution to make sure all your commits get linked

> E.g. if you run `git commit -m "Fix typo"` on a branch `SB-123-typo-bug` then the commit will end up being `SB-123: Fix typo`

After cloing the repository, please run the following command to set the hook path to the new .githooks directory:

```
git config core.hooksPath .githooks
```

## Guidelines

- Keep in mind that code is read much more than it's written so readability is always a top concern - from documentation to syntax and indentation
    - If you find a problematic area feel free to leave behind a `FIXME` or `TODO` comment as a warning for the next person or a reminder for yourself
    - One caveat is that readability in the PR is also important, so it might not be worth fixing the indentation in a file if it's going to make it hard to see a logic change in the same file
- Avoid anything client-specific in the codebase, both client names and terminology that is specific to a client
    - We have a fair amount of terms that many clients have different names for, so it's important to try to consistently use our terminology and not get it mixed up with theirs
- Code is occasionally seen by clients so avoid names and comments that they might find offensive

## Tips

- `git blame` is your friend for finding someone to help you out, we've had a lot of different people touching the code over time, and often they might not work here anymore, but if you look for a recent change somewhere in the file that person should be able to point you in the right direction
- Sync with the release branch frequently to keep merge conflicts as simple as possible and catch any compatability bugs early. Either a merge commit or a rebase works - whatever your preference is

### VSCode Setup

> Everyone is free to use whatever tools they enjoy most for working with the
> codebase, so this isn't a requirement, just some useful settings that can be
> adopted for local development if they seem helpful

This repo has a `.code-workspace` file that can be opened as a workspace in
VSCode for a few benefits. The workspace include a list of recommended
extensions to install for language support/tooling, as well as some settings
that will aid in development. This is primarily for frontend development as the
tooling in normal Visual Studio is more lacking there, but can also be used for
working in any area of the codebase if you like that editor.

The settings are in `.vscode/settings.json` and VSCode will give tooltips to
explain what they all do, but the biggest ones to be concerned with are
`files.exclude`, which hides files/folders from the file explorer sidebar, and
`search.exclude`, which hides things from search results. You may occasionally
want to change these settings, but these clear up the clutter to help find the
important parts of the codebase.

Any of these workspace settings can be overridden by user settings if desired.
