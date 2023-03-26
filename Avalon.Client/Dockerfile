FROM mcr.microsoft.com/dotnet/runtime:7.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /src
COPY ["Avalon.Client/Avalon.Client.csproj", "Avalon.Client/"]
RUN dotnet restore "Avalon.Client/Avalon.Client.csproj"
COPY . .
WORKDIR "/src/Avalon.Client"
RUN dotnet build "Avalon.Client.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Avalon.Client.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Avalon.Client.dll"]
