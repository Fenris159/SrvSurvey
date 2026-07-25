# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS restore
WORKDIR /source

COPY global.json SrvSurvey.CrossPlatform.slnx ./
COPY src/SrvSurvey.Core/SrvSurvey.Core.csproj src/SrvSurvey.Core/
COPY src/SrvSurvey.Desktop/SrvSurvey.Desktop.csproj src/SrvSurvey.Desktop/
COPY tests/SrvSurvey.Core.Tests/SrvSurvey.Core.Tests.csproj tests/SrvSurvey.Core.Tests/
COPY tests/SrvSurvey.Desktop.Tests/SrvSurvey.Desktop.Tests.csproj tests/SrvSurvey.Desktop.Tests/

RUN dotnet restore SrvSurvey.CrossPlatform.slnx \
    && dotnet restore src/SrvSurvey.Desktop/SrvSurvey.Desktop.csproj --runtime linux-x64

FROM restore AS test
COPY src/ src/
COPY tests/ tests/
COPY docs/ docs/
COPY data/ data/
COPY SrvSurvey/ SrvSurvey/
RUN dotnet build SrvSurvey.CrossPlatform.slnx --configuration Release --no-restore \
    && dotnet test SrvSurvey.CrossPlatform.slnx --configuration Release --no-build --no-restore

FROM test AS publish
RUN dotnet publish src/SrvSurvey.Desktop/SrvSurvey.Desktop.csproj \
    --configuration Release \
    --runtime linux-x64 \
    --self-contained true \
    --no-restore \
    --output /artifacts/linux-x64 \
    -p:DebugType=None \
    -p:DebugSymbols=false

# Export the publish directory with:
# docker build --output type=local,dest=./artifacts/docker .
FROM scratch AS export
COPY --from=publish /artifacts/linux-x64/ /
