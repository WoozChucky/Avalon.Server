FROM mcr.microsoft.com/dotnet/aspnet:11.0 AS base

EXPOSE 8080

WORKDIR /app
COPY ./bin/Release/net10.0/publish/ .

ENTRYPOINT ["dotnet", "Avalon.Api.dll"]
