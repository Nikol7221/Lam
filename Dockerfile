
# ===== ОСНОВА =====
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://0.0.0.0:8080

# ===== СБОРКА =====
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY . .

# Установка unzip
RUN apt-get update && apt-get install -y unzip curl && rm -rf /var/lib/apt/lists/*

# Восстановление и публикация
RUN dotnet restore Lampac/Lampac.csproj
RUN dotnet publish Lampac/Lampac.csproj -c Release -o /app/publish --no-restore

# ===== ФИНАЛ =====
FROM base AS final
WORKDIR /app

# Копируем приложение
COPY --from=build /app/publish .

# Создаём папки
RUN mkdir -p wwwroot lpc

# Скачиваем и распаковываем cloudflare.zip
RUN curl -fSL -o cloudflare.zip https://lampac.sh/update/cloudflare.zip && \
    unzip -q cloudflare.zip -d . && \
    rm cloudflare.zip || echo "Failed to download cloudflare.zip"

ENTRYPOINT ["dotnet", "Lampac.dll"]

