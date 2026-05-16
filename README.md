# CC0 Browser

CC0 Browser is a Windows desktop browser for searching and viewing verified CC0 content through the Neurvance RAG API. The desktop app is built with WPF and WebView2, with a small Python helper that calls the CC0 Content API and normalizes results for the browser UI.

## Features

- Tabbed WebView2 browsing with back, forward, reload, home, and bookmark controls.
- Address bar that opens normal web URLs or searches Neurvance RAG when you enter a query.
- Built-in search results pages for CC0 content.
- Local history, bookmarks, and settings stored under `%LOCALAPPDATA%\NeurvanceBrowser`.
- Settings window for saving or loading a `CC0_CONTENT_API_KEY`.
- Python CLI helper for direct RAG searches.

## Requirements

- Windows
- .NET SDK that supports `net10.0-windows`
- Microsoft Edge WebView2 Runtime
- Python 3.10 or newer

## Setup

Install the Python dependencies:

```powershell
python -m pip install -r requirements.txt
```

Create a local `.env` file from the example:

```powershell
Copy-Item .env.example .env
```

Then edit `.env` and set your API key:

```env
CC0_CONTENT_API_KEY=your_key_here
```

The `.env` file is ignored by git. You can also set `CC0_CONTENT_API_KEY` in your shell environment, or save the key in the app's Settings window. The Python client also supports `CC0_CONTENT_BASE_URL` if you need to point at a different API host.

## Run The Desktop App

```powershell
dotnet run --project .\NeurvanceBrowser\NeurvanceBrowser.csproj
```

You can also use the command in `run.txt`.

When the app opens, type a URL to browse the web or type a search phrase to query the CC0 RAG API.

## Run A Search From The CLI

```powershell
python .\rag.py "public domain space photography"
```

For normalized JSON output:

```powershell
python .\rag.py --json "public domain space photography"
```

## Project Layout

```text
NeurvanceBrowser/      WPF desktop app and WebView2 browser UI
cc0_content.py         Python client for the CC0 Content API
rag.py                 CLI/search adapter used by the desktop app
requirements.txt       Python dependencies
.env.example           Example local API key configuration
run.txt                Quick desktop app run command
```

## Development Notes

- `bin/`, `obj/`, Python cache files, WebView2 runtime data, and `.env` files are intentionally ignored.
- The desktop app finds `rag.py` by walking upward from the application folder.
- Search requires a valid `CC0_CONTENT_API_KEY`; regular web browsing does not.

## License

Apache License 2.0 with Commons Clause License Condition v1.0. This allows
commercial use, but does not allow selling the software itself. See `LICENSE`.
