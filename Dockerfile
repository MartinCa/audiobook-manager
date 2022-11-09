FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build-env
WORKDIR /app

# Copy everything
COPY ./AudiobookManager ./
# Restore as distinct layers
RUN dotnet restore
# Build and publish a release
RUN dotnet publish -c Release -o out

# Build client app
FROM node:18 as build-node
WORKDIR /app
COPY /client/package*.json ./

RUN npm install

COPY /client ./

RUN npm run build

# Build runtime image
FROM mcr.microsoft.com/dotnet/aspnet:6.0
WORKDIR /app
COPY --from=build-env /app/out .
COPY --from=build-node /app/dist ./wwwroot

COPY ./dockerscripts/. ./

# Add group to use for the user
RUN addgroup xyzgroup --gid 911

# Add user to run the app as
RUN useradd --uid 911 -d /app -g xyzgroup appuser

# Make the user the owner of the app dir
RUN chown -R appuser:xyzgroup /app

RUN chmod +x ./run.sh

#ENTRYPOINT ["dotnet", "AudiobookManager.Api.dll"]
ENTRYPOINT [ "./run.sh" ]
