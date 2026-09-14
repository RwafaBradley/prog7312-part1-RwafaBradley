FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY SmartX.Desktop.sln ./
COPY src/SmartX.Core/SmartX.Core.csproj src/SmartX.Core/
COPY src/SmartX.Api/SmartX.Api.csproj src/SmartX.Api/
RUN dotnet restore src/SmartX.Api/SmartX.Api.csproj

COPY src/SmartX.Core/ src/SmartX.Core/
COPY src/SmartX.Api/ src/SmartX.Api/
RUN dotnet publish src/SmartX.Api/SmartX.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./

RUN mkdir -p /app/App_Data/attachments && chown -R $APP_UID /app/App_Data
VOLUME ["/app/App_Data"]
USER $APP_UID

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    SmartX__AttachmentRoot=/app/App_Data/attachments

EXPOSE 8080

HEALTHCHECK --interval=20s --timeout=3s --start-period=10s --retries=3 \
    CMD ["dotnet", "SmartX.Api.dll", "--healthcheck"]

ENTRYPOINT ["dotnet", "SmartX.Api.dll"]
