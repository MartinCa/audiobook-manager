# Audiobook Manager

I created this application to make it easier for me to organize audiobooks to be served by applications such as [Audiobookshelf](https://www.audiobookshelf.org/).

## Audiobookshelf compatibility

This project's directory structure, embedded m4b tag conventions, and sidecar files (`desc.txt`, `reader.txt`, `metadata.opf`, cover images) are meant to track what Audiobookshelf expects. The authoritative references are:

- [Directory structure](https://audiobookshelf.org/docs/documentation/libraries/book-library/directory-structure)
- [Book metadata](https://audiobookshelf.org/docs/documentation/libraries/book-library/book-metadata)
- [Series management](https://audiobookshelf.org/docs/documentation/libraries/book-library/series-management)
- [GitHub mirror](https://github.com/audiobookshelf/audiobookshelf-docs/tree/master/docs/documentation/libraries/book-library) of the same docs, useful if the docs site above is unreachable

## Use of the application

This application is made to be used as a docker container. It is intended to read audiobooks in m4b file format from a directory and organize them in a library in another directory.

The docker image reads a few environment variables that can be used to configure how the application works.

| Environment Variable | Default                     | Description                                               |
| -------------------- | --------------------------- | --------------------------------------------------------- |
| PUID                 | 911                         | User id of the user used to run the application           |
| PGID                 | 911                         | Group id of the user used to run the application          |
| UMASK                | 022                         | Permissions mask for everything the application writes    |
| CONFIG_CHMOD         | 0750                        | Permissions applied to `/config` and the database in it   |
| AudiobookImportPath  | /input                      | Directory which is scanned for audiobooks to be organized |
| AudiobookLibraryPath | /library                    | Directory which is used as the root of the library        |
| DbLocation           | /config/audiobookmanager.db | Location of the SQLLite database file                     |

The paths specified as environment variables should be mounted to the host system.

### File permissions

Everything the application writes — relocated `.m4b` files, `desc.txt`, `reader.txt`,
`metadata.opf`, cover images — lands on volumes shared with the host, so `UMASK` decides who else
on the host can write to your library.

The default `022` gives the owner write access and everyone else read access. `PUID`/`PGID` and the
`chown` at startup are what make the application able to write there regardless of the ids the host
uses, so the mask does not need to be permissive for that to work.

If another container genuinely needs to **write** into the library — a tagger, a downloader — give
it the same `PGID` and set `UMASK=002`, which makes new files group-writable. Setting `UMASK=000`
restores the fully world-writable behaviour that earlier versions used unconditionally.

`CONFIG_CHMOD` covers `/config`, which holds the SQLite database and its journal; nothing but this
application is expected to open those. It is applied recursively to the directory as well as the
files in it, so it must keep the owner's execute bit — `0640` would make `/config` untraversable
and the application would not start. Both values are validated at startup and a bad one is a
startup failure naming the setting, rather than a container that comes up with the wrong
permissions.

## Development

### DB Migration

In order to create a new Migration use the Package Manager Console.

Startup project: AudiobookManager.Api

Default project: AudiobookManager.Database

Command: `Add-Migration <migration name>`

Migrations are performed at runtime.

### Swagger / Redoc

Swagger is hosted at `http://localhost:5271/swagger/index.html`
