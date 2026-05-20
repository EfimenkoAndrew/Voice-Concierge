# syntax=docker/dockerfile:1

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props ./
COPY src/VoiceConcierge.Contracts/VoiceConcierge.Contracts.csproj src/VoiceConcierge.Contracts/
COPY src/VoiceConcierge.Agent/VoiceConcierge.Agent.csproj src/VoiceConcierge.Agent/
RUN dotnet restore src/VoiceConcierge.Agent/VoiceConcierge.Agent.csproj -a $TARGETARCH --locked-mode
COPY src/VoiceConcierge.Contracts/ src/VoiceConcierge.Contracts/
COPY src/VoiceConcierge.Agent/ src/VoiceConcierge.Agent/
RUN dotnet publish src/VoiceConcierge.Agent/VoiceConcierge.Agent.csproj -a $TARGETARCH --no-restore -c Release -o /app /p:UseAppHost=false

FROM --platform=$BUILDPLATFORM curlimages/curl:8.11.1 AS models
USER root
ARG WHISPER_REV=5359861c739e955e79d9a303bcbc70fb988958b1
RUN mkdir -p /m && \
    curl -fsSL "https://github.com/snakers4/silero-vad/raw/v5.1.2/src/silero_vad/data/silero_vad.onnx" -o /m/silero_vad.onnx && \
    curl -fsSL "https://huggingface.co/ggerganov/whisper.cpp/resolve/${WHISPER_REV}/ggml-base.en.bin" -o /m/ggml-base.en.bin

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
RUN apt-get update \
 && apt-get install -y --no-install-recommends libgomp1 wget \
 && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app ./
COPY --from=models /m /app/models
RUN chown -R $APP_UID /app
ENV STT_MODEL_DIR=/app/models VAD_MODEL_DIR=/app/models
EXPOSE 9090
USER $APP_UID
HEALTHCHECK --interval=10s --timeout=5s --retries=12 \
    CMD wget -qO- http://localhost:9090/healthz >/dev/null || exit 1
ENTRYPOINT ["dotnet", "VoiceConcierge.Agent.dll"]
