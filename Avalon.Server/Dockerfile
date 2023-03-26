FROM mcr.microsoft.com/dotnet/runtime:7.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /src
COPY ["Avalon.Server/Avalon.Server.csproj", "Avalon.Server/"]
RUN dotnet restore "Avalon.Server/Avalon.Server.csproj"
COPY . .
WORKDIR "/src/Avalon.Server"
RUN dotnet build "Avalon.Server.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Avalon.Server.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Avalon.Server.dll"]
