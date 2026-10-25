# Native acceptance image; the worker runtime will consume the same tools/lock.
FROM python:3.15-slim@sha256:533b0b2f9b5d46fd097091cf826f27902f1d6c3bca714b6dc74551356aa97a51
COPY src/Server/Avalon.Crashes.Worker/native /native
RUN bash /native/install.sh /opt/avalon-native
ENV PATH="/opt/avalon-native:${PATH}" HOME=/tmp
WORKDIR /repo
USER 65532:65532
