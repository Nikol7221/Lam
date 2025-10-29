

# ===== ОСНОВА =====
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

# ===== СБОРКА =====
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY . .

RUN dotnet restore Lampac/Lampac.csproj
RUN dotnet publish Lampac/Lampac.csproj -c Release -o /app/publish --no-restore

# ===== ФИНАЛ =====
FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .

RUN mkdir -p wwwroot lpc
RUN echo '<h1>Lampac запущен! Веб-интерфейс в разработке.</h1><p>API работает: <a href="/api/online">/api/online</a></p>' > wwwroot/index.html

# Динамический порт
ENTRYPOINT ["dotnet", "Lampac.dll"]
