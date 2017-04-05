FROM microsoft/dotnet:latest
COPY src/Imgur /app
WORKDIR /app
RUN ["dotnet", "restore"]
RUN ["dotnet", "build"]
RUN ["apt-get","update"]
RUN ["apt-get","install","fontconfig","ttf-dejavu","-y"]
ENV FONTCONFIG_PATH /etc/fonts
EXPOSE 5000/tcp
ENV ASPNETCORE_URLS https://*:5000
VOLUME /app
ENTRYPOINT ["dotnet", "run", "--server.urls", "http://*:5000"]