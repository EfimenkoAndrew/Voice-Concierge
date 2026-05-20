# syntax=docker/dockerfile:1

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props ./
COPY src/VoiceConcierge.Contracts/VoiceConcierge.Contracts.csproj src/VoiceConcierge.Contracts/
COPY src/VoiceConcierge.Api/VoiceConcierge.Api.csproj src/VoiceConcierge.Api/
RUN dotnet restore src/VoiceConcierge.Api/VoiceConcierge.Api.csproj -a $TARGETARCH --locked-mode
COPY src/VoiceConcierge.Contracts/ src/VoiceConcierge.Contracts/
COPY src/VoiceConcierge.Api/ src/VoiceConcierge.Api/
RUN dotnet publish src/VoiceConcierge.Api/VoiceConcierge.Api.csproj -a $TARGETARCH --no-restore -c Release -o /app /p:UseAppHost=false

FROM --platform=$BUILDPLATFORM curlimages/curl:8.11.1 AS models
USER root
ARG BGE_REV=5c38ec7c405ec4b44b94cc5a9bb96e735b38267a
RUN mkdir -p /m && \
    curl -fsSL "https://huggingface.co/BAAI/bge-small-en-v1.5/resolve/${BGE_REV}/onnx/model.onnx" -o /m/model.onnx && \
    curl -fsSL "https://huggingface.co/BAAI/bge-small-en-v1.5/resolve/${BGE_REV}/vocab.txt" -o /m/vocab.txt

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
RUN apt-get update \
 && apt-get install -y --no-install-recommends curl \
 && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app ./
COPY --from=models /m /app/models/bge-small-en-v1.5
RUN chown -R $APP_UID /app/models
ENV ASPNETCORE_URLS=http://+:8080 \
    EMBEDDING_MODEL_DIR=/app/models/bge-small-en-v1.5
EXPOSE 8080
USER $APP_UID
HEALTHCHECK --interval=5s --timeout=5s --retries=20 \
    CMD curl -fsS http://localhost:8080/health || exit 1
ENTRYPOINT ["dotnet", "VoiceConcierge.Api.dll"]
