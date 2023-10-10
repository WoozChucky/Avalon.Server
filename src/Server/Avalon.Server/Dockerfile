FROM mcr.microsoft.com/dotnet/runtime:8.0 AS base

WORKDIR /app
COPY ./bin/Release/net8.0/publish/ .

ENTRYPOINT ["dotnet", "Avalon.dll"]
