#!/usr/bin/env python3
"""Seed a development SQLite database with a few books/authors/discovered items.

Usage:
    python3 dev/seed-db.py [path-to-dev-db] [--fresh]

The database path should match DbLocation in the API's configuration. The API
creates the schema itself at startup (context.Database.Migrate()), so run the
API once before seeding - this script only inserts rows. --fresh deletes the
database file first (the API will recreate and migrate it on next start).

Requires the API to have run at least once against that database, and uses
only the Python standard library (sqlite3).
"""

import argparse
import pathlib
import sqlite3
import sys


def seed(db_path: pathlib.Path) -> None:
    db = sqlite3.connect(db_path)
    try:
        cur = db.cursor()
        books = [
            ("The Hobbit", 2020, "/data/library/Tolkien/The Hobbit.m4b", "Author 1"),
            ("Hollow Bones", 2026, "/data/library/Picoult/Hollow Bones.m4b", "Author 2"),
        ]
        for name, year, path, author in books:
            row = cur.execute(
                "SELECT id FROM audiobooks WHERE file_info_full_path = ?", (path,)
            ).fetchone()
            if row:
                print(f"skip (already seeded): {name}")
                continue
            cur.execute(
                """INSERT INTO audiobooks
                   (book_name, year, description, file_info_full_path,
                    file_info_file_name, file_info_size_in_bytes, language,
                    duration_in_seconds)
                   VALUES (?, ?, ?, ?, ?, ?, ?, ?)""",
                (name, year, "Seeded test description", path, f"{name}.m4b",
                 123_456, "en", 36_000),
            )
            book_id = cur.lastrowid
            cur.execute(
                "INSERT INTO persons (name, name_folded) VALUES (?, ?)",
                (author, author.lower()),
            )
            cur.execute(
                "INSERT INTO audiobooks_authors_persons (authors_id, books_authored_id) VALUES (?, ?)",
                (cur.lastrowid, book_id),
            )
            print(f"seeded: {name} (id {book_id})")

        discovered_path = "/data/process/audiobooks/untagged/Kvinder.m4b"
        if not cur.execute(
            "SELECT 1 FROM discovered_audiobooks WHERE file_info_full_path = ?",
            (discovered_path,),
        ).fetchone():
            cur.execute(
                """INSERT INTO discovered_audiobooks
                   (book_name, year, authors, narrators, file_info_full_path,
                    file_info_file_name, file_info_size_in_bytes, discovered_at,
                    language, duration_in_seconds)
                   VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)""",
                ("Kvinder i modstandskampen", 2026, "Some Author", "Some Narrator",
                 discovered_path, "Kvinder.m4b", 555_555,
                 "2026-09-18T00:00:00", "da", 40_000),
            )
            print("seeded: discovered item Kvinder i modstandskampen")
        db.commit()
    finally:
        db.close()


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "db",
        type=pathlib.Path,
        help="Path to the SQLite database (the API's DbLocation).",
    )
    parser.add_argument(
        "--fresh",
        action="store_true",
        help="Delete the database file first; the API recreates and migrates it on next start.",
    )
    args = parser.parse_args()
    if args.fresh and args.db.exists():
        args.db.unlink()
        print(f"deleted {args.db}")
    if not args.db.exists():
        print(
            f"{args.db} does not exist. Start the API once to create and migrate "
            "the schema, then re-run this script."
        )
        return 1
    seed(args.db)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
