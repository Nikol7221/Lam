

# Скачивание cloudflare.zip (без падения)
RUN curl -fsSL https://lampac.sh/update/cloudflare.zip -o cloudflare.zip || \
    (echo "cloudflare.zip not found, skipping" && touch cloudflare.zip)

# Распаковка (если файл есть)
RUN unzip -q cloudflare.zip -d . 2>/dev/null || echo "No files to unzip"

# Удаляем zip
RUN rm -f cloudflare.zip

# Заглушка index.html
RUN echo '<h1>Lampac работает!</h1><p>API: <a href="/api/online">/api/online</a></p>' > wwwroot/index.html
