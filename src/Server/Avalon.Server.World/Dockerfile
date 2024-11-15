FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base

WORKDIR /app
COPY ./bin/Release/net9.0/publish/ .

ENTRYPOINT ["dotnet", "AvalonWorldServer.dll"]
