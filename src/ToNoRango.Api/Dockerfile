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

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "ToNoRango.Api.dll"]
