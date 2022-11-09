FROM mcr.microsoft.com/dotnet/sdk:6.0-alpine AS build-env
WORKDIR /app

# Copy everything
COPY ./AudiobookManager ./
# Restore as distinct layers
RUN dotnet restore
# Build and publish a release
RUN dotnet publish -c Release -o out

# Build client app
FROM node:18-alpine as build-node
WORKDIR /app
COPY /client/package*.json ./

RUN npm install

COPY /client ./

RUN npm run build

# Build runtime image
FROM mcr.microsoft.com/dotnet/aspnet:6.0-alpine

# User manipulation tools
RUN apk add --no-cache --update --upgrade shadow

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
