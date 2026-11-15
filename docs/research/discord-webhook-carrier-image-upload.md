# Discord webhook carrier-image upload contract

Checked: 2026-11-14, America/Chicago. Source revision: `12357ea89ca7236b24208e9b2683f59ab1ddeaea`, plus local layout, outbound-size, and response-diagnostic changes described below. This combines primary-source research, a code audit, a temporary local image-encoding/request-serialization harness, and local HTTP-response tests. No real Discord webhook messages were sent, and no tester image, private settings, or real Discord response was available.

## Finding

**The inspected upload uses a documented multipart shape and supported image formats. The normal 1280×720 PNG crop is not an obvious size-limit violation.** The initial audit found that successful response bodies and most API error details were discarded. The local follow-up now checks Discord's returned title and embed image and records diagnostic metadata. The tester's original failure remains unproven until an affected live send is captured.

## Published requirements

| Area | Current primary-source evidence | Application consequence |
| --- | --- | --- |
| File bytes | The shared API reference states a default **20 MiB per file**, with potentially larger Nitro/Boost allowances. | Stay below the default; do not assume that a webhook inherits its creator's Nitro allowance. The reference does not promise that inheritance. [Uploading files](https://docs.discord.com/developers/reference#uploading-files) |
| Whole request | **25 MiB** is documented for Create Message. Execute Webhook links to shared upload instructions but does not restate a separate total. | Treat 25 MiB as a conservative message-request ceiling, rather than claiming a separately documented webhook-specific total. [Create Message limitations](https://docs.discord.com/developers/resources/message#create-message), [Execute Webhook](https://docs.discord.com/developers/resources/webhook#execute-webhook) |
| Embed image format | Attachment-backed embeds support `.jpg`, `.jpeg`, `.png`, `.webp`, and `.gif`. | PNG and JPEG are supported. A file's extension and media type must agree with its real encoding. [Attachments within embeds](https://docs.discord.com/developers/reference#uploading-files) |
| Multipart | Files use `multipart/form-data`, unique `files[n]` names, and a filename in Content-Disposition. Non-file fields may be grouped in `payload_json`; the example gives that part `application/json`. | Match the embed's `attachment://filename` exactly to the uploaded filename. [Upload example](https://docs.discord.com/developers/reference#uploading-files) |
| Attachment metadata | `attachments` is optional when executing a webhook; when supplied, new attachment IDs match `n` in `files[n]`. | Omitting the metadata array is not, by itself, a documented protocol violation. [Webhook parameter table](https://docs.discord.com/developers/resources/webhook#execute-webhook), [Attachment request structure](https://docs.discord.com/developers/resources/message#attachment-object) |
| Confirmation | `wait=true` waits for confirmation and returns the created message body. | Inspect the returned embed image when confirming an embedded upload; do not require a separate attachment-array entry. [Webhook query parameters](https://docs.discord.com/developers/resources/webhook#execute-webhook) |

These are current limits, not historical values: Discord's support FAQ dates the ordinary user upload increase from 10 MB to 20 MB to August 2026. That article concerns client uploads and is supporting context, not an independent webhook entitlement specification. [File Attachments FAQ](https://support.discord.com/hc/en-us/articles/25444343291031-File-Attachments-FAQ)

### Dimensions and display

No numerical attachment-image dimension ceiling was found in the inspected upload, webhook, or embed-image contracts. The reference's **16–4096** range describes a CDN `?size=` resize request for listed image assets; it is not stated as an attachment upload limit. Do not convert that into a claimed maximum input resolution. [CDN image formatting](https://docs.discord.com/developers/reference#image-formatting)

The embed image URL supports HTTP(S) and attachments. Its returned width/height describe media; clients cannot impose image dimensions by supplying those values on Create Message. [Embed image structure](https://docs.discord.com/developers/resources/message#embed-object), [Create Message limitations](https://docs.discord.com/developers/resources/message#create-message)

## What SrvSurvey actually sends

These are source observations, distinct from Discord's public contract:

- [`DiscordCarrierImage.LoadAsync`](../../src/SrvSurvey.Core/Discord/DiscordCarrierImage.cs) caps the **source** at 8,388,608 bytes (8 MiB), checks PNG/JPEG signatures, and normalizes upload names to `carrier.png` or `carrier.jpg` with matching MIME types. Without a saved crop, those bounded original bytes are uploaded. Signature recognition alone is not a complete decode validator.
- [`DiscordCarrierImageProcessor`](../../src/SrvSurvey.Desktop/Platform/DiscordCarrierImageProcessor.cs) applies a separate native-decode safety policy of 16,000,000 pixels and at most 16,384 pixels on either side. These are application limits, not limits attributed to Discord.
- A saved crop is decoded and drawn onto a fresh **1280×720 BGRA8888** bitmap, then encoded as PNG and named `carrier.png` / `image/png`. The output constants are in [`DiscordCarrierImageCrop`](../../src/SrvSurvey.Core/Discord/DiscordCarrierImageCrop.cs); rendering and encoding are in [`EncodeCrop`](../../src/SrvSurvey.Desktop/Platform/DiscordCarrierImageProcessor.cs). Raw output pixels occupy **3,686,400 bytes** (`1280 × 720 × 4`), plus any encoding overhead. This arithmetic describes the raw bitmap; the separate outbound guard below constrains encoded bytes.
- [`SendAnnouncementAsync`](../../src/SrvSurvey.Core/Discord/DiscordCarrierWebhookClient.cs) uses the selected personal/squadron image, sets `embeds[0].image.url` to `attachment://` plus that image's filename, and sends the matching bytes in the same request.
- The local fix now also checks the **outbound** image against the same **8 MiB** cap after loading/cropping and before constructing the multipart body. An oversized encoder result follows the existing text-only fallback. This closes the output-size validation gap found during this audit; it has not been shown to explain the tester's image loss. [Outbound guard](../../src/SrvSurvey.Core/Discord/DiscordCarrierWebhookClient.cs)
- [`CreateContent`](../../src/SrvSurvey.Core/Discord/DiscordCarrierWebhookClient.cs) uses .NET `MultipartFormDataContent` with JSON under `payload_json`, and `ByteArrayContent` under `files[0]` with the filename and MIME type. This matches the documented naming and reference mechanism. It does not send base64, a Windows filesystem path, a webhook avatar, or an externally hosted screenshot as the embed image.

The source-file and new outbound caps are below the documented default upload limit. The fixed crop resolution also gives substantial byte headroom, corroborated by the measurements below. Neither source inspection nor a successful local preview substitutes for examining the real submission and its response.

## Local byte-size and serialization measurements

The temporary harness at `/tmp/discord-upload-audit-20261114/Program.cs` exercised the real `DiscordCarrierImageProcessor.Crop` and `DiscordCarrierWebhookClient`, capturing and serializing the multipart content through a fake `HttpMessageHandler`. It used deterministic random RGBA pixels (seed **61743**, **1920×1080**) to make compression difficult. The PNG source was close to the application's 8 MiB source cap. A 256-character title, 4096-character description, and 1000-character route used Unicode characters to stress JSON escaping and the formatter's embed budget.

| Source and saved crop | Source bytes | Uploaded image bytes | Complete multipart body bytes | Uploaded dimensions/type |
| --- | ---: | ---: | ---: | --- |
| PNG, cropped | 8,310,238 | 3,627,640 | 3,657,666 | 1280×720, PNG |
| PNG, no crop | 8,310,238 | 8,310,238 | 8,340,264 | 1920×1080, PNG |
| JPEG, cropped | 2,910,929 | 2,895,409 | 2,925,435 | 1280×720, PNG |
| JPEG, no crop | 2,910,929 | 2,910,929 | 2,940,956 | 1920×1080, JPEG |

All four cases passed the harness assertions. It decoded the uploaded bytes, checked dimensions and encoding, checked `multipart/form-data` / `application/json` / image MIME types and Content-Disposition, and verified exact equality between the uploaded filename and the embed's attachment reference. The largest measured multipart body was about **7.95 MiB**, comfortably below both published numeric size thresholds above. The crop from the near-cap PNG source produced about **3.46 MiB** of PNG data.

Reproduction with the existing temporary harness and built Release assemblies, from the repository root:

```console
LD_LIBRARY_PATH="$PWD/src/SrvSurvey.Desktop/bin/Release/net10.0/runtimes/linux-x64/native" dotnet run --project /tmp/discord-upload-audit-20261114/Audit.csproj --configuration Release
```

The command rebuilds the temporary harness so its copied references reflect the current Release assemblies. The machine-local results are `/tmp/discord-upload-audit-20261114/request-summary.json`. The temporary harness is investigative material, not a committed project or a portable fixture. These four measurements are not a proof for every permitted image, and they do not use the tester's screenshot. The fake transport validates serialized body bytes, not HTTP socket framing, Discord acceptance, CDN processing, or client display.

### Transfer framing

The inspected Discord upload and Execute Webhook contracts do not state a Content-Length requirement or a prohibition on chunked request bodies. Searches of the official documentation repository found no demonstrated Execute Webhook chunked-multipart incompatibility. A related 2022 issue concerns **responding to incoming interaction delivery**, which is a different direction/endpoint; its reporter says a chunked POST to the interaction callback endpoint worked. It does not establish a failure of ordinary webhook execution. [Upload contract](https://docs.discord.com/developers/reference#uploading-files), [Execute Webhook](https://docs.discord.com/developers/resources/webhook#execute-webhook), [discord-api-docs issue #5250](https://github.com/discord/discord-api-docs/issues/5250)

Consequently, unknown multipart length or HTTP/1.1 chunked framing can be investigated at a real local socket, but cannot presently be called the cause based on the primary-source evidence gathered here. No real Discord transport test was performed.

## Implemented hardening and diagnostics

Discord lists JSON error codes **40005** (request too large), **50045** (file too large), **50046** (invalid file), and **50035** (invalid form body or Content-Type). These are API error codes, not HTTP status numbers; the inspected reference does not guarantee a particular HTTP status for every upload rejection. [Discord status/error reference](https://docs.discord.com/developers/topics/opcodes-and-status-codes#json-json-error-codes)

The three gaps found by the audit are addressed in the local changes:

1. **Outbound byte cap.** The [post-crop check](../../src/SrvSurvey.Core/Discord/DiscordCarrierWebhookClient.cs) enforces the 8 MiB cap on submitted bytes. An oversized encoder result follows the existing text-only fallback. Its regression test failed before the guard and passed afterward.
2. **Created-message confirmation.** [`ConfirmMessageAsync`](../../src/SrvSurvey.Core/Discord/DiscordCarrierWebhookClient.Diagnostics.cs) reads up to 64 KiB of the `wait=true` response. It compares the returned title with the submitted title, checks the returned embed image, and records the message ID and comparison results. A missing image, changed title, or unreadable confirmation produces a warning without repeating the accepted POST. The message schema excludes embed-referenced files from `attachments`, so an empty array is valid; checking the embed also allows Discord's CDN URL rewrite. [Message structure](https://docs.discord.com/developers/resources/message#message-object), [Embed image structure](https://docs.discord.com/developers/resources/message#embed-object), [Discord confirmation of attachment-array behavior](https://github.com/discord/discord-api-docs/issues/4809).
3. **Safe rejection details.** [`RejectionMessageAsync`](../../src/SrvSurvey.Core/Discord/DiscordCarrierWebhookClient.Diagnostics.cs) reads up to 16 KiB and captures numeric Discord error codes. Known size, invalid-file, and message-format codes have specific status messages. HTTP status and retry timing are logged without arbitrary response text.

Settings, template, and image fingerprints connect Apply, Save, queueing, preparation, and confirmation without logging message content, webhook credentials, image paths, or CDN URLs. See [delivery diagnostics](../discord-carrier-announcements.md#delivery-diagnostics) for the capture procedure. Diagnostic logging failures do not interrupt posting.

Local image-read/decode/crop exceptions already cause a text-only fallback, with a specific status telling the user the image was omitted. HTTP upload errors do **not** cause a second text-only retry. The custom title is rendered before image preparation and is retained by the local fallback. Thus the inspected implementation does not establish that an image-size rejection would also replace a custom title with the default title. [Image preparation and fallback](../../src/SrvSurvey.Core/Discord/DiscordCarrierWebhookClient.cs)

## Follow-up evidence

The local harness above establishes MIME type, multipart naming, filename/reference equality, decoded dimensions, and encoded/request byte sizes for its fixtures. HTTP-boundary tests also cover missing-image/changed-title confirmations, known upload rejections, unreadable and bounded response bodies, privacy, and logging failures. The desktop integration test checks the correlated trace through Apply, Save, restart, and the live journal monitor. These checks use fake HTTP responses and do not establish real Discord acceptance for the tester's image.

For the tester's actual failure, the useful remaining evidence is whether the client prepared an image, the bounded Discord response code, and whether Discord's confirmed message contains that image. None of the cited contracts establishes a silent fallback to an old/default title when an uploaded image is too large, and no such behavior was reproduced in this audit.

## Local validation

The first RC77.17 validation passed CSharpier, localization verification, the full SonarCloud-profile warnings-as-errors build, 3,785 Core tests, 171 selected Desktop tests, all 66 mandatory journal-upload cases, and 97.2% changed-production coverage (313/322 points). Validation output is in `/tmp/discord-response-diagnostics/rc17-quality.log`. The image harness also passed again with response capture enabled; its four byte measurements were unchanged.

Before implementation, three missing-image/changed-title response cases failed because the client reported ordinary success. Deliberate mutations then confirmed that tests detect lost error codes, an excessive response bound, raw-content logging, logger exceptions interrupting delivery, missing unreadable-confirmation warnings, and disconnected production log callbacks. All mutations were restored before the passing gate. These are local regression checks, not a capture of the tester's live Discord response.

A subsequent local Kilo review (`kilo/openai/gpt-6.1-sol`, `xhigh`) identified unpaired UTF-16 surrogates that `JsonDocument` accepts structurally but `GetString()` rejects with `InvalidOperationException`. The new exception handling treats them as unreadable confirmations. Three Core regression cases and a Desktop next-queued-announcement case failed before the fix and passed afterward; the final Kilo re-review reported no findings.

A follow-up regression also verified that revoking publishing during a diagnostic callback prevents the pending HTTP request. The pre-submission policy check now runs after diagnostic logging, immediately before `HttpClient.SendAsync`; the regression failed before that adjustment and passed afterward. The final full-branch Kilo review reported no findings.
