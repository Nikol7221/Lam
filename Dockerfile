

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

# Создаём папки
RUN mkdir -p wwwroot lpc

# Создаём заглушку index.html
RUN echo '<h1>Lampac запущен! Веб-интерфейс в разработке.</h1><p>API работает: <a href="/api/online">/api/online</a></p>' > wwwroot/index.html

ENTRYPOINT ["dotnet", "Lampac.dll"]
