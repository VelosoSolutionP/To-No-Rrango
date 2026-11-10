FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props global.json ./
COPY src/ToNoRango.Api/ToNoRango.Api.csproj src/ToNoRango.Api/
COPY src/ToNoRango.Application/ToNoRango.Application.csproj src/ToNoRango.Application/
COPY src/ToNoRango.Domain/ToNoRango.Domain.csproj src/ToNoRango.Domain/
COPY src/ToNoRango.Infrastructure/ToNoRango.Infrastructure.csproj src/ToNoRango.Infrastructure/
COPY src/ToNoRango.SharedKernel/ToNoRango.SharedKernel.csproj src/ToNoRango.SharedKernel/

RUN dotnet restore src/ToNoRango.Api/ToNoRango.Api.csproj

COPY src/ src/
RUN dotnet publish src/ToNoRango.Api/ToNoRango.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

# Voz das respostas a áudio: piper (pt_BR) gratuito, rodando dentro da API.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS voice
RUN apt-get update && apt-get install -y --no-install-recommends curl ca-certificates && rm -rf /var/lib/apt/lists/*
RUN mkdir -p /opt/voz \
    && curl -fsSL https://github.com/rhasspy/piper/releases/download/2023.11.14-2/piper_linux_x86_64.tar.gz | tar xz -C /opt \
    && curl -fsSL -o /opt/voz/pt_BR-faber-medium.onnx https://huggingface.co/rhasspy/piper-voices/resolve/v1.0.0/pt/pt_BR/faber/medium/pt_BR-faber-medium.onnx \
    && curl -fsSL -o /opt/voz/pt_BR-faber-medium.onnx.json https://huggingface.co/rhasspy/piper-voices/resolve/v1.0.0/pt/pt_BR/faber/medium/pt_BR-faber-medium.onnx.json

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
# ffmpeg transforma a fala do piper em nota de voz do WhatsApp (ogg/opus).
RUN apt-get update && apt-get install -y --no-install-recommends ffmpeg && rm -rf /var/lib/apt/lists/*
COPY --from=voice /opt/piper /opt/piper
COPY --from=voice /opt/voz /opt/voz
ENV Voice__PiperPath=/opt/piper/piper \
    Voice__Model=/opt/voz/pt_BR-faber-medium.onnx
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "ToNoRango.Api.dll"]
