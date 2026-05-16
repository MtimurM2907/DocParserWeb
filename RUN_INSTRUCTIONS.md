# 📖 Инструкция по запуску DocParseLab

Общее описание проекта, роли, API и жизненный цикл документа — в [README.md](./README.md).

## 📋 Требования

### Минимальные требования

| Компонент | Версия | Ссылка |
|-----------|--------|--------|
| **.NET Runtime** | 8.0+ | [Скачать](https://dotnet.microsoft.com/download/dotnet/8.0) |
| **PostgreSQL** | 12+ | [Скачать](https://www.postgresql.org/download/) |
| **Браузер** | Любой современный | Chrome, Firefox, Edge |

### Для разработки (опционально)

| Компонент | Версия | Ссылка |
|-----------|--------|--------|
| **.NET SDK** | 8.0+ | [Скачать](https://dotnet.microsoft.com/download/dotnet/8.0) |
| **Node.js** | 20+ | [Скачать](https://nodejs.org/) |

---

## Что запускается

- **Сервер**: ASP.NET Core (`DocParseLab.Server`) — API и статический фронтенд из `DocParseLab.Server/wwwroot`.
- **Клиент**: Vite/React (`docparselab.client`) — собирается в `wwwroot` командой `npm run build`.
- **База данных**: PostgreSQL; миграции EF применяются при старте (или вручную: `dotnet ef database update`).

Поддерживаемые загрузки: **PDF** и **DOCX**.

---

## Быстрый старт (Windows)

### Шаг 1: Установка .NET Runtime

1. Перейдите на https://dotnet.microsoft.com/download/dotnet/8.0
2. Скачайте **ASP.NET Core Runtime 8.0** (Windows)
3. Запустите установщик и следуйте инструкциям

### Шаг 2: Установка PostgreSQL

1. Перейдите на https://www.postgresql.org/download/windows/
2. Скачайте и установите PostgreSQL
3. Запомните пароль, установленный для пользователя `postgres`

### Шаг 3: Создание базы данных

Откройте **pgAdmin** или командную строку PostgreSQL и выполните:

```sql
CREATE DATABASE pdf_parser_db;
```

### Шаг 4: Настройка приложения

1. Распакуйте архив с приложением в папку
2. Откройте файл `DocParseLab.Server/appsettings.json` (или `appsettings.Production.json`, если используете Production)
3. Измените строку подключения к PostgreSQL:

```json
"ConnectionStrings": {
  "DefaultConnection": "Host=localhost;Port=5432;Database=pdf_parser_db;Username=postgres;Password=ВАШ_ПАРОЛЬ"
}
```

4. При необходимости измените другие настройки (GigaChat, JWT ключ)

#### Настройки GigaChat (опционально)

AI‑описание и AI‑переписывание используют настройки секции `GigaChat` в `appsettings.json`.
Если не настроить — приложение всё равно запустится, но AI‑функции могут возвращать ошибку.

#### Настройки SMTP для отправки документов на email

Функция **«Отправить на email»** требует заполненной секции `Smtp` в `DocParseLab.Server/appsettings.json`:

```json
"Smtp": {
  "Host": "smtp.gmail.com",
  "Port": 587,
  "EnableSsl": true,
  "Username": "your_email@gmail.com",
  "Password": "APP_PASSWORD",
  "FromEmail": "your_email@gmail.com",
  "FromName": "DocParseLab"
}
```

Для Gmail нужен **пароль приложения** (не обычный пароль аккаунта):
1. Включить 2FA в Google-аккаунте.
2. Создать App Password.
3. Вставить его в `Smtp:Password`.

Альтернатива: можно задать через переменные окружения:
- `Smtp__Host`
- `Smtp__Port`
- `Smtp__EnableSsl`
- `Smtp__Username`
- `Smtp__Password`
- `Smtp__FromEmail`
- `Smtp__FromName`

#### OCR для сканов (Tesseract)

Если PDF является сканом (страницы — изображения без текстового слоя), приложение может включать OCR через **Tesseract**.

1. Скачайте `rus.traineddata` и `eng.traineddata` (или нужные языки) из репозитория tessdata, например `tessdata_fast`.
2. Положите файлы в папку:
   - `DocParseLab.Server/Resources/Tessdata/`
3. Убедитесь, что в `DocParseLab.Server/appsettings.json` секция `Ocr` указывает на этот путь:

```json
"Ocr": {
  "Enabled": true,
  "Languages": "rus+eng",
  "TessdataPath": "Resources/Tessdata"
}
```

После этого при парсинге PDF без текстового слоя будет использоваться OCR.

### Шаг 5: Запуск приложения

#### Вариант A (рекомендуется для разработки): собрать клиент → запустить сервер

Откройте PowerShell в корне проекта и выполните:

```powershell
cd .\docparselab.client
npm install
npm run build
cd ..

cd .\DocParseLab.Server
dotnet run
```

После сборки фронтенда сервер будет раздавать актуальный UI из `wwwroot`.

#### Вариант B: прямой запуск готового exe (Production)

Откройте командную строку в папке с приложением и выполните:

```bat
set ASPNETCORE_ENVIRONMENT=Production
set ASPNETCORE_URLS=http://+:5000
DocParseLab_Server.exe
```

**Вариант B (PowerShell)**

```powershell
$env:ASPNETCORE_ENVIRONMENT="Production"
$env:ASPNETCORE_URLS="http://+:5000"
.\DocParseLab_Server.exe
```

### Шаг 5.1 (если нужно): применение миграций вручную

Обычно миграции применяются при старте автоматически. Если нужно вручную:

```powershell
cd .\DocParseLab.Server
dotnet ef database update
```

### Шаг 6: Проверка работы

Откройте в браузере:
- **Приложение:** http://localhost:5000
- **Swagger API:** http://localhost:5000/swagger

---

## Первый вход и пользователи

1. Откройте http://localhost:5000 — если в базе **нет пользователей**, отобразится форма **создания администратора** (bootstrap).
2. Укажите email и пароль. После этого вы войдёте в систему с ролью **Admin**.
3. Дальнейших пользователей создаёт только администратор: раздел **«Пользователи»** в интерфейсе или `POST /api/auth/users` с JWT.
4. Публичной регистрации нет — без входа парсинг и работа с документами недоступны.

**JWT:** после входа токен хранится в браузере (`localStorage`) и передаётся в заголовке:

```http
Authorization: Bearer <ваш_токен>
```

Срок жизни токена задаётся в `Jwt:ExpirationMinutes` (по умолчанию 720 минут).

---

## Работа с документами (кратко)

| Действие | Где в UI |
|----------|----------|
| Загрузка PDF/DOCX | Главная → загрузка файла |
| Реестр | «Реестр документов» |
| Согласование | Боковая панель «Согласование» в карточке документа |
| Подпись | Панель «Цифровая подпись» (после статуса **Согласован**) |
| Версии текста | Панель «Версии» |
| Экспорт / email | Кнопки в шапке документа |

**Цепочка статусов:** Черновик → На согласовании → Согласован → **Подписан** → В архиве.

В архив можно отправить только **подписанный** документ. После подписи текст редактировать нельзя.

---

## Устранение неполадок

| Проблема | Что проверить |
|----------|----------------|
| Ошибка подключения к БД | PostgreSQL запущен, имя БД и пароль в `ConnectionStrings` |
| `dotnet build` — файл занят | Остановите запущенный `DocParseLab_Server` (другой терминал / диспетчер задач) |
| AI не работает | Секция `GigaChat` в appsettings, доступ к API Сбера |
| OCR не срабатывает | Файлы `*.traineddata` в `Resources/Tessdata`, `Ocr:Enabled: true` |
| Email не уходит | Секция `Smtp`, для Gmail — пароль приложения |
| Миграции | `cd DocParseLab.Server` → `dotnet ef database update` |

---

## Переменные окружения (примеры)

```powershell
$env:ConnectionStrings__DefaultConnection="Host=localhost;Port=5432;Database=pdf_parser_db;Username=postgres;Password=secret"
$env:Jwt__Key="production_secret_key_at_least_32_characters_long"
$env:ASPNETCORE_ENVIRONMENT="Production"
$env:ASPNETCORE_URLS="http://+:5000"
```