# Multi-stage build для .NET 9 ASP.NET Core приложения

# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /build

# Копируем файлы проекта и восстанавливаем зависимости (кэш слоя)
COPY BookingService/BookingService.csproj BookingService/
RUN dotnet restore BookingService/BookingService.csproj

# Копируем весь исходный код и публикуем
COPY . .
RUN dotnet publish BookingService/BookingService.csproj -c Release -o /app/publish --no-restore

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Создаём непривилегированного пользователя
RUN groupadd -r appuser && useradd -r -g appuser appuser
USER appuser

COPY --from=build /app/publish .

EXPOSE 8080

ENTRYPOINT ["dotnet", "BookingService.dll"]
