FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
EXPOSE 8080
WORKDIR /app
COPY ./out/ .
ENTRYPOINT ["dotnet", "Avalon.Api.dll"]
