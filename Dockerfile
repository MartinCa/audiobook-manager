FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine@sha256:620e765fe18186c08399f7aa978f79f04b6bbf0ee1b3b8a91e2d5c9619e59da1 AS build-env
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
FROM node:24-alpine@sha256:e67514e5d0f6c46656005e1b693b2ec9d52e80b641307de684d4a015ba7a4eaf AS build-node
WORKDIR /app

ARG APP_VERSION=dev
ARG COMMIT_HASH=""
ENV VITE_APP_VERSION=$APP_VERSION
ENV VITE_COMMIT_HASH=$COMMIT_HASH
ENV CI=true

RUN corepack enable

COPY /client/package.json /client/pnpm-lock.yaml /client/pnpm-workspace.yaml ./

RUN pnpm install --frozen-lockfile

COPY /client ./

RUN pnpm run build

# Build runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine@sha256:c4b29bf368004ad9076c1ab9bc91fb373561e3905b4345637e14e8b8c57e3be8

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
