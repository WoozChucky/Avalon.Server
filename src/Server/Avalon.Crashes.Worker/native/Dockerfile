# Native acceptance image; the worker runtime will consume the same tools/lock.
FROM python:3.15-slim@sha256:94d3426f1def7db4a106a232ac121bfd975393e5dfd40a619b955fae45fe7913
COPY src/Server/Avalon.Crashes.Worker/native /native
RUN bash /native/install.sh /opt/avalon-native
ENV PATH="/opt/avalon-native:${PATH}" HOME=/tmp
WORKDIR /repo
USER 65532:65532
