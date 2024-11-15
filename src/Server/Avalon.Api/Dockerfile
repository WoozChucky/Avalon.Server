FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base

EXPOSE 8080

WORKDIR /app
COPY ./bin/Release/net9.0/publish/ .

ENTRYPOINT ["dotnet", "Avalon.Api.dll"]
