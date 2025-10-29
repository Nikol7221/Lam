
# ===== ОСНОВА =====
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

# ===== СБОРКА =====
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Копируем ВСЁ
COPY . .

# Восстановление и публикация
RUN dotnet restore Lampac/Lampac.csproj
RUN dotnet publish Lampac/Lampac.csproj -c Release -o /app/publish --no-restore

# ===== ФИНАЛ =====
FROM base AS final
WORKDIR /app

# Копируем опубликованное приложение
COPY --from=build /app/publish .

# Копируем только то, что точно есть
RUN mkdir -p wwwroot lpc && \
    curl -fSL -o cloudflare.zip https://lampac.sh/update/cloudflare.zip && \
    unzip -o cloudflare.zip -d . && \
    rm cloudflare.zip

# Устанавливаем переменную окружения
ENV ASPNETCORE_URLS=http://0.0.0.0:8080

ENTRYPOINT ["dotnet", "Lampac.dll"]
