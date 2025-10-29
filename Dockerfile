

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

# Установка curl и unzip
RUN apt-get update && apt-get install -y curl unzip && rm -rf /var/lib/apt/lists/*

# Создание папок
RUN mkdir -p wwwroot lpc

# Скачивание и распаковка cloudflare.zip
RUN curl -fSL -o cloudflare.zip https://lampac.sh/update/cloudflare.zip && \
    unzip -q cloudflare.zip -d . && \
    rm cloudflare.zip || echo "cloudflare.zip not available"

# Заглушка index.html
RUN echo '<h1>Lampac работает!</h1><p>API: <a href="/api/online">/api/online</a></p>' > wwwroot/index.html

ENTRYPOINT ["dotnet", "Lampac.dll"]
