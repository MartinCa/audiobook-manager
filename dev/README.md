# Development utilities

## Seeding a development database

`dev/seed-db.py` inserts a couple of audiobooks (with authors), plus a
discovered (import) item, into the API's SQLite database so the library,
book detail, edit form, and organize views have something to render during
frontend development and manual testing.

1. Start the API once with development paths so it creates and migrates the
   schema:

   ```bash
   cd AudiobookManager/AudiobookManager.Api
   ASPNETCORE_ENVIRONMENT=Development \
     AudiobookImportPath=/tmp/abm-dev/untagged \
     AudiobookLibraryPath=/tmp/abm-dev/library \
     DbLocation=/tmp/abm-dev/abm-test.db \
     dotnet run
   ```

   (any writable directories work; stop the API again afterwards)

2. Seed the same database the API created:

   ```bash
   python3 dev/seed-db.py /tmp/abm-dev/abm-test.db
   ```

3. Start the API and the frontend dev server as usual and log in / browse:
   books appear under `/library`, the discovered item under
   `/library/discovered`.

`--fresh` deletes the database first; run the API once afterwards to
recreate the schema, then seed again. Re-running the script on an already
seeded database is a no-op.
