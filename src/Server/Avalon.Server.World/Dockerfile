FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
EXPOSE 21001
WORKDIR /app
COPY ./out/ .
ENTRYPOINT ["dotnet", "AvalonWorldServer.dll"]
