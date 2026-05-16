import argparse
import json
import sys
from typing import Any

from dotenv import load_dotenv

from cc0_content import CC0Client


# .env example: CC0_CONTENT_API_KEY=exampleKey
load_dotenv()


def _clean_text(value: Any, max_len: int = 520) -> str:
    if value is None:
        return ""
    text = " ".join(str(value).split())
    if len(text) <= max_len:
        return text
    return text[: max_len - 1].rstrip() + "..."


def _pick(mapping: dict[str, Any], *keys: str) -> Any:
    for key in keys:
        value = mapping.get(key)
        if value not in (None, ""):
            return value
    return None


def _metadata(chunk: dict[str, Any]) -> dict[str, Any]:
    metadata = chunk.get("metadata")
    return metadata if isinstance(metadata, dict) else {}


def _normalize_chunk(chunk: Any, index: int) -> dict[str, str]:
    if not isinstance(chunk, dict):
        return {
            "title": f"RAG result {index + 1}",
            "url": "",
            "snippet": _clean_text(chunk),
            "source": "Neurvance RAG",
        }

    metadata = _metadata(chunk)
    title = _pick(
        chunk,
        "title",
        "name",
        "document_title",
        "source_title",
    ) or _pick(metadata, "title", "name", "document_title", "source_title")

    url = _pick(
        chunk,
        "url",
        "link",
        "source_url",
        "document_url",
        "page_url",
        "uri",
    ) or _pick(metadata, "url", "link", "source_url", "document_url", "page_url", "uri")

    snippet = _pick(
        chunk,
        "snippet",
        "text",
        "content",
        "chunk",
        "body",
        "summary",
    ) or _pick(metadata, "snippet", "text", "content", "summary", "description")

    source = _pick(chunk, "source", "source_name", "provider", "collection") or _pick(
        metadata, "source", "source_name", "provider", "collection"
    )

    return {
        "title": _clean_text(title or source or url or f"RAG result {index + 1}", 120),
        "url": _clean_text(url, 700),
        "snippet": _clean_text(snippet),
        "source": _clean_text(source or "Neurvance RAG", 120),
    }


def search(query: str) -> dict[str, Any]:
    with CC0Client() as client:
        response = client.search(query)

    chunks = response.get("chunks", [])
    if not isinstance(chunks, list):
        chunks = []

    return {
        "ok": True,
        "query": response.get("query", query),
        "total_results": response.get("total_results", len(chunks)),
        "processing_time_ms": response.get("processing_time_ms", 0),
        "results": [_normalize_chunk(chunk, index) for index, chunk in enumerate(chunks)],
    }


def _write_json(payload: dict[str, Any]) -> None:
    print(json.dumps(payload, ensure_ascii=False))


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Search the Neurvance RAG system.")
    parser.add_argument("query", nargs="*", help="Question or topic to search for.")
    parser.add_argument("--json", action="store_true", help="Return normalized JSON.")
    args = parser.parse_args(argv)

    query = " ".join(args.query).strip()
    if not query:
        payload = {"ok": False, "query": "", "error": "Please enter a question to search."}
        if args.json:
            _write_json(payload)
        else:
            print(payload["error"], file=sys.stderr)
        return 2

    try:
        payload = search(query)
    except Exception as exc:
        payload = {"ok": False, "query": query, "error": str(exc)}
        if args.json:
            _write_json(payload)
        else:
            print(payload["error"], file=sys.stderr)
        return 1

    if args.json:
        _write_json(payload)
    else:
        for result in payload["results"]:
            print(f'{result["title"]}\n{result["url"]}\n{result["snippet"]}\n')
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
