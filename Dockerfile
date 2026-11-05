# Backend ASP.NET Core 9 (API + SignalR + worker nền). Dùng cho Render / mọi nền tảng chạy Docker.
# Cấu hình truyền hoàn toàn qua biến môi trường (appsettings.json không có trong git), xem DEPLOY_RENDER.md.

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Restore trước để tận dụng cache layer khi chỉ đổi code
COPY EmergencyDispatch.Domain/EmergencyDispatch.Domain.csproj EmergencyDispatch.Domain/
COPY EmergencyDispatch.Application/EmergencyDispatch.Application.csproj EmergencyDispatch.Application/
COPY EmergencyDispatch.Infrastructure/EmergencyDispatch.Infrastructure.csproj EmergencyDispatch.Infrastructure/
COPY EmergencyDispatch.API/EmergencyDispatch.API.csproj EmergencyDispatch.API/
RUN dotnet restore EmergencyDispatch.API/EmergencyDispatch.API.csproj

COPY EmergencyDispatch.Domain/ EmergencyDispatch.Domain/
COPY EmergencyDispatch.Application/ EmergencyDispatch.Application/
COPY EmergencyDispatch.Infrastructure/ EmergencyDispatch.Infrastructure/
COPY EmergencyDispatch.API/ EmergencyDispatch.API/
RUN dotnet publish EmergencyDispatch.API/EmergencyDispatch.API.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# QuestPDF (xuất báo cáo PDF) cần fontconfig + font hệ thống trên Linux
RUN apt-get update \
    && apt-get install -y --no-install-recommends fontconfig fonts-dejavu-core \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_RUNNING_IN_CONTAINER=true
EXPOSE 8080

# Image aspnet có sẵn user không phải root
USER app
ENTRYPOINT ["dotnet", "EmergencyDispatch.API.dll"]
