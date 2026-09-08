FROM mcr.microsoft.com/dotnet/aspnet:11.0 AS base

WORKDIR /app
COPY ./bin/Release/net10.0/publish/ .

ENTRYPOINT ["dotnet", "AvalonAuthServer.dll"]
