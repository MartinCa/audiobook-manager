FROM mcr.microsoft.com/dotnet/sdk:11.0-alpine@sha256:f2542b156e7c9eda7783e4a5745efa6c133a162a1940fd4175849aa80fccfd23 AS build-env
WORKDIR /app

ARG APP_VERSION=dev
ARG COMMIT_HASH=""

# Copy everything
COPY ./AudiobookManager ./
# Restore as distinct layers
RUN dotnet restore
# Build and publish a release
RUN dotnet publish AudiobookManager.Api/AudiobookManager.Api.csproj -c Release --no-restore -p:InformationalVersion="${APP_VERSION}${COMMIT_HASH:++$COMMIT_HASH}" -o out

# Build client app
FROM node:24-alpine@sha256:06d7adf3e0ef61a13f1d28682726366f97a15aa1888c934d3ecec86c33918b53 AS build-node
WORKDIR /app

ARG APP_VERSION=dev
ARG COMMIT_HASH=""
ENV VITE_APP_VERSION=$APP_VERSION
ENV VITE_COMMIT_HASH=$COMMIT_HASH
ENV CI=true

RUN corepack enable

COPY /client/package.json /client/pnpm-lock.yaml /client/pnpm-workspace.yaml ./

# --ignore-scripts: this stage only needs the packages on disk to run `pnpm
# build` below, never a package's own install script. Without it, lefthook's
# `prepare` script (`lefthook install`) shells out to `git rev-parse` to find
# the repo root - which fails outright here, since this image has no `git`
# binary and the build context never copies `.git` in the first place.
RUN pnpm install --frozen-lockfile --ignore-scripts

COPY /client ./

RUN pnpm run build

# Build runtime image
FROM mcr.microsoft.com/dotnet/aspnet:11.0-alpine@sha256:77256017a4f675e1cf66a4e910e115608a73a044b50ee07c3d042c8a71132da5

# User manipulation tools
RUN apk add --no-cache --update --upgrade shadow su-exec

# Environment
ENV PUID=""
ENV PGID=""
# Left empty so run.sh's own defaults (022 / 0750) are the single place they are stated.
ENV UMASK=""
ENV CONFIG_CHMOD=""
ENV AudiobookImportPath="/input"
ENV AudiobookLibraryPath="/library"
ENV DbLocation="/config/audiobookmanager.db"

WORKDIR /app
COPY --from=build-env /app/out .
COPY --from=build-node /app/dist ./wwwroot

COPY ./dockerscripts/. ./

RUN addgroup appgroup -g 911 && adduser -D -u 911 -h /config -G appgroup appuser

# Make the user the owner of the app dir
RUN chown -R appuser:appgroup /app

RUN chmod +x ./run.sh

#ENTRYPOINT ["dotnet", "AudiobookManager.Api.dll"]
ENTRYPOINT [ "./run.sh" ]
