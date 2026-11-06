# Context: this worker project, after dotnet publish -o ./out (never repository-root out/).
# Reuse the canonical verified releases and licenses; no second set of native pins.
FROM python:3.15-slim@sha256:94d3426f1def7db4a106a232ac121bfd975393e5dfd40a619b955fae45fe7913 AS native
COPY native/ /native/
RUN bash /native/install.sh /opt/avalon-native

FROM mcr.microsoft.com/dotnet/aspnet:11.0@sha256:e1ba5ef29b9c0f51b4236cbd25268b84a1e15f8fb1dca21f141bd0358903a1f2
# util-linux provides setsid/prlimit; libstdc++/libgcc and OpenSSL satisfy the locked GNU tools.
RUN apt-get update && apt-get install -y --no-install-recommends util-linux libstdc++6 libgcc-s1 libssl3t64 libgssapi-krb5-2 ca-certificates \
    && rm -rf /var/lib/apt/lists/*
COPY --from=native /opt/avalon-native/ /opt/avalon-native/
WORKDIR /app
COPY out/ ./
ENV ASPNETCORE_ENVIRONMENT=Production DOTNET_EnableDiagnostics=0
EXPOSE 8080
USER 1654:1654
ENTRYPOINT ["dotnet", "Avalon.Crashes.Worker.dll"]
CMD ["run"]
