# doesn't support microsoft/aspnetcore runtime image, fails with https://github.com/CoreCompat/CoreCompat/issues/3
FROM microsoft/dotnet:latest
COPY src /app
WORKDIR /app

RUN dotnet restore --configfile ../NuGet.Config
RUN dotnet publish -c Release -o /app/out
RUN apt-get update
RUN apt-get install -y fontconfig ttf-dejavu
ENV FONTCONFIG_PATH /etc/fonts

ENV ASPNETCORE_URLS http://*:5000
WORKDIR /app/out
ENTRYPOINT ["dotnet", "Imgur.dll"]
