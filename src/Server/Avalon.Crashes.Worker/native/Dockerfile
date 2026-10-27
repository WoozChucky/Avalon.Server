# Native acceptance image; the worker runtime will consume the same tools/lock.
FROM python:3.15-slim@sha256:f786b2e19cf9bb54e96bfcd4c1b40c4c56336c22e60607ffd4b080ae5b5dee83
COPY src/Server/Avalon.Crashes.Worker/native /native
RUN bash /native/install.sh /opt/avalon-native
ENV PATH="/opt/avalon-native:${PATH}" HOME=/tmp
WORKDIR /repo
USER 65532:65532
