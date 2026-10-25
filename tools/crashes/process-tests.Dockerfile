# Isolated real-Linux test runner only. Task 14 owns the product worker image.
FROM avalon-crashes-native:task1 AS native
FROM mcr.microsoft.com/dotnet/sdk:10.0
COPY --from=native /opt/avalon-native /opt/avalon-native
WORKDIR /repo
