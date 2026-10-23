# Native acceptance image; the worker runtime will consume the same tools/lock.
FROM python:3.12-slim@sha256:a0f57064cbdcd6285fa45f51237ab0b35ea8ebaed06a1f5973e89e19dfb86704
COPY src/Server/Avalon.Crashes.Worker/native /native
RUN bash /native/install.sh /opt/avalon-native
ENV PATH="/opt/avalon-native:${PATH}" HOME=/tmp
WORKDIR /repo
USER 65532:65532
