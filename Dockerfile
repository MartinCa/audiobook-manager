FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine@sha256:620e765fe18186c08399f7aa978f79f04b6bbf0ee1b3b8a91e2d5c9619e59da1 AS build-env
WORKDIR /app

# Copy everything
COPY ./AudiobookManager ./
# Restore as distinct layers
RUN dotnet restore
# Build and publish a release
RUN dotnet publish -c Release -o out

# Build client app
FROM node:24-alpine@sha256:d32cdf619f63fe0471182d08996dd516c6275bb5fd31ae06e55a570bd9e1ad43 AS build-node
WORKDIR /app

RUN corepack enable

COPY /client/package.json /client/pnpm-lock.yaml /client/pnpm-workspace.yaml ./

RUN pnpm install --frozen-lockfile

COPY /client ./

RUN pnpm run build

# Build runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine@sha256:c4b29bf368004ad9076c1ab9bc91fb373561e3905b4345637e14e8b8c57e3be8

# User manipulation tools
RUN apk add --no-cache --update --upgrade shadow

# Environment
ENV PUID=""
ENV PGID=""
ENV AudiobookImportPath="/input"
ENV AudiobookLibraryPath="/library"
ENV DbLocation="/config/audiobookmanager.db"

WORKDIR /app
COPY --from=build-env /app/out .
COPY --from=build-node /app/dist ./wwwroot

COPY ./dockerscripts/. ./

RUN addgroup appgroup -g 911
RUN adduser -D -u 911 -h /app -G appgroup appuser

# Make the user the owner of the app dir
RUN chown -R appuser:appgroup /app

RUN chmod +x ./run.sh

#ENTRYPOINT ["dotnet", "AudiobookManager.Api.dll"]
ENTRYPOINT [ "./run.sh" ]
