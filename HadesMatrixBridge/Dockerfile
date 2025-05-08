# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/runtime:9.0 AS base
USER $APP_UID
WORKDIR /app


# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
# Define version arguments with default values
ARG VERSION=1.0.0
ARG ASSEMBLY_VERSION=1.0.0.0
ARG FILE_VERSION=1.0.0.0
ARG INFORMATIONAL_VERSION=1.0.0

WORKDIR /src
COPY ["HadesMatrixBridge/HadesMatrixBridge.csproj", "HadesMatrixBridge/"]
RUN dotnet restore "./HadesMatrixBridge/HadesMatrixBridge.csproj"
COPY . .
WORKDIR "/src/HadesMatrixBridge"
RUN dotnet build "./HadesMatrixBridge.csproj" -c $BUILD_CONFIGURATION -o /app/build \
    -p:Version=$VERSION \
    -p:AssemblyVersion=$ASSEMBLY_VERSION \
    -p:FileVersion=$FILE_VERSION \
    -p:InformationalVersion=$INFORMATIONAL_VERSION

# This stage is used to publish the service project as a self-contained application
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
# Pass version arguments from build stage
ARG VERSION
ARG ASSEMBLY_VERSION
ARG FILE_VERSION
ARG INFORMATIONAL_VERSION
# Publish as self-contained for Linux x64
RUN dotnet publish "HadesMatrixBridge.csproj" -c $BUILD_CONFIGURATION -o /app/publish \
    --self-contained true \
    -r linux-x64 \
    -p:PublishSingleFile=false \
    -p:PublishTrimmed=false \
    -p:Version=$VERSION \
    -p:AssemblyVersion=$ASSEMBLY_VERSION \
    -p:FileVersion=$FILE_VERSION \
    -p:InformationalVersion=$INFORMATIONAL_VERSION

# Use a minimal base image for the final stage
FROM mcr.microsoft.com/dotnet/runtime-deps:9.0 AS final
WORKDIR /app

# Create a non-root user to run the application
RUN adduser --disabled-password --home /app --gecos '' appuser && \
    chown -R appuser:appuser /app
USER appuser

# Copy the published application
COPY --from=publish --chown=appuser:appuser /app/publish .

# Create a directory for data
RUN mkdir -p /app/data && \
    chown -R appuser:appuser /app/data

# Expose the port used by the application
EXPOSE 7000
EXPOSE 9000

# Set the entry point to the executable
ENTRYPOINT ["./HadesMatrixBridge"]
