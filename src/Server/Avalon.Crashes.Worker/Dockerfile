# Context: this worker project, after dotnet publish -o ./out (never repository-root out/).
# Reuse the canonical verified releases and licenses; no second set of native pins.
FROM python:3.15-slim@sha256:533b0b2f9b5d46fd097091cf826f27902f1d6c3bca714b6dc74551356aa97a51 AS native
COPY native/ /native/
RUN bash /native/install.sh /opt/avalon-native

FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:8af87eba5e7cdafb1514d7388be287365da97c132509e84dfe252ffd552398ca
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
