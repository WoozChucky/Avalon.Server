FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base

EXPOSE 8080

WORKDIR /app
COPY ./bin/Release/net8.0/publish/ .

ENTRYPOINT ["dotnet", "Avalon.Api.dll"]
