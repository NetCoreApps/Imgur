FROM microsoft/aspnetcore-build:2.0 AS build-env
COPY src/Imgur /app
WORKDIR /app

RUN dotnet restore --configfile ../NuGet.Config
RUN dotnet publish -c Release -o out
RUN apt-get update
RUN apt-get install fontconfig ttf-dejavu -y
ENV FONTCONFIG_PATH /etc/fonts

# Build runtime image
FROM microsoft/aspnetcore:2.0
WORKDIR /app
COPY --from=build-env /app/out .
ENV ASPNETCORE_URLS http://*:5000
VOLUME /app
ENTRYPOINT ["dotnet", "Imgur.dll"]
