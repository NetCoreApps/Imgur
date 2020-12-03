FROM mcr.microsoft.com/dotnet/sdk:5.0 AS build
WORKDIR /app

# copy csproj and restore as distinct layers
COPY src/*.sln .
COPY src/Imgur/*.csproj ./Imgur/
RUN dotnet restore

# copy everything else and build app
COPY src/Imgur/. ./Imgur/
WORKDIR /app/Imgur
RUN dotnet publish -c Release -o out

FROM mcr.microsoft.com/dotnet/aspnet:5.0 AS runtime
WORKDIR /app
COPY --from=build /app/Imgur/out ./
ENV ASPNETCORE_URLS http://*:5000
ENTRYPOINT ["dotnet", "Imgur.dll"]
