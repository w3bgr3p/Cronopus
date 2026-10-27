# Telegram release announcements

`notify-site.yml` rebuilds the site and independently sends a Telegram Rich
Message when a release is published. Both stable and prerelease publications
are announced. Drafts are rejected. Notes are taken from the GitHub Release
without translation or generated summaries.

## Setup

Repository Settings → Secrets and variables → Actions:

- Secret `TELEGRAM_BOT_TOKEN`: one bot token for both destinations.
- Variable `TELEGRAM_MODE`: `test` initially; change to `prod` when satisfied.
  An unset value also selects `test`. Unknown modes fail without sending.
- Optional variables `TELEGRAM_TEST_CHAT_ID`, `TELEGRAM_TEST_TOPIC_ID`,
  `TELEGRAM_PROD_CHAT_ID`, `TELEGRAM_PROD_TOPIC_ID` override the defaults.

| Mode | Default group | Default topic |
| --- | --- | --- |
| test | -1004208623621 | 1144 |
| prod | -1002225536706 | 332 |

Add the bot to both groups and allow it to send messages in their topics.
Do not put the token in source files, variables, release text, or workflow inputs.

## Test the appearance

Actions → notify-site → Run workflow:

1. `release_tag`: a published tag, or empty for the latest stable release.
2. `telegram_mode`: `test` (manual runs default to test even if the variable is prod).
3. `preview_only`: enable to prepare artifacts without sending; disable to send.

The run summary and `telegram-release-test` artifact contain the Markdown
preview and JSON payload. GitHub's preview is approximate; check the actual
Telegram client for native Rich Message appearance.
Manual runs do not rebuild the site. The release-triggered rebuild is unchanged.

After verifying the test topic, set repository variable `TELEGRAM_MODE` to
`prod` for future releases. A manual prod run sends immediately to the prod topic.

## Release formatting

The title is the release name (or product and tag). A redundant opening
`# Cronopus <version>` heading is removed. A short leading paragraph remains
visible. Level 1/2 sections and their contents are displayed directly; flat lists
appear directly without an added heading. Download sections (`Downloads`, `Download`, `Загрузки`,
`Скачать`, `Скачивание`) are omitted because downloads are available via buttons.
Markdown lists, links and inline formatting are retained.
Installer/portable buttons appear only for matching release assets, alongside
release and documentation links. Long sections are split into bounded messages;
code fences are closed and reopened across boundaries.

Prefer this release-note structure for a visible introduction and clear sections:

```markdown
Short description of this release.

## Added
- Change with **emphasis** and `code`.

## Fixed
- Another change.
```

No automatic send retries or persistent deduplication are implemented.
Rerunning a successful job sends another copy (useful for formatting tests).
On network timeout or partial failure, inspect the topic before rerunning:
earlier messages may already have arrived. Errors stop the job and redact tokens.

## Local verification

```powershell
rtk node --test .github/scripts/telegram-release.test.mjs
rtk node .github/scripts/telegram-release.mjs --release-json path/to/release.json
```

The JSON fixture uses the GitHub REST release schema (`tag_name`, `html_url`,
`body`, `assets[].browser_download_url`), not `gh release view` field names.
The preview command does not need a Telegram token and never sends messages.
Native API contract: https://core.telegram.org/bots/api#sendrichmessage.
