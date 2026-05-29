# Booking Service — ASP.NET Core Pet Project

Практический проект для .NET-разработчиков, построенный вокруг реального микросервиса бронирования.

Сервис работает в связке с внешним Catalog Service через RabbitMQ (Rebus) — как в настоящей распределённой системе. Задачи спроектированы так, чтобы каждая затрагивала конкретный паттерн или проблему, с которыми сталкиваются на реальных проектах.

## Стек

- **C# 13**, **.NET 9**, **ASP.NET Core**
- **Entity Framework Core 9** + **PostgreSQL 16**
- **EF Core Migrations** — миграции БД
- **Rebus** + **RabbitMQ** — асинхронное взаимодействие между сервисами
- **Docker Compose** — локальная инфраструктура (БД, брокер, Catalog Service)
- **Swashbuckle** — OpenAPI / Swagger UI

## Зачем это делать

- Поработать с распределённой архитектурой — два сервиса, брокер сообщений, асинхронные потоки
- Решать задачи, которые встречаются на реальных проектах: race conditions, гарантии доставки, кэширование, аудит
- Подготовиться к собеседованиям — задачи покрывают темы, про которые могут спрашивать

## Задачи

Каждая задача — самостоятельная фича или исправление, которое добавляется поверх существующего сервиса. Задачи можно выполнять в любом порядке, но нумерация отражает рекомендуемую последовательность.

| #  | Тема | Что делать |
|----|------|------------|
| 01 | **Compensating Transaction** | Ввести промежуточный статус `CancellationPending` для безопасной отмены бронирования. Реализовать rollback при ошибке Catalog Service (DLQ). |
| 02 | **Endpoint статистики** | `GET /api/booking/statistics` — агрегации на стороне БД: общее количество, разбивка по статусам, топ-5 ресурсов. |
| 03 | **Race conditions и зависшие отмены** | Обработать гонку между отменой и подтверждением. Фоновый `IHostedService` для повторной отправки зависших отмен. |
| 04 | **Аудит-лог** | Таблица истории изменений статуса. Endpoint с пагинацией `GET /api/bookings/{id}/history`. |
| 05 | **Идемпотентность** | Таблица `processed_events` для защиты от повторной обработки сообщений из брокера. |
| 06 | **Domain Events** | Публикация `BookingStatusChangedEvent` в RabbitMQ при каждом изменении статуса. |
| 07 | **Transactional Outbox** | Сохранение событий в outbox-таблицу в одной транзакции с бизнес-данными. Фоновая отправка с retry. |
| 08 | **Оптимизация запросов** | Анализ и добавление индексов. Интеграционные тесты с Testcontainers для проверки через `pg_indexes`. |
| 09 | **Notification Service** | HTTP-клиент с Polly Retry к внешнему сервису уведомлений. Graceful degradation. WireMock для тестов. |
| 10 | **Кэширование (Memory Cache)** | `IMemoryCache` для статистики. Инвалидация при изменении данных. |

## Быстрый старт

```bash
# Клонировать репозиторий
git clone https://github.com/devdad-ru/booking-service-csharp.git
cd booking-service-csharp

# Поднять инфраструктуру
docker compose up -d

# Запустить сервис (из директории BookingService/)
dotnet run --project BookingService --launch-profile Development
```

После запуска:
- Booking Service: `http://localhost:8001`
- Swagger UI: `http://localhost:8001/swagger`
- RabbitMQ Management: `http://localhost:15672` (admin/admin)

### Локальная разработка без Docker

Вы можете запустить только инфраструктуру в Docker, а сам сервис — локально.

**1. Поднять инфраструктуру:**
```bash
docker compose up -d booking-service-db rabbitmq catalog-service catalog-db catalog-migrations
```

**2. Применить миграции и запустить сервис:**
```bash
cd BookingService
dotnet run
```

Миграции применяются автоматически при старте (через `MigrateAsync()` в `Program.cs`).

