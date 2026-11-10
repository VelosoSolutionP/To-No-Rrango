# Transcrição de áudio local e gratuita: whisper.cpp (modelo base, português).
# Nada sai do servidor e não há custo por mensagem.
FROM debian:bookworm-slim AS build
RUN apt-get update && apt-get install -y --no-install-recommends build-essential cmake curl ca-certificates \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /src
ARG WHISPER_VERSION=1.8.2
RUN curl -fsSL "https://github.com/ggml-org/whisper.cpp/archive/refs/tags/v${WHISPER_VERSION}.tar.gz" | tar xz --strip-components=1
# Sem AVX/AVX2/FMA: o servidor atual não tem essas instruções; assim roda em qualquer x86-64.
RUN cmake -B build -DCMAKE_BUILD_TYPE=Release -DBUILD_SHARED_LIBS=OFF -DGGML_NATIVE=OFF -DWHISPER_BUILD_TESTS=OFF \
      -DGGML_AVX=OFF -DGGML_AVX2=OFF -DGGML_FMA=OFF -DGGML_F16C=OFF -DGGML_AVX512=OFF -DGGML_BMI2=OFF \
    && cmake --build build -j"$(nproc)" --target whisper-server
RUN curl -fsSL -o /ggml-base.bin "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin"

FROM debian:bookworm-slim
# ffmpeg converte o ogg/opus do WhatsApp para o formato que o whisper lê (--convert).
RUN apt-get update && apt-get install -y --no-install-recommends ffmpeg libgomp1 curl \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /src/build/bin/whisper-server /usr/local/bin/whisper-server
COPY --from=build /ggml-base.bin /models/ggml-base.bin
WORKDIR /tmp
EXPOSE 8178
HEALTHCHECK --interval=30s --timeout=5s --retries=3 CMD curl -fs http://127.0.0.1:8178/ >/dev/null || exit 1
ENTRYPOINT ["whisper-server", "-m", "/models/ggml-base.bin", "-l", "pt", "--convert", "--host", "0.0.0.0", "--port", "8178"]
CMD ["-t", "8"]
