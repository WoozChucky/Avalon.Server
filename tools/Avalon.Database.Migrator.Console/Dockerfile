FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base

WORKDIR /app
COPY ./bin/Release/net8.0/publish/ .

ENTRYPOINT ["dotnet", "Avalon.Database.Migrator.Console.dll"]
