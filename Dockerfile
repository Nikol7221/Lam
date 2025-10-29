
# ===== ОСНОВА =====
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://0.0.0.0:8080

# ===== СБОРКА =====
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY . .

# Восстановление и публикация
RUN dotnet restore Lampac/Lampac.csproj
RUN dotnet publish Lampac/Lampac.csproj -c Release -o /app/publish --no-restore

# ===== ФИНАЛ =====
FROM base AS final
WORKDIR /app

# Копируем приложение
COPY --from=build /app/publish .

# Создаём минимальные папки (чтобы не падало)
RUN mkdir -p wwwroot lpc

# Копируем init.conf, если есть
COPY ./init.conf ./init.conf || echo "init.conf not found"

# Создаём заглушку index.html
RUN echo '<h1>Lampac работает! Веб-интерфейс в разработке.</h1>' > wwwroot/index.html

ENTRYPOINT ["dotnet", "Lampac.dll"]