Строка подключения для локальной разработки задана в `BookingService/appsettings.Development.json`:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5433;Database=booking_service;Username=booking_admin;Password=admin_booking"
  }
}
```

### Добавление новой миграции EF Core

```bash
cd BookingService
dotnet ef migrations add <НазваниеМиграции>
dotnet ef database update
```

## Как сдать задание

### 1. Сделайте форк

Перейдите в репозиторий [devdad-ru/booking-service-csharp](https://github.com/devdad-ru/booking-service-csharp) и нажмите кнопку **Fork** в правом верхнем углу.

### 2. Разрешите запуск GitHub Actions

В вашем форке перейдите во вкладку **Actions** и нажмите **I understand my workflows, go ahead and enable them**. Это необходимо для автоматической проверки PR.

### 3. Клонируйте свой форк

```bash
git clone https://github.com/<ваш-username>/booking-service-csharp.git
cd booking-service-csharp
```

### 4. Создайте ветку для задачи
Например: tasks/task1 (или task01)
Триггеру автопроверки важно корректное название ветки и номера задачи (task1 или task01) другие названия веток не затриггерят проверку.
(каждую последующую ветку с новой задачей нужно создавать от предыдущей ветки задачи: task1 от master, task2 от task1 и т.д.)
```bash
git checkout -b tasks/task1
```

### 5. Выполните задачу и зафиксируйте изменения

```bash
git add .
git commit -m "Решение задачи <номер>"
git push origin tasks/task1
```

### 6. Создайте Pull Request в своём форке

Откройте свой форк на GitHub и создайте **Pull Request** в ветку `master` **вашего форка** (не в родительский репозиторий).

> При создании PR убедитесь, что base repository — это **ваш форк**, а не `devdad-ru/booking-service-csharp`.

PR можно создать через GitHub в браузере или прямо из IDE, если в ней подключен ваш GitHub-аккаунт и есть функция создания Pull Request.

После создания PR автоматически запустится workflow, который отправит ваше решение на проверку.

### 7. Приложите ссылку на PR в ответ

Сразу после создания PR скопируйте ссылку на него и отправьте её в ответе на задание на платформе.

Пример ссылки:

```text
https://github.com/<ваш-username>/booking-service-csharp/pull/<номер-pr>
```

Важно: ссылку нужно отправить в течение **5 минут** после создания или обновления PR. Если ссылка не будет найдена в ответе на задание за это время, проверка не запустится.

Если вы не успели отправить ссылку вовремя, отправьте ссылку в ответе на задание и сделайте новый `push` в ту же ветку PR.

### 8. Если задание требует доработки

Не создавайте новый PR. Внесите правки в той же ветке, сделайте новый commit и `push`.

После этого нажмите кнопку "Редактировать" и "Отправить на проверку", чтобы запустить повторную проверку по уже вложенному PR.

## Архитектура

```
┌──────────┐      REST       ┌─────────────────┐     RabbitMQ     ┌─────────────────┐
│  Client  │ ──────────────> │ Booking Service  │ <─────────────> │ Catalog Service  │
└──────────┘                 │  (.NET/ASP.NET)  │                 │    (.NET/C#)     │
                             └────────┬─────────┘                 └────────┬─────────┘
                                      │                                    │
                                      v                                    v
                               ┌──────────────┐                    ┌──────────────┐
                               │ PostgreSQL    │                    │ PostgreSQL   │
                               │ (Booking DB)  │                    │ (Catalog DB) │
                               └──────────────┘                    └──────────────┘
```

Booking Service принимает запросы на бронирование, отправляет команды в Catalog Service через RabbitMQ (Rebus) и обрабатывает ответы (подтверждение/отказ). Catalog Service управляет доступностью ресурсов и поставляется как готовый Docker-образ — его код менять не нужно.

### Структура проекта

```
booking-service-csharp/
├── BookingService.sln
├── docker-compose.yml
├── docker-compose-mount/
│   ├── BookingService.Host/          # Конфигурация Booking Service для Docker
│   └── BookingService.Catalog.Host/  # Конфигурация Catalog Service для Docker
├── logs/
└── BookingService/
    ├── BookingService.csproj
    ├── Program.cs                    # Точка входа, регистрация сервисов, middleware
    ├── Controllers/
    │   └── BookingController.cs      # REST endpoints
    ├── Dto/
    │   ├── Request/                  # Входящие DTO
    │   └── Response/                 # Исходящие DTO
    ├── Entities/
    │   ├── Booking.cs                # EF Entity с доменной логикой
    │   └── BookingStatus.cs          # Enum статусов
    ├── Exceptions/
    │   └── BusinessException.cs      # Бизнес-ошибки → HTTP 400
    ├── Configuration/
    │   ├── ICurrentDateTimeProvider.cs
    │   └── RabbitMqSettings.cs
    ├── Infrastructure/
    │   ├── Data/
    │   │   ├── BookingDbContext.cs
    │   │   ├── BookingRepository.cs
    │   │   └── Migrations/
    │   └── Messaging/
    │       ├── Contracts/            # RabbitMQ message contracts
    │       ├── BookingEventPublisher.cs
    │       ├── BookingEventsHandler.cs
    │       └── CancelBookingErrorsHandler.cs
    ├── Services/
    │   └── BookingService.cs         # Бизнес-логика
    └── Mappers/
        └── BookingMapper.cs
```

### Взаимодействие через RabbitMQ (Rebus)

Сервис использует библиотеку [Rebus](https://github.com/rebus-org/Rebus) — ту же, что и Catalog Service.

**Исходящие команды (Booking Service → Catalog Service):**

| Тип сообщения | Назначение |
|---------------|------------|
| `CreateBookingJobRequest` | Зарезервировать ресурс в Catalog Service |
| `CancelBookingJobByRequestIdRequest` | Освободить ресурс в Catalog Service |

**Входящие события (Catalog Service → Booking Service):**

| Тип сообщения | Назначение |
|---------------|------------|
| `BookingJobConfirmed` | Ресурс успешно зарезервирован → статус `Confirmed` |
| `BookingJobDenied` | Ресурс недоступен → статус `Cancelled` |

**Обмен сообщениями:**
- Topic exchange: `booking-service-topics`
- Входящая очередь Booking Service: `booking-service`

### Жизненный цикл бронирования

```
POST /api/booking
       │
       ▼
 Booking.Create()         ← валидация дат и идентификаторов
 status = AwaitConfirmation
       │
       ▼
 Publish CreateBookingJobRequest → RabbitMQ → Catalog Service
       │
       ▼ (асинхронно)
  ┌────┴────┐
  │         │
Confirmed  Denied
  │         │
  ▼         ▼
booking.   booking.
Confirm()  Cancel()
```

## REST API

| Метод | Путь | Описание |
|-------|------|----------|
| `POST` | `/api/booking` | Создать бронирование → возвращает `id` |
| `GET` | `/api/booking/{id}` | Получить бронирование по ID |
| `POST` | `/api/booking/by-filter` | Список бронирований с фильтрацией и пагинацией |
| `GET` | `/api/booking/{id}/status` | Получить только статус бронирования |
| `POST` | `/api/booking/{id}/cancel` | Отменить бронирование |

Полная документация доступна через Swagger UI: `http://localhost:8001/swagger`
