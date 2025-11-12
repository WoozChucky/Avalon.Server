FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base

WORKDIR /app
COPY ./bin/Release/net10.0/publish/ .

ENTRYPOINT ["dotnet", "AvalonWorldServer.dll"]
