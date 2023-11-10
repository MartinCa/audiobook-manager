FROM mcr.microsoft.com/dotnet/sdk:7.0-alpine@sha256:4c2ed06d729b26c96c3b0e64c98356058c1ad342c54b08238434f0ef05d70bfa AS build-env
WORKDIR /app

# Copy everything
COPY ./AudiobookManager ./
# Restore as distinct layers
RUN dotnet restore
# Build and publish a release
RUN dotnet publish -c Release -o out

# Build client app
FROM node:21-alpine@sha256:df76a9449df49785f89d517764012e3396b063ba3e746e8d88f36e9f332b1864 as build-node
WORKDIR /app
COPY /client/package*.json ./

RUN npm install

COPY /client ./

RUN npm run build

# Build runtime image
FROM mcr.microsoft.com/dotnet/aspnet:7.0-alpine@sha256:cfb5c365b3dc1a6d6b2635507817025f739748c84b31c81734c148b6cac04100

# User manipulation tools
RUN apk add --no-cache --update --upgrade shadow

# Environment
ENV PUID=""
ENV PGID=""
ENV AudiobookImportPath="/input"
ENV AudiobookLibraryPath ="/library"
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
