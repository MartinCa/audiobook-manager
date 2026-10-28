FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine@sha256:cc02b814237a76c4735fb6eeee87181597b5e56f2ce458a435e527674d91f4d7 AS build-env
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
FROM docker.io/library/node:24-alpine@sha256:06d7adf3e0ef61a13f1d28682726366f97a15aa1888c934d3ecec86c33918b53 AS build-node
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
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine@sha256:254fe017495996d3abcc487ffacdcce2b73affb380d3af87af12c8dff41fe0b0

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
