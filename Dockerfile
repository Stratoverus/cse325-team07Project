# Multi-stage build for ASP.NET Core on Render
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project file and restore dependencies first for better layer caching
COPY TaskDone.csproj ./
RUN dotnet restore TaskDone.csproj

# Copy the remaining source and publish
COPY . .
RUN dotnet publish TaskDone.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production

# Render provides PORT at runtime; default to 10000 for local Docker runs.
ENTRYPOINT ["sh", "-c", "dotnet TaskDone.dll --urls http://0.0.0.0:${PORT:-10000}"]
