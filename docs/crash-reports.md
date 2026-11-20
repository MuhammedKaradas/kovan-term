# Crash reports

Kovan Term can send a report when it crashes, so the crash can be fixed. It's **off unless you agree**: release
builds ask once, and you can change it in Settings → Privacy. Builds from source never ask or send anything.

## What a report contains

Only what's listed here, from one crash:

| Field | Example | |
|---|---|---|
| `schema` | `1` | Format version |
| `installId` | `"1b2c3d4e-…"` | A random id made when you agreed, so reports from one install can be grouped. It isn't tied to you or your computer |
| `appVersion` | `"0.2.0"` | Kovan Term's version |
| `os` | `"Windows 10.0.26200.0"` | Windows version |
| `architecture` | `"X64"` | |
| `runtime` | `"10.0.11"` | .NET version |
| `time` | `"2026-11-21T07:00:00+00:00"` | When it crashed, in UTC |
| `source` | `"Dispatcher"` | Which handler caught it (`Dispatcher` or `AppDomain`) |
| `exception` | `{ "type", "message", "stack" }` | The error's type, message and stack trace |
| `inner` | `[{ "type", "message", "stack" }]` | The errors that caused it, up to 5 |

Before a report is written:

- **file paths are removed**: a stack frame keeps only the file name and line (`in MainWindow.xaml.cs:line 42`), and
  any other path in a message becomes `<path>` (with everything after it on that line);
- your **user name** becomes `<user>`, the **computer name** `<computer>`, and **e-mail addresses** `<email>`;
- messages are cut at 2,000 characters and stack traces at 16,000.

Nothing from your terminals, files, prompts or agents' conversations is ever part of a report.

## When it's sent

The report is written to `%APPDATA%\KovanTerm\crash-reports\` when the crash happens. The next time Kovan starts, it
sends what's waiting there and deletes each report once it's sent. If you're offline or the server has trouble, the
reports wait for the next start. At most 20 wait, and none for longer than 30 days. You can read or delete them there
at any time.

## For the server

Each report is one request:

```http
POST <endpoint>
Content-Type: application/json; charset=utf-8
User-Agent: KovanTerm/<version>

{"schema":1,"installId":"…","appVersion":"0.2.0","os":"Windows 10.0.26200.0","architecture":"X64","runtime":"10.0.11",
 "time":"2026-11-21T07:00:00+00:00","source":"Dispatcher",
 "exception":{"type":"System.InvalidOperationException","message":"…","stack":"…"},
 "inner":[{"type":"System.IO.FileNotFoundException","message":"Could not find file '<path>'.","stack":"…"}]}
```

- Answer **2xx** once it's stored: the report is deleted.
- Answer **4xx** (except 429) for a report you won't take, such as an unknown `schema`: it's deleted and not sent again.
- Answer **429** or **5xx** to have it sent again on a later start. Kovan stops sending for that start at the first
  such answer.
- The same report can arrive twice, for example when the answer is lost. Reports have no id, so treat
  (`installId`, `time`, `exception.type`) as the key if that matters.

The endpoint is set when building: `dotnet publish … -p:KovanCrashReportEndpoint=https://…` (HTTPS only). The release
workflow takes it from the repository variable `CRASH_REPORT_ENDPOINT`.
