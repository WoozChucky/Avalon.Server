# Native acceptance image; the worker runtime will consume the same tools/lock.
FROM python:3.15-slim@sha256:65a65fc7b30b06cb6df4930fd4650697cb23f4c2698ab81fc87e4b031bc36928
COPY src/Server/Avalon.Crashes.Worker/native /native
RUN bash /native/install.sh /opt/avalon-native
ENV PATH="/opt/avalon-native:${PATH}" HOME=/tmp
WORKDIR /repo
USER 65532:65532
