# See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images faster for debugging.

# Base runtime image
FROM mcr.microsoft.com/dotnet/runtime:10.0-alpine AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS publish
WORKDIR /src

# Copy entire repo to ensure custom MSBuild imports (msbuildLib) are available
# .dockerignore causes not all the files are copied.
COPY . .

# Build and publish
RUN dotnet build "RmqToRestApiForwarder.sln" -c Release
RUN dotnet publish "Projects/RmqToRestApiForwarder/RmqToRestApiForwarder.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Final image
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

ENTRYPOINT ["dotnet", "RmqToRestApiForwarder.dll"]
