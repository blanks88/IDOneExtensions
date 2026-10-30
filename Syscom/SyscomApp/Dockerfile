FROM mcr.microsoft.com/dotnet/runtime:10.0 AS base
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Syscom/SyscomApp/SyscomApp.csproj", "Syscom/SyscomApp/"]
COPY ["Syscom/Syscom/Syscom.csproj", "Syscom/Syscom/"]
COPY ["IDOneRepository/IDOneRepository.csproj", "IDOneRepository/"]
COPY ["Utils/Utils.csproj", "Utils/"]
RUN dotnet restore "Syscom/SyscomApp/SyscomApp.csproj"
COPY . .
WORKDIR "/src/Syscom/SyscomApp"
RUN dotnet build "./SyscomApp.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./SyscomApp.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

RUN mkdir -p /app/publish/bin \
    && cp /src/Syscom/SyscomApp/run-syscom.sh /app/publish/bin/run-syscom \
    && chmod 755 /app/publish/bin/run-syscom

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
CMD ["/app/bin/run-syscom"]
